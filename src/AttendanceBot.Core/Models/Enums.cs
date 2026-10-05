namespace AttendanceBot.Core.Models;

public enum AttendanceType { ClockIn, ClockOut }
public enum RunStatus { Pending, Running, Success, Failed, Blocked, Skipped, ManualRequired }
public enum ProviderKind { Gps, Network }
public enum DayOfWeekFlags { None=0, Monday=1, Tuesday=2, Wednesday=4, Thursday=8, Friday=16, Saturday=32, Sunday=64, EveryDay=127 }
