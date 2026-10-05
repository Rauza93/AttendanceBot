using AttendanceBot.Core.Interfaces;
using AttendanceBot.Core.Models;

namespace AttendanceBot.Core.Services;

public sealed class SequentialQueue(IEnumerable<IAttendanceProvider> providers)
{
    private readonly IAttendanceProvider[] _providers = providers.ToArray();
    private CancellationTokenSource? _stop;
    public void Stop() => _stop?.Cancel();
    public async Task<IReadOnlyList<AttendanceResult>> ExecuteAsync(IEnumerable<AttendanceProfile> profiles, AttendanceType type, Func<int, ProviderKind, AttendanceType, Task<bool>> alreadySuccessful, Func<AttendanceResult, Task> persist, int maxRetries, TimeSpan retryDelay, TimeSpan profileDelay, bool visibleBrowser, TimeSpan timeout, CancellationToken ct)
    {
        _stop = CancellationTokenSource.CreateLinkedTokenSource(ct); var results = new List<AttendanceResult>();
        foreach (var profile in profiles.Where(x => x.Enabled).OrderBy(x => x.QueueOrder))
        {
            foreach (var provider in _providers.Where(p => (p.Kind == ProviderKind.Gps && profile.Website1Enabled) || (p.Kind == ProviderKind.Network && profile.Website2Enabled)))
            {
                var result = new AttendanceResult { ProfileId = profile.Id, Provider = provider.Kind, AttendanceType = type, Status = RunStatus.Running, StartedAt = DateTime.UtcNow, Attempt = 0 };
                for (var attempt = 1; attempt <= Math.Max(1, maxRetries + 1); attempt++)
                {
                    result.Attempt = attempt;
                    if (await alreadySuccessful(profile.Id, provider.Kind, type)) { result.Status = RunStatus.Skipped; result.ResultMessage = "Duplicate protection: attendance already succeeded."; break; }
                    var context = new ProviderContext(profile, type, visibleBrowser, timeout); var prerequisite = await provider.CheckPrerequisitesAsync(context, _stop.Token);
                    if (prerequisite.Status == RunStatus.Blocked || prerequisite.Status == RunStatus.ManualRequired) { result.Status = prerequisite.Status; result.ResultMessage = prerequisite.Message; break; }
                    var login = await provider.LoginAsync(context, _stop.Token); var action = login.Status == RunStatus.ManualRequired ? login : login.Status == RunStatus.Failed ? login : type == AttendanceType.ClockIn ? await provider.ClockInAsync(context, _stop.Token) : await provider.ClockOutAsync(context, _stop.Token);
                    var verified = action.Status is RunStatus.Pending or RunStatus.Success ? await provider.VerifyResultAsync(context, _stop.Token) : action;
                    result.Status = verified.Status == RunStatus.Pending ? RunStatus.Success : verified.Status; result.ResultMessage = verified.Message; result.ErrorCode = verified.ErrorCode; result.ErrorMessage = verified.ErrorMessage; await provider.LogoutAsync(context, _stop.Token);
                    if (result.Status is RunStatus.Success or RunStatus.ManualRequired or RunStatus.Blocked) break; if (attempt <= maxRetries) await Task.Delay(retryDelay, _stop.Token);
                }
                result.FinishedAt = DateTime.UtcNow; results.Add(result); await persist(result);
            }
            if (profileDelay > TimeSpan.Zero) await Task.Delay(profileDelay, _stop.Token);
        }
        return results;
    }
}
