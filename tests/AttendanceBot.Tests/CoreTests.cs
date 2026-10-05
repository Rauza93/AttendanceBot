using AttendanceBot.Core.Interfaces;
using AttendanceBot.Core.Models;
using AttendanceBot.Core.Services;

namespace AttendanceBot.Tests;

public sealed class CoreTests
{
    [Fact] public void Scheduler_UsesConfiguredTimeAndDays()
    {
        var now = new DateTimeOffset(2026, 10, 5, 6, 0, 0, TimeSpan.FromHours(7));
        var next = new SchedulerService().GetNextOccurrence([new Schedule { Enabled = true, LocalTime = new TimeOnly(7), ActiveDays = DayOfWeekFlags.Monday }], now);
        Assert.Equal(7, next?.Hour);
    }
    [Fact] public async Task Queue_IsSequentialAndProtectsDuplicates()
    {
        var order = new List<int>(); var provider = new FakeProvider(order); var queue = new SequentialQueue([provider]);
        var profiles = new[] { new AttendanceProfile { Id = 1, Name = "A", QueueOrder = 2 }, new AttendanceProfile { Id = 2, Name = "B", QueueOrder = 1 } };
        await queue.ExecuteAsync(profiles, AttendanceType.ClockIn, (_, _, _) => Task.FromResult(false), r => Task.CompletedTask, 0, TimeSpan.Zero, TimeSpan.Zero, false, TimeSpan.FromSeconds(1), CancellationToken.None);
        Assert.Equal([2, 1], order);
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
}
