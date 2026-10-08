using AttendanceBot.Core.Interfaces;
using AttendanceBot.Core.Models;
using AttendanceBot.Core.Services;
using AttendanceBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AttendanceBot.Infrastructure.Services;

/// <summary>Owns the single scheduler loop and serial attendance-run queue for the desktop process.</summary>
public sealed class AttendanceAutomationService(
    IDbContextFactory<AttendanceDbContext> dbFactory,
    IEnumerable<IAttendanceProvider> providers,
    AutomationRuntimeService runtime,
    ITelegramService telegram)
{
    private readonly SemaphoreSlim _lifecycle = new(1, 1);
    private readonly SemaphoreSlim _queueLock = new(1, 1);
    private readonly IAttendanceProvider[] _providers = providers.OrderBy(p => p.Kind).ToArray();
    private CancellationTokenSource? _schedulerCancellation;
    private CancellationTokenSource? _activeQueueCancellation;
    private Task? _schedulerTask;
    private DateOnly? _missedDate;
    private AttendanceType? _missedType;
    private static readonly SchedulerService Scheduler = new();

    public AutomationSnapshot Snapshot => runtime.Snapshot;

    public async Task StartAsync(bool automatic = false, CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            if (_schedulerTask is { IsCompleted: false }) return;
            await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
            if (!await db.Database.CanConnectAsync(cancellationToken)) throw new InvalidOperationException("Database tidak dapat diakses.");
            var schedules = await db.Schedules.AsNoTracking().Where(s => s.Enabled).ToListAsync(cancellationToken);
            if (schedules.Count == 0) throw new InvalidOperationException("Tidak ada jadwal aktif.");
            if (runtime.Snapshot.State is not (AutomationState.Running or AutomationState.Processing)) runtime.Start();
            _schedulerCancellation?.Dispose();
            _schedulerCancellation = new CancellationTokenSource();
            _schedulerTask = SchedulerLoopAsync(_schedulerCancellation.Token);
            await AuditAsync("AUTOMATION_STARTED", automatic ? "Automation started automatically." : "Automation started.", cancellationToken);
        }
        finally { _lifecycle.Release(); }
    }

    public async Task StopAsync(CancellationToken cancellationToken = default)
    {
        await _lifecycle.WaitAsync(cancellationToken);
        try
        {
            runtime.Stop();
            _missedDate = null; _missedType = null;
            _activeQueueCancellation?.Cancel();
            _schedulerCancellation?.Cancel();
            try { if (_schedulerTask is not null) await _schedulerTask.WaitAsync(TimeSpan.FromSeconds(10), cancellationToken); }
            catch (OperationCanceledException) { }
            catch (TimeoutException) { }
            await AuditAsync("AUTOMATION_STOPPED", "Automation stopped.", cancellationToken);
        }
        finally { _lifecycle.Release(); }
    }

    public async Task PauseAsync(CancellationToken cancellationToken = default)
    {
        if (runtime.Pause()) await AuditAsync("AUTOMATION_PAUSED", "Automation paused.", cancellationToken);
    }

    public async Task ResumeAsync(CancellationToken cancellationToken = default)
    {
        if (!runtime.Resume()) return;
        await AuditAsync("AUTOMATION_RESUMED", "Automation resumed.", cancellationToken);
        if (_missedType is { } type && _missedDate == DateOnly.FromDateTime(DateTime.Now))
        {
            _missedDate = null; _missedType = null;
            _ = RunQueueAsync(type, DateTime.Now, true, _schedulerCancellation?.Token ?? CancellationToken.None);
        }
    }

    public Task RunManualAsync(AttendanceType type, CancellationToken cancellationToken = default) =>
        RunQueueAsync(type, DateTime.Now, false, cancellationToken);

    public async Task<(AttendanceType? Type, DateTimeOffset? At)> GetNextScheduleAsync(CancellationToken cancellationToken = default)
    {
        await using var db = await dbFactory.CreateDbContextAsync(cancellationToken);
        var schedules = await db.Schedules.AsNoTracking().Where(s => s.Enabled).ToListAsync(cancellationToken);
        var matches = schedules.Select(s => (Schedule: s, At: Scheduler.GetNextOccurrence([s], DateTimeOffset.Now)))
            .Where(x => x.At.HasValue).OrderBy(x => x.At).FirstOrDefault();
        return matches.Schedule is null ? (null, null) : (matches.Schedule.AttendanceType, matches.At);
    }

    private async Task SchedulerLoopAsync(CancellationToken ct)
    {
        var claimed = new HashSet<string>();
        while (!ct.IsCancellationRequested)
        {
            try
            {
                var now = DateTimeOffset.Now;
                await using var db = await dbFactory.CreateDbContextAsync(ct);
                var schedules = await db.Schedules.AsNoTracking().Where(s => s.Enabled).ToListAsync(ct);
                var next = schedules.Select(s => (Schedule: s, At: Scheduler.GetNextOccurrence([s], now)))
                    .Where(x => x.At.HasValue).OrderBy(x => x.At).FirstOrDefault();
                if (runtime.Snapshot.State == AutomationState.Paused) runtime.SetNextSchedule(next.At);
                else if (runtime.Snapshot.State != AutomationState.Processing) runtime.SetWaiting(next.At);
                foreach (var schedule in schedules)
                {
                    var dayBit = ToFlag(now.DayOfWeek);
                    var at = now.Date.Add(schedule.LocalTime.ToTimeSpan());
                    var key = $"{now:yyyy-MM-dd}:{schedule.AttendanceType}";
                    if ((schedule.ActiveDays & dayBit) == 0 || now < at || now >= at.AddMinutes(1) || !claimed.Add(key)) continue;
                    if (runtime.Snapshot.State == AutomationState.Paused) { _missedDate = DateOnly.FromDateTime(now.Date); _missedType = schedule.AttendanceType; continue; }
                    if (_queueLock.CurrentCount == 0) { _missedDate = DateOnly.FromDateTime(now.Date); _missedType = schedule.AttendanceType; continue; }
                    await RunQueueAsync(schedule.AttendanceType, at, true, ct);
                }
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
            }
            catch (OperationCanceledException) when (ct.IsCancellationRequested) { break; }
            catch (Exception ex) { runtime.SetError(ex.GetBaseException().Message); await Task.Delay(TimeSpan.FromSeconds(5), ct); }
        }
    }

    private async Task RunQueueAsync(AttendanceType type, DateTime scheduledAt, bool scheduled, CancellationToken ct)
    {
        if (!await _queueLock.WaitAsync(0, ct)) throw new InvalidOperationException("Queue sedang berjalan");
        _activeQueueCancellation = CancellationTokenSource.CreateLinkedTokenSource(ct);
        ct = _activeQueueCancellation.Token;
        var wasAutomationActive = runtime.Snapshot.State is AutomationState.Running or AutomationState.Processing or AutomationState.Paused;
        var run = new AttendanceRun { AttendanceType = type, ScheduledAt = scheduledAt, StartedAt = DateTime.UtcNow, Status = RunStatus.Running };
        var collected = new List<(AttendanceProfile Profile, AttendanceResult Result)>();
        try
        {
            await using (var db = await dbFactory.CreateDbContextAsync(ct)) { db.AttendanceRuns.Add(run); await db.SaveChangesAsync(ct); }
            if (!scheduled) await AuditAsync("MANUAL_RUN_STARTED", $"Manual {type} run started.", ct);
            var profiles = await LoadProfilesAsync(ct);
            var providers = _providers.Where(p => profiles.Any(profile => profile.Enabled && IsProviderEnabled(profile, p.Kind))).ToArray();
            var total = profiles.Sum(p => _providers.Count(provider => IsProviderEnabled(p, provider.Kind)));
            var done = 0;
            foreach (var profile in profiles)
            {
                if (!profile.Enabled) continue;
                foreach (var provider in providers.Where(p => IsProviderEnabled(profile, p.Kind)))
                {
                    ct.ThrowIfCancellationRequested();
                    runtime.SetProcessing(profile.Name, ProviderLabel(provider.Kind), type, Math.Max(0, total - done - 1));
                    var result = await ProcessProviderAsync(run.Id, profile, provider, type, ct);
                    collected.Add((profile, result)); done++;
                    var settings = await ReadTelegramSettingsAsync(ct);
                    try { await telegram.SendResultAsync(result, profile, settings, ct); }
                    catch (OperationCanceledException) when (ct.IsCancellationRequested) { throw; }
                    catch (Exception ex) { await AuditAsync("TELEGRAM_SEND_FAILED", ex.GetBaseException().Message, ct); }
                }
            }
            run.Status = collected.Any(x => x.Result.Status is RunStatus.Failed or RunStatus.Blocked) ? RunStatus.Failed : RunStatus.Success;
        }
        catch (OperationCanceledException)
        {
            run.Status = RunStatus.Failed;
            await AuditAsync("ATTENDANCE_RUN_CANCELLED", $"Run #{run.Id} cancelled; current provider stopped safely.", CancellationToken.None);
        }
        catch (Exception ex)
        {
            run.Status = RunStatus.Failed;
            await AuditAsync("ATTENDANCE_RUN_FAILED", $"Run #{run.Id}: {ex.GetBaseException().Message}", CancellationToken.None);
        }
        finally
        {
            run.FinishedAt = DateTime.UtcNow;
            await using (var db = await dbFactory.CreateDbContextAsync(CancellationToken.None))
            {
                var stored = await db.AttendanceRuns.FindAsync(run.Id);
                if (stored is not null) { stored.Status = run.Status; stored.FinishedAt = run.FinishedAt; await db.SaveChangesAsync(); }
            }
            try
            {
                var settings = await ReadTelegramSettingsAsync(CancellationToken.None);
                await telegram.SendSummaryAsync(collected.GroupBy(x => x.Profile.Id).Select(g => (g.First().Profile, (IEnumerable<AttendanceResult>)g.Select(x => x.Result))), settings, type, CancellationToken.None);
            }
            catch { }
            if (!scheduled) await AuditAsync("MANUAL_RUN_COMPLETED", $"Manual run #{run.Id} completed: {run.Status}.", CancellationToken.None);
            if (wasAutomationActive && runtime.Snapshot.State != AutomationState.Stopped) runtime.SetWaiting((await GetNextScheduleAsync()).At);
            else if (runtime.Snapshot.State == AutomationState.Processing) runtime.Stop();
            _queueLock.Release();
            _activeQueueCancellation?.Dispose(); _activeQueueCancellation = null;
            if (runtime.Snapshot.State == AutomationState.Running && _missedType is { } pending && _missedDate == DateOnly.FromDateTime(DateTime.Now))
            {
                _missedType = null; _missedDate = null;
                _ = Task.Run(() => RunQueueAsync(pending, DateTime.Now, true, _schedulerCancellation?.Token ?? CancellationToken.None));
            }
        }
    }

    private async Task<AttendanceResult> ProcessProviderAsync(long runId, AttendanceProfile profile, IAttendanceProvider provider, AttendanceType type, CancellationToken ct)
    {
        await using var db = await dbFactory.CreateDbContextAsync(ct);
        var today = DateTime.UtcNow.Date;
        var duplicate = await db.AttendanceResults.AnyAsync(r => r.ProfileId == profile.Id && r.Provider == provider.Kind && r.AttendanceType == type && r.Status == RunStatus.Success && r.StartedAt >= today && r.StartedAt < today.AddDays(1), ct);
        var result = new AttendanceResult { AttendanceRunId = runId, ProfileId = profile.Id, Provider = provider.Kind, AttendanceType = type, StartedAt = DateTime.UtcNow, Attempt = 1 };
        if (duplicate) { result.Status = RunStatus.Skipped; result.ResultMessage = "Already succeeded today."; }
        else
        {
            var context = new ProviderContext(profile, type, true, TimeSpan.FromSeconds(60));
            var maxRetries = int.TryParse(await ReadSettingAsync("Schedule.MaxRetry", "0", ct), out var retries) ? Math.Clamp(retries, 0, 5) : 0;
            for (var attempt = 1; attempt <= maxRetries + 1; attempt++)
            {
                result.Attempt = attempt;
                try
                {
                    var prerequisite = await provider.CheckPrerequisitesAsync(context, ct);
                    var action = prerequisite.Status is not (RunStatus.Pending or RunStatus.Success) ? prerequisite : await provider.LoginAsync(context, ct);
                    if (action.Status is RunStatus.Pending or RunStatus.Success)
                        action = type == AttendanceType.ClockIn ? await provider.ClockInAsync(context, ct) : await provider.ClockOutAsync(context, ct);
                    if (action.Status is RunStatus.Pending or RunStatus.Success) action = await provider.VerifyResultAsync(context, ct);
                    result.Status = action.Status == RunStatus.Pending ? RunStatus.Success : action.Status;
                    result.ResultMessage = action.Message; result.ErrorCode = action.ErrorCode; result.ErrorMessage = action.ErrorMessage;
                    await provider.LogoutAsync(context, ct);
                }
                catch (OperationCanceledException) { throw; }
                catch (Exception ex) { result.Status = RunStatus.Failed; result.ErrorMessage = ex.GetBaseException().Message; }
                if (result.Status is not (RunStatus.Failed or RunStatus.Blocked) || attempt > maxRetries) break;
                await Task.Delay(TimeSpan.FromSeconds(5), ct);
            }
        }
        result.FinishedAt = DateTime.UtcNow;
        db.AttendanceResults.Add(result); await db.SaveChangesAsync(ct);
        return result;
    }

    private async Task<List<AttendanceProfile>> LoadProfilesAsync(CancellationToken ct)
    { await using var db = await dbFactory.CreateDbContextAsync(ct); return await db.AttendanceProfiles.AsNoTracking().Where(p => p.Enabled).OrderBy(p => p.QueueOrder).ThenBy(p => p.Id).ToListAsync(ct); }
    private async Task<TelegramSetting> ReadTelegramSettingsAsync(CancellationToken ct)
    { await using var db = await dbFactory.CreateDbContextAsync(ct); return await db.TelegramSettings.AsNoTracking().OrderBy(x => x.Id).FirstOrDefaultAsync(ct) ?? new TelegramSetting(); }
    private async Task<string> ReadSettingAsync(string key, string fallback, CancellationToken ct)
    { await using var db = await dbFactory.CreateDbContextAsync(ct); return await db.ApplicationSettings.Where(x => x.Key == key).Select(x => x.Value).FirstOrDefaultAsync(ct) ?? fallback; }
    private async Task AuditAsync(string action, string message, CancellationToken ct)
    { await using var db = await dbFactory.CreateDbContextAsync(ct); db.AuditLogs.Add(new AuditLog { Timestamp = DateTime.UtcNow, Category = "Automation", Action = action, Message = message }); await db.SaveChangesAsync(ct); }
    private static bool IsProviderEnabled(AttendanceProfile profile, ProviderKind kind) => kind == ProviderKind.Gps ? profile.Website1Enabled : profile.Website2Enabled;
    private static string ProviderLabel(ProviderKind kind) => kind == ProviderKind.Gps ? "Website GPS" : "Website Network";
    private static DayOfWeekFlags ToFlag(DayOfWeek day) => day switch { DayOfWeek.Monday => DayOfWeekFlags.Monday, DayOfWeek.Tuesday => DayOfWeekFlags.Tuesday, DayOfWeek.Wednesday => DayOfWeekFlags.Wednesday, DayOfWeek.Thursday => DayOfWeekFlags.Thursday, DayOfWeek.Friday => DayOfWeekFlags.Friday, DayOfWeek.Saturday => DayOfWeekFlags.Saturday, _ => DayOfWeekFlags.Sunday };
}
