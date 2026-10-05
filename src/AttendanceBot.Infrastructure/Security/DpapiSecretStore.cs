using System.Security.Cryptography;
using System.Text;
using AttendanceBot.Core.Interfaces;

namespace AttendanceBot.Infrastructure.Security;

public sealed class DpapiSecretStore : ISecretStore
{
    public string Protect(string value, string purpose)
    {
        var entropy = SHA256.HashData(Encoding.UTF8.GetBytes(purpose));
        var data = ProtectedData.Protect(Encoding.UTF8.GetBytes(value), entropy, DataProtectionScope.CurrentUser);
        return Convert.ToBase64String(data);
    }
    public string? Unprotect(string? value, string purpose)
    {
        if (string.IsNullOrWhiteSpace(value)) return null;
        try { var entropy = SHA256.HashData(Encoding.UTF8.GetBytes(purpose)); return Encoding.UTF8.GetString(ProtectedData.Unprotect(Convert.FromBase64String(value), entropy, DataProtectionScope.CurrentUser)); }
        catch (CryptographicException) { return null; }
    }
    public void Delete(string purpose) { }
}
