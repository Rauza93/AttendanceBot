using AttendanceBot.Core.Models;

namespace AttendanceBot.Core.Services;

public static class WebsiteConfigurationValidator
{
    public static string? Validate(WebsiteConfiguration configuration)
    {
        if (string.IsNullOrWhiteSpace(configuration.Name)) return "Name wajib diisi.";
        if (!ValidUrl(configuration.BaseUrl)) return "Base URL harus berupa URL http atau https yang valid.";
        if (configuration.Enabled && !ValidUrl(configuration.LoginUrl)) return "Login URL wajib berupa URL http atau https yang valid saat provider aktif.";
        if (configuration.Enabled && !ValidUrl(configuration.AttendanceUrl)) return "Attendance URL wajib berupa URL http atau https yang valid saat provider aktif.";
        if (!string.IsNullOrWhiteSpace(configuration.LoginUrl) && !ValidUrl(configuration.LoginUrl)) return "Login URL harus berupa URL http atau https yang valid.";
        if (!string.IsNullOrWhiteSpace(configuration.AttendanceUrl) && !ValidUrl(configuration.AttendanceUrl)) return "Attendance URL harus berupa URL http atau https yang valid.";
        return null;
    }

    public static bool ValidUrl(string? value) => !string.IsNullOrWhiteSpace(value)
        && Uri.TryCreate(value.Trim(), UriKind.Absolute, out var uri)
        && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps)
        && !string.IsNullOrWhiteSpace(uri.Host);
}
