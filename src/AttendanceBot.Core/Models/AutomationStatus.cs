namespace AttendanceBot.Core.Models;

public enum AutomationState { Stopped, Running, Paused, Processing, Error }

public sealed record AutomationSnapshot(AutomationState State, string? Profile = null, string? Provider = null,
    AttendanceType? Action = null, int QueueRemaining = 0, string Mode = "Stopped", DateTimeOffset? NextSchedule = null,
    string? Error = null, DateTimeOffset? StartedAt = null);
