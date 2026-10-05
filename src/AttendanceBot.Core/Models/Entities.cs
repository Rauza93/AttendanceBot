namespace AttendanceBot.Core.Models;

public sealed class AttendanceProfile
{
    public int Id { get; set; }
    public string Name { get; set; } = "";
    public string? EmployeeCode { get; set; }
    public bool Enabled { get; set; } = true;
    public bool Website1Enabled { get; set; } = true;
    public bool Website2Enabled { get; set; } = true;
    public string Website1Username { get; set; } = "";
    public string? Website1PasswordSecret { get; set; }
    public string Website2Username { get; set; } = "";
    public string? Website2PasswordSecret { get; set; }
    public int QueueOrder { get; set; }
    public string? Notes { get; set; }
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime UpdatedAt { get; set; } = DateTime.UtcNow;
}

public sealed class Schedule
{
    public int Id { get; set; }
    public AttendanceType AttendanceType { get; set; }
    public bool Enabled { get; set; }
    public TimeOnly LocalTime { get; set; }
    public DayOfWeekFlags ActiveDays { get; set; } = DayOfWeekFlags.EveryDay;
}

public sealed class AttendanceRun
{
    public long Id { get; set; }
    public AttendanceType AttendanceType { get; set; }
    public DateTime ScheduledAt { get; set; }
    public DateTime? StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public RunStatus Status { get; set; } = RunStatus.Pending;
    public ICollection<AttendanceResult> Results { get; set; } = new List<AttendanceResult>();
}

public sealed class AttendanceResult
{
    public long Id { get; set; }
    public long AttendanceRunId { get; set; }
    public AttendanceRun? AttendanceRun { get; set; }
    public int ProfileId { get; set; }
    public ProviderKind Provider { get; set; }
    public AttendanceType AttendanceType { get; set; }
    public RunStatus Status { get; set; }
    public int Attempt { get; set; }
    public DateTime StartedAt { get; set; }
    public DateTime? FinishedAt { get; set; }
    public string? ResultMessage { get; set; }
    public string? ScreenshotPath { get; set; }
    public string? ErrorCode { get; set; }
    public string? ErrorMessage { get; set; }
}

public sealed class ApplicationSetting { public string Key { get; set; } = ""; public string Value { get; set; } = ""; }
public sealed class TelegramSetting { public int Id { get; set; } public bool Enabled { get; set; } public string? BotTokenSecret { get; set; } public string? ChatId { get; set; } public bool NotifySuccess { get; set; } = true; public bool NotifyFailure { get; set; } = true; public bool SendSummary { get; set; } = true; public bool SendScreenshot { get; set; } }
public sealed class AuditLog { public long Id { get; set; } public DateTime Timestamp { get; set; } = DateTime.UtcNow; public string Level { get; set; } = "Information"; public string Category { get; set; } = "Application"; public string Action { get; set; } = ""; public int? ProfileId { get; set; } public string Message { get; set; } = ""; }
