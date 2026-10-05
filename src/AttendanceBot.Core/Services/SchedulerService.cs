using AttendanceBot.Core.Models;

namespace AttendanceBot.Core.Services;

public sealed class SchedulerService
{
    public DateTimeOffset? GetNextOccurrence(IEnumerable<Schedule> schedules, DateTimeOffset now)
    {
        DateTimeOffset? best = null;
        foreach (var s in schedules.Where(x => x.Enabled)) for (var i = 0; i < 8; i++) { var day = now.Date.AddDays(i); var dow = day.DayOfWeek switch { DayOfWeek.Monday => DayOfWeekFlags.Monday, DayOfWeek.Tuesday => DayOfWeekFlags.Tuesday, DayOfWeek.Wednesday => DayOfWeekFlags.Wednesday, DayOfWeek.Thursday => DayOfWeekFlags.Thursday, DayOfWeek.Friday => DayOfWeekFlags.Friday, DayOfWeek.Saturday => DayOfWeekFlags.Saturday, _ => DayOfWeekFlags.Sunday }; if ((s.ActiveDays & dow) == 0) continue; var candidate = new DateTimeOffset(day.Add(s.LocalTime.ToTimeSpan()), now.Offset); if (candidate <= now) continue; if (best is null || candidate < best) best = candidate; }
        return best;
    }
}
