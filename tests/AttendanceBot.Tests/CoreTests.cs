using AttendanceBot.Core.Interfaces;
using AttendanceBot.Core.Models;
using AttendanceBot.Core.Services;
using AttendanceBot.Infrastructure.Persistence;
using AttendanceBot.Infrastructure.Security;
using AttendanceBot.Automation;
using AttendanceBot.Infrastructure.Services;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using System.Net;
using System.Text.Json;
using Xunit;

namespace AttendanceBot.Tests;

public sealed class CoreTests
{
    [Fact] public async Task WebsiteConfiguration_SaveReloadUpdateAndDisable()
    {
        var path = Path.Combine(Path.GetTempPath(), $"attendancebot-website-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<AttendanceDbContext>().UseSqlite($"Data Source={path}").Options;
        try
        {
            await using (var db = new AttendanceDbContext(options))
            {
                await db.Database.EnsureCreatedAsync();
                db.WebsiteConfigurations.Add(new WebsiteConfiguration { ProviderType = "GPS", Name = "GPS site", Enabled = true, BaseUrl = "https://example.org", LoginUrl = "https://example.org/login", AttendanceUrl = "https://example.org/attendance" });
                await db.SaveChangesAsync();
            }
            await using (var db = new AttendanceDbContext(options))
            {
                var item = await db.WebsiteConfigurations.SingleAsync(x => x.ProviderType == "GPS");
                Assert.Equal("https://example.org/login", item.LoginUrl);
                item.BaseUrl = "https://updated.example.org"; item.Enabled = false;
                await db.SaveChangesAsync();
            }
            await using var verify = new AttendanceDbContext(options);
            var updated = await verify.WebsiteConfigurations.SingleAsync();
            Assert.Equal("https://updated.example.org", updated.BaseUrl);
            Assert.False(updated.Enabled);
        }
        finally { SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    [InlineData("ftp://example.org")]
    [InlineData("invalid url")]
    public void WebsiteConfiguration_RejectsInvalidUrls(string url)
    {
        var item = new WebsiteConfiguration { Name = "Test", Enabled = true, BaseUrl = url, LoginUrl = url, AttendanceUrl = "https://example.org/attendance" };
        Assert.NotNull(WebsiteConfigurationValidator.Validate(item));
    }

    [Fact] public async Task GpsProvider_ReadsDatabaseUrlAndMissingConfigRequiresManual()
    {
        var path = Path.Combine(Path.GetTempPath(), $"attendancebot-provider-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<AttendanceDbContext>().UseSqlite($"Data Source={path}").Options;
        var factory = new TestDbFactory(options);
        try
        {
            await using (var db = new AttendanceDbContext(options))
            {
                await db.Database.EnsureCreatedAsync();
                var provider = new GpsAttendanceProvider(new ProviderOptions(), factory);
                var missing = await provider.CheckPrerequisitesAsync(new ProviderContext(new AttendanceProfile(), AttendanceType.ClockIn, true, TimeSpan.FromSeconds(3)), CancellationToken.None);
                Assert.Equal(RunStatus.ManualRequired, missing.Status);
                Assert.Equal("Website GPS belum dikonfigurasi", missing.Message);
                db.WebsiteConfigurations.Add(new WebsiteConfiguration { ProviderType = "GPS", Name = "GPS", Enabled = true, BaseUrl = "https://site.test", LoginUrl = "https://site.test/auth", AttendanceUrl = "https://site.test/in" });
                await db.SaveChangesAsync();
                var configured = await provider.CheckPrerequisitesAsync(new ProviderContext(new AttendanceProfile(), AttendanceType.ClockIn, true, TimeSpan.FromSeconds(3)), CancellationToken.None);
                Assert.Equal(RunStatus.Pending, configured.Status);
            }
        }
        finally { SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    private sealed class TestDbFactory(DbContextOptions<AttendanceDbContext> options) : IDbContextFactory<AttendanceDbContext>
    {
        public AttendanceDbContext CreateDbContext() => new(options);
        public Task<AttendanceDbContext> CreateDbContextAsync(CancellationToken cancellationToken = default) => Task.FromResult(CreateDbContext());
    }
    [Fact] public void Scheduler_UsesConfiguredTimeAndDays()
    {
        var now = new DateTimeOffset(2026, 10, 5, 6, 0, 0, TimeSpan.FromHours(7));
        var next = new SchedulerService().GetNextOccurrence([new Schedule { Enabled = true, LocalTime = new TimeOnly(7, 0), ActiveDays = DayOfWeekFlags.Monday }], now);
        Assert.Equal(7, next?.Hour);
    }

    [Fact] public void AutomationRuntime_StartIsIdempotentAndSupportsPauseResumeStop()
    {
        var runtime = new AutomationRuntimeService();
        Assert.Equal(AutomationState.Stopped, runtime.Snapshot.State);
        Assert.True(runtime.Start());
        Assert.Equal(AutomationState.Running, runtime.Snapshot.State);
        Assert.False(runtime.Start());
        Assert.True(runtime.Pause());
        Assert.Equal(AutomationState.Paused, runtime.Snapshot.State);
        Assert.True(runtime.Resume());
        Assert.Equal(AutomationState.Running, runtime.Snapshot.State);
        var cancellation = runtime.RunToken;
        Assert.True(runtime.Stop());
        Assert.True(cancellation.IsCancellationRequested);
        Assert.Equal(AutomationState.Stopped, runtime.Snapshot.State);
        Assert.False(runtime.Stop());
    }

    [Fact] public void AutomationRuntime_ProcessingStateCarriesCurrentWork()
    {
        var runtime = new AutomationRuntimeService();
        runtime.Start();
        runtime.SetProcessing("Ahmad", "Website GPS", AttendanceType.ClockIn, 3);
        Assert.Equal(AutomationState.Processing, runtime.Snapshot.State);
        Assert.Equal("Ahmad", runtime.Snapshot.Profile);
        Assert.Equal("Website GPS", runtime.Snapshot.Provider);
        Assert.Equal(3, runtime.Snapshot.QueueRemaining);
    }

    [Fact] public async Task AutomationService_ManualQueueRunsSequentiallyAndPersistsLastRun()
    {
        var path = Path.Combine(Path.GetTempPath(), $"attendancebot-runtime-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<AttendanceDbContext>().UseSqlite($"Data Source={path}").Options;
        var factory = new TestDbFactory(options);
        var order = new List<string>();
        try
        {
            await using (var db = new AttendanceDbContext(options))
            {
                await db.Database.EnsureCreatedAsync();
                db.AttendanceProfiles.AddRange(new AttendanceProfile { Name = "Later", QueueOrder = 5, Website1Enabled = true, Website2Enabled = true }, new AttendanceProfile { Name = "First", QueueOrder = 1, Website1Enabled = true, Website2Enabled = true });
                await db.SaveChangesAsync();
            }
            var runtime = new AutomationRuntimeService();
            var service = new AttendanceAutomationService(factory, [new RuntimeFakeProvider(ProviderKind.Gps, order), new RuntimeFakeProvider(ProviderKind.Network, order)], runtime, new RuntimeFakeTelegram());
            await service.RunManualAsync(AttendanceType.ClockIn);
            Assert.Equal(["First-Gps", "First-Network", "Later-Gps", "Later-Network"], order);
            Assert.Equal(AutomationState.Stopped, runtime.Snapshot.State);
            await using var verify = new AttendanceDbContext(options);
            var last = await verify.AttendanceRuns.OrderByDescending(x => x.Id).FirstAsync();
            Assert.Equal(RunStatus.Success, last.Status);
            Assert.Equal(4, await verify.AttendanceResults.CountAsync());
        }
        finally { SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    [Fact] public async Task AutomationService_StartSchedulesClockInRunAndDuplicateStartIsIdempotent()
    {
        var path = Path.Combine(Path.GetTempPath(), $"attendancebot-scheduled-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<AttendanceDbContext>().UseSqlite($"Data Source={path}").Options;
        try
        {
            await using (var db = new AttendanceDbContext(options))
            {
                await db.Database.EnsureCreatedAsync();
                var at = DateTime.Now.AddSeconds(3);
                db.Schedules.Add(new Schedule { AttendanceType = AttendanceType.ClockIn, Enabled = true, LocalTime = TimeOnly.FromDateTime(at), ActiveDays = at.DayOfWeek switch { DayOfWeek.Monday => DayOfWeekFlags.Monday, DayOfWeek.Tuesday => DayOfWeekFlags.Tuesday, DayOfWeek.Wednesday => DayOfWeekFlags.Wednesday, DayOfWeek.Thursday => DayOfWeekFlags.Thursday, DayOfWeek.Friday => DayOfWeekFlags.Friday, DayOfWeek.Saturday => DayOfWeekFlags.Saturday, _ => DayOfWeekFlags.Sunday } });
                db.AttendanceProfiles.Add(new AttendanceProfile { Name = "Scheduled", Website1Enabled = true, Website2Enabled = false });
                await db.SaveChangesAsync();
            }
            var factory = new TestDbFactory(options);
            var provider = new RuntimeFakeProvider(ProviderKind.Gps, []);
            var service = new AttendanceAutomationService(factory, [provider], new AutomationRuntimeService(), new RuntimeFakeTelegram());
            await service.StartAsync();
            await service.StartAsync();
            var deadline = DateTime.UtcNow.AddSeconds(12);
            long runId = 0;
            while (DateTime.UtcNow < deadline)
            {
                await using var poll = new AttendanceDbContext(options);
                runId = await poll.AttendanceRuns.Select(x => x.Id).FirstOrDefaultAsync();
                if (runId != 0) break;
                await Task.Delay(200);
            }
            Assert.NotEqual(0, runId);
            await service.StopAsync();
            await using var verify = new AttendanceDbContext(options);
            Assert.Equal(AttendanceType.ClockIn, (await verify.AttendanceRuns.SingleAsync()).AttendanceType);
            Assert.Single(await verify.AttendanceResults.ToListAsync());
        }
        finally { SqliteConnection.ClearAllPools(); if (File.Exists(path)) File.Delete(path); }
    }

    private sealed class RuntimeFakeProvider(ProviderKind kind, List<string> order) : IAttendanceProvider
    {
        public ProviderKind Kind => kind;
        public Task<ProviderResult> CheckPrerequisitesAsync(ProviderContext c, CancellationToken ct) => Task.FromResult(new ProviderResult(RunStatus.Pending, "ok"));
        public Task<ProviderResult> LoginAsync(ProviderContext c, CancellationToken ct) => Task.FromResult(new ProviderResult(RunStatus.Pending, "ok"));
        public Task<ProviderResult> CheckAttendanceStatusAsync(ProviderContext c, CancellationToken ct) => Task.FromResult(new ProviderResult(RunStatus.Pending, "ok"));
        public Task<ProviderResult> ClockInAsync(ProviderContext c, CancellationToken ct) { order.Add($"{c.Profile.Name}-{kind}"); return Task.FromResult(new ProviderResult(RunStatus.Success, "ok")); }
        public Task<ProviderResult> ClockOutAsync(ProviderContext c, CancellationToken ct) { order.Add($"{c.Profile.Name}-{kind}"); return Task.FromResult(new ProviderResult(RunStatus.Success, "ok")); }
        public Task<ProviderResult> VerifyResultAsync(ProviderContext c, CancellationToken ct) => Task.FromResult(new ProviderResult(RunStatus.Success, "ok"));
        public Task LogoutAsync(ProviderContext c, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class RuntimeFakeTelegram : ITelegramService
    {
        public Task SendResultAsync(AttendanceResult result, AttendanceProfile profile, TelegramSetting settings, CancellationToken ct) => Task.CompletedTask;
        public Task SendSummaryAsync(IEnumerable<(AttendanceProfile Profile, IEnumerable<AttendanceResult> Results)> summary, TelegramSetting settings, AttendanceType type, CancellationToken ct) => Task.CompletedTask;
        public Task<bool> TestAsync(TelegramSetting settings, CancellationToken ct) => Task.FromResult(true);
    }

    [Theory]
    [InlineData("", "0", "Nama wajib diisi.")]
    [InlineData("Profile", "-1", "Queue Order harus berupa angka nol atau lebih.")]
    [InlineData("Profile", "invalid", "Queue Order harus berupa angka nol atau lebih.")]
    [InlineData("Profile", "0", null)]
    public void ProfileValidation_ReportsExpectedResult(string name, string queueOrderText, string? expectedError)
    {
        var error = ProfileInputValidator.Validate(name, queueOrderText, true, "", "", false, "", "", out var order);
        Assert.Equal(expectedError, error);
        if (expectedError is null) Assert.Equal(0, order);
    }

    [Fact] public void ProfileValidation_RejectsPartialEnabledWebsiteCredentials()
    {
        var error = ProfileInputValidator.Validate("Profile", "2", true, "user", "", false, "", "", out var order);
        Assert.Equal("Username dan password website harus diisi berpasangan.", error);
        Assert.Equal(2, order);
    }

    [Fact] public async Task Sqlite_CrudAndSettingsSurviveNewContexts()
    {
        var path = Path.Combine(Path.GetTempPath(), $"attendancebot-test-{Guid.NewGuid():N}.db");
        var options = new DbContextOptionsBuilder<AttendanceDbContext>().UseSqlite($"Data Source={path}").Options;
        try
        {
            int id;
            await using (var db = new AttendanceDbContext(options))
            {
                await db.Database.EnsureCreatedAsync();
                var profile = new AttendanceProfile { Name = "Test User 01", Website1Enabled = false, Website2Enabled = false };
                db.AttendanceProfiles.Add(profile);
                db.Schedules.Add(new Schedule { AttendanceType = AttendanceType.ClockIn, Enabled = true, LocalTime = new TimeOnly(8, 15), ActiveDays = DayOfWeekFlags.Monday | DayOfWeekFlags.Friday });
                db.ApplicationSettings.Add(new ApplicationSetting { Key = "test.setting", Value = "before" });
                await db.SaveChangesAsync();
                id = profile.Id;
            }

            await using (var db = new AttendanceDbContext(options))
            {
                var profile = await db.AttendanceProfiles.SingleAsync(p => p.Id == id);
                Assert.Equal("Test User 01", profile.Name);
                profile.Name = "Updated User";
                var schedule = await db.Schedules.SingleAsync();
                schedule.LocalTime = new TimeOnly(9, 30);
                (await db.ApplicationSettings.SingleAsync(s => s.Key == "test.setting")).Value = "after";
                await db.SaveChangesAsync();
            }

            await using (var db = new AttendanceDbContext(options))
            {
                Assert.Equal("Updated User", (await db.AttendanceProfiles.SingleAsync()).Name);
                Assert.Equal(new TimeOnly(9, 30), (await db.Schedules.SingleAsync()).LocalTime);
                Assert.Equal("after", (await db.ApplicationSettings.SingleAsync(s => s.Key == "test.setting")).Value);
                db.AttendanceProfiles.Remove(await db.AttendanceProfiles.SingleAsync());
                await db.SaveChangesAsync();
            }
            await using var verify = new AttendanceDbContext(options);
            Assert.Empty(await verify.AttendanceProfiles.ToListAsync());
        }
        finally
        {
            SqliteConnection.ClearAllPools();
            if (File.Exists(path)) File.Delete(path);
        }
    }

    [Fact] public void PinHasher_VerifiesOnlyMatchingPin()
    {
        var hasher = new PinHasher();
        var hash = hasher.Hash("1234");
        Assert.NotEqual("1234", hash);
        Assert.True(hasher.Verify("1234", hash));
        Assert.False(hasher.Verify("0000", hash));
        Assert.False(hasher.Verify("1234", "invalid"));
    }

    [Fact] public void SecretInterface_CanBeExercisedWithNonDpapiTestStore()
    {
        ISecretStore secrets = new TestSecretStore();
        var protectedValue = secrets.Protect("sensitive", "test-purpose");
        Assert.NotEqual("sensitive", protectedValue);
        Assert.Equal("sensitive", secrets.Unprotect(protectedValue, "test-purpose"));
        Assert.Null(secrets.Unprotect(protectedValue, "another-purpose"));
    }

    [Fact] public void LogRedactor_RemovesCredentialsAndTokenValues()
    {
        var text = LogRedactor.Redact("password=abc123 bot_token:XYZ chat_id=998877");
        Assert.DoesNotContain("abc123", text);
        Assert.DoesNotContain("XYZ", text);
        Assert.DoesNotContain("998877", text);
    }

    [Fact] public async Task TelegramService_BuildsExpectedAttendanceMessage()
    {
        var handler = new CaptureHandler();
        using var http = new HttpClient(handler);
        var service = new AttendanceBot.Infrastructure.Services.TelegramService(http, new TestSecretStore());
        var settings = new TelegramSetting
        {
            Enabled = true,
            BotTokenSecret = new TestSecretStore().Protect("123:token", "telegram-bot-token"),
            ChatId = "chat-01",
            NotifySuccess = true
        };
        var result = new AttendanceResult
        {
            Provider = ProviderKind.Gps,
            AttendanceType = AttendanceType.ClockIn,
            Status = RunStatus.Success,
            StartedAt = new DateTime(2026, 10, 6, 8, 0, 0, DateTimeKind.Utc),
            FinishedAt = new DateTime(2026, 10, 6, 8, 1, 0, DateTimeKind.Utc),
            ResultMessage = "Clock-in verified"
        };
        await service.SendResultAsync(result, new AttendanceProfile { Name = "Test Person" }, settings, CancellationToken.None);
        using var payload = JsonDocument.Parse(handler.Body!);
        Assert.Equal("chat-01", payload.RootElement.GetProperty("chat_id").GetString());
        var message = payload.RootElement.GetProperty("text").GetString();
        Assert.Contains("Test Person", message);
        Assert.Contains("Gps", message);
        Assert.Contains("Clock-in verified", message);
    }

    [Fact] public async Task Queue_IsSequentialAndProtectsDuplicates()
    {
        var order = new List<int>(); var provider = new FakeProvider(order); var queue = new SequentialQueue([provider]);
        var profiles = new[] { new AttendanceProfile { Id = 1, Name = "A", QueueOrder = 2 }, new AttendanceProfile { Id = 2, Name = "B", QueueOrder = 1 } };
        await queue.ExecuteAsync(profiles, AttendanceType.ClockIn, (_, _, _) => Task.FromResult(false), r => Task.CompletedTask, 0, TimeSpan.Zero, TimeSpan.Zero, false, TimeSpan.FromSeconds(1), CancellationToken.None);
        Assert.Equal([2, 1], order);
    }

    [Fact] public async Task Queue_RetriesFailuresAndSkipsExistingSuccess()
    {
        var provider = new FailingProvider();
        var queue = new SequentialQueue([provider]);
        var profile = new AttendanceProfile { Id = 5, Name = "Retry", Website1Enabled = true, Website2Enabled = false };
        var results = await queue.ExecuteAsync([profile], AttendanceType.ClockIn, (_, _, _) => Task.FromResult(false), _ => Task.CompletedTask,
            2, TimeSpan.Zero, TimeSpan.Zero, false, TimeSpan.FromSeconds(1), CancellationToken.None);
        Assert.Equal(3, provider.LoginCount);
        Assert.Equal(3, results.Single().Attempt);
        Assert.Equal(RunStatus.Failed, results.Single().Status);

        var duplicate = await queue.ExecuteAsync([profile], AttendanceType.ClockIn, (_, _, _) => Task.FromResult(true), _ => Task.CompletedTask,
            2, TimeSpan.Zero, TimeSpan.Zero, false, TimeSpan.FromSeconds(1), CancellationToken.None);
        Assert.Equal(RunStatus.Skipped, duplicate.Single().Status);
        Assert.Equal(3, provider.LoginCount);
    }

    [Fact] public async Task Queue_StopsOnManualRequiredProviderState()
    {
        var provider = new ManualProvider();
        var queue = new SequentialQueue([provider]);
        var profile = new AttendanceProfile { Id = 7, Name = "Manual", Website1Enabled = true, Website2Enabled = false };
        var results = await queue.ExecuteAsync([profile], AttendanceType.ClockIn, (_, _, _) => Task.FromResult(false), _ => Task.CompletedTask,
            4, TimeSpan.Zero, TimeSpan.Zero, false, TimeSpan.FromSeconds(1), CancellationToken.None);
        Assert.Equal(RunStatus.ManualRequired, results.Single().Status);
        Assert.Equal(0, provider.LoginCount);
    }
    private sealed class FakeProvider(List<int> order) : IAttendanceProvider
    {
        public ProviderKind Kind => ProviderKind.Gps;
        public Task<ProviderResult> CheckPrerequisitesAsync(ProviderContext c, CancellationToken _) { order.Add(c.Profile.Id); return Task.FromResult(new ProviderResult(RunStatus.Pending, "ok")); }
        public Task<ProviderResult> LoginAsync(ProviderContext c, CancellationToken _) => Task.FromResult(new ProviderResult(RunStatus.Pending, "ok"));
        public Task<ProviderResult> CheckAttendanceStatusAsync(ProviderContext c, CancellationToken _) => Task.FromResult(new ProviderResult(RunStatus.Pending, "ok"));
        public Task<ProviderResult> ClockInAsync(ProviderContext c, CancellationToken _) => Task.FromResult(new ProviderResult(RunStatus.Pending, "ok"));
        public Task<ProviderResult> ClockOutAsync(ProviderContext c, CancellationToken _) => Task.FromResult(new ProviderResult(RunStatus.Pending, "ok"));
        public Task<ProviderResult> VerifyResultAsync(ProviderContext c, CancellationToken _) => Task.FromResult(new ProviderResult(RunStatus.Success, "ok"));
        public Task LogoutAsync(ProviderContext c, CancellationToken _) => Task.CompletedTask;
    }

    private sealed class TestSecretStore : ISecretStore
    {
        public string Protect(string value, string purpose) => Convert.ToBase64String(System.Text.Encoding.UTF8.GetBytes(purpose + ":" + value));
        public string? Unprotect(string? value, string purpose)
        {
            if (value is null) return null;
            var decoded = System.Text.Encoding.UTF8.GetString(Convert.FromBase64String(value));
            return decoded.StartsWith(purpose + ":", StringComparison.Ordinal) ? decoded[(purpose.Length + 1)..] : null;
        }
        public void Delete(string purpose) { }
    }

    private sealed class FailingProvider : IAttendanceProvider
    {
        public int LoginCount { get; private set; }
        public ProviderKind Kind => ProviderKind.Gps;
        public Task<ProviderResult> CheckPrerequisitesAsync(ProviderContext c, CancellationToken ct) => Task.FromResult(new ProviderResult(RunStatus.Pending, "ok"));
        public Task<ProviderResult> LoginAsync(ProviderContext c, CancellationToken ct) { LoginCount++; return Task.FromResult(new ProviderResult(RunStatus.Failed, "failure")); }
        public Task<ProviderResult> CheckAttendanceStatusAsync(ProviderContext c, CancellationToken ct) => Task.FromResult(new ProviderResult(RunStatus.Pending, "ok"));
        public Task<ProviderResult> ClockInAsync(ProviderContext c, CancellationToken ct) => Task.FromResult(new ProviderResult(RunStatus.Pending, "ok"));
        public Task<ProviderResult> ClockOutAsync(ProviderContext c, CancellationToken ct) => Task.FromResult(new ProviderResult(RunStatus.Pending, "ok"));
        public Task<ProviderResult> VerifyResultAsync(ProviderContext c, CancellationToken ct) => Task.FromResult(new ProviderResult(RunStatus.Success, "ok"));
        public Task LogoutAsync(ProviderContext c, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class ManualProvider : IAttendanceProvider
    {
        public int LoginCount { get; private set; }
        public ProviderKind Kind => ProviderKind.Gps;
        public Task<ProviderResult> CheckPrerequisitesAsync(ProviderContext c, CancellationToken ct) => Task.FromResult(new ProviderResult(RunStatus.ManualRequired, "Configure provider first."));
        public Task<ProviderResult> LoginAsync(ProviderContext c, CancellationToken ct) { LoginCount++; return Task.FromResult(new ProviderResult(RunStatus.Pending, "unexpected")); }
        public Task<ProviderResult> CheckAttendanceStatusAsync(ProviderContext c, CancellationToken ct) => Task.FromResult(new ProviderResult(RunStatus.Pending, "ok"));
        public Task<ProviderResult> ClockInAsync(ProviderContext c, CancellationToken ct) => Task.FromResult(new ProviderResult(RunStatus.Pending, "ok"));
        public Task<ProviderResult> ClockOutAsync(ProviderContext c, CancellationToken ct) => Task.FromResult(new ProviderResult(RunStatus.Pending, "ok"));
        public Task<ProviderResult> VerifyResultAsync(ProviderContext c, CancellationToken ct) => Task.FromResult(new ProviderResult(RunStatus.Success, "ok"));
        public Task LogoutAsync(ProviderContext c, CancellationToken ct) => Task.CompletedTask;
    }

    private sealed class CaptureHandler : HttpMessageHandler
    {
        public string? Body { get; private set; }
        protected override async Task<HttpResponseMessage> SendAsync(HttpRequestMessage request, CancellationToken cancellationToken)
        {
            Body = await request.Content!.ReadAsStringAsync(cancellationToken);
            return new HttpResponseMessage(HttpStatusCode.OK);
        }
    }
}
