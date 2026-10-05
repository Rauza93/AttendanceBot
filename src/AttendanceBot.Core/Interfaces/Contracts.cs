using AttendanceBot.Core.Models;

namespace AttendanceBot.Core.Interfaces;

public sealed record ProviderContext(AttendanceProfile Profile, AttendanceType Type, bool VisibleBrowser, TimeSpan Timeout);
public sealed record ProviderResult(RunStatus Status, string Message, string? ErrorCode = null, string? ErrorMessage = null, bool ManualRequired = false);

public interface IAttendanceProvider
{
    ProviderKind Kind { get; }
    Task<ProviderResult> CheckPrerequisitesAsync(ProviderContext context, CancellationToken ct);
    Task<ProviderResult> LoginAsync(ProviderContext context, CancellationToken ct);
    Task<ProviderResult> CheckAttendanceStatusAsync(ProviderContext context, CancellationToken ct);
    Task<ProviderResult> ClockInAsync(ProviderContext context, CancellationToken ct);
    Task<ProviderResult> ClockOutAsync(ProviderContext context, CancellationToken ct);
    Task<ProviderResult> VerifyResultAsync(ProviderContext context, CancellationToken ct);
    Task LogoutAsync(ProviderContext context, CancellationToken ct);
}
public interface INetworkChecker { Task<NetworkCheckResult> CheckAsync(NetworkRules rules, CancellationToken ct); }
public sealed record NetworkRules(string? RequiredSsid, string? InterfaceName, string? Gateway, string? AllowedIpRange);
public sealed record NetworkCheckResult(bool IsAllowed, string Message, string? Ssid = null, string? InterfaceName = null, string? IpAddress = null);
public interface ISecretStore { string Protect(string value, string purpose); string? Unprotect(string? value, string purpose); void Delete(string purpose); }
public interface ITelegramService { Task SendResultAsync(AttendanceResult result, AttendanceProfile profile, TelegramSetting settings, CancellationToken ct); Task SendSummaryAsync(IEnumerable<(AttendanceProfile Profile, IEnumerable<AttendanceResult> Results)> summary, TelegramSetting settings, AttendanceType type, CancellationToken ct); Task<bool> TestAsync(TelegramSetting settings, CancellationToken ct); }
public interface IAttendanceQueue { Task<AttendanceRun> ExecuteAsync(AttendanceType type, DateTime scheduledAt, CancellationToken ct); void Stop(); }
