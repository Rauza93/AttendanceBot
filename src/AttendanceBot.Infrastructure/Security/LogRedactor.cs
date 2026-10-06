using System.Text.RegularExpressions;

namespace AttendanceBot.Infrastructure.Security;

public static partial class LogRedactor
{
    [GeneratedRegex(@"(?<key>password|bot[_-]?token|token|chat[_-]?id)\s*[:=]\s*(?<value>[^\s,;&]+)", RegexOptions.IgnoreCase | RegexOptions.CultureInvariant)]
    private static partial Regex SecretPattern();

    public static string Redact(string? message) =>
        string.IsNullOrEmpty(message)
            ? message ?? string.Empty
            : SecretPattern().Replace(message, match => $"{match.Groups["key"].Value}=[REDACTED]");
}
