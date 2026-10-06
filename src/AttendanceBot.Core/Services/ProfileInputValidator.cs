using System.Globalization;

namespace AttendanceBot.Core.Services;

public static class ProfileInputValidator
{
    public static string? Validate(
        string? name,
        string? queueOrderText,
        bool gpsEnabled,
        string? gpsUsername,
        string? gpsPassword,
        bool networkEnabled,
        string? networkUsername,
        string? networkPassword,
        out int queueOrder)
    {
        queueOrder = 0;
        if (string.IsNullOrWhiteSpace(name)) return "Nama wajib diisi.";
        if (!int.TryParse(queueOrderText, NumberStyles.Integer, CultureInfo.InvariantCulture, out queueOrder) || queueOrder < 0)
            return "Queue Order harus berupa angka nol atau lebih.";
        if (!CredentialPairValid(gpsEnabled, gpsUsername, gpsPassword) || !CredentialPairValid(networkEnabled, networkUsername, networkPassword))
            return "Username dan password website harus diisi berpasangan.";
        return null;
    }

    private static bool CredentialPairValid(bool enabled, string? username, string? password)
    {
        if (!enabled) return true;
        return string.IsNullOrWhiteSpace(username) == string.IsNullOrWhiteSpace(password);
    }
}
