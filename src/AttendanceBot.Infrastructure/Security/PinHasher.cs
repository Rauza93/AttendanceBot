using System.Security.Cryptography;

namespace AttendanceBot.Infrastructure.Security;

public sealed class PinHasher
{
    public string Hash(string pin)
    {
        var salt = RandomNumberGenerator.GetBytes(16); var hash = Rfc2898DeriveBytes.Pbkdf2(pin, salt, 120_000, HashAlgorithmName.SHA256, 32);
        return $"{Convert.ToBase64String(salt)}.{Convert.ToBase64String(hash)}";
    }
    public bool Verify(string pin, string encoded)
    {
        var p = encoded.Split('.'); if (p.Length != 2) return false;
        try { var hash = Rfc2898DeriveBytes.Pbkdf2(pin, Convert.FromBase64String(p[0]), 120_000, HashAlgorithmName.SHA256, 32); return CryptographicOperations.FixedTimeEquals(hash, Convert.FromBase64String(p[1])); } catch (FormatException) { return false; }
    }
}
