using System.Net.Http.Json;
using AttendanceBot.Core.Interfaces;
using AttendanceBot.Core.Models;

namespace AttendanceBot.Infrastructure.Services;

public sealed class TelegramService(HttpClient http, ISecretStore secrets) : ITelegramService
{
    public async Task<bool> TestAsync(TelegramSetting settings, CancellationToken ct) { if (!TryGet(settings, out var token, out var chat)) return false; var r = await http.PostAsJsonAsync($"https://api.telegram.org/bot{token}/sendMessage", new { chat_id = chat, text = "AttendanceBot connection test." }, ct); return r.IsSuccessStatusCode; }
    public async Task SendResultAsync(AttendanceResult result, AttendanceProfile profile, TelegramSetting settings, CancellationToken ct)
    {
        if (!settings.Enabled || (result.Status == RunStatus.Success && !settings.NotifySuccess) || (result.Status != RunStatus.Success && !settings.NotifyFailure) || !TryGet(settings, out var token, out var chat)) return;
        var icon = result.Status == RunStatus.Success ? "SUCCESS" : result.Status == RunStatus.ManualRequired ? "MANUAL REQUIRED" : "FAILED";
        var text = $"ABSEN {icon}\nNama: {profile.Name}\nWebsite: {result.Provider}\nJenis: {result.AttendanceType}\nWaktu: {result.FinishedAt:HH:mm:ss}\nStatus: {result.Status}\nPesan: {result.ResultMessage ?? result.ErrorMessage}";
        await http.PostAsJsonAsync($"https://api.telegram.org/bot{token}/sendMessage", new { chat_id = chat, text }, ct);
    }
    public async Task SendSummaryAsync(IEnumerable<(AttendanceProfile Profile, IEnumerable<AttendanceResult> Results)> summary, TelegramSetting settings, AttendanceType type, CancellationToken ct)
    {
        if (!settings.Enabled || !settings.SendSummary || !TryGet(settings, out var token, out var chat)) return;
        var lines = summary.Select(x => $"{x.Profile.Name}: {string.Join(" | ", x.Results.Select(r => $"{r.Provider} {(r.Status == RunStatus.Success ? "OK" : "FAIL")}"))}");
        await http.PostAsJsonAsync($"https://api.telegram.org/bot{token}/sendMessage", new { chat_id = chat, text = $"RINGKASAN ABSEN {type}\n\n{string.Join("\n", lines)}" }, ct);
    }
    private bool TryGet(TelegramSetting s, out string token, out string chat) { token = secrets.Unprotect(s.BotTokenSecret, "telegram-bot-token") ?? ""; chat = s.ChatId ?? ""; return token.Length > 0 && chat.Length > 0; }
}
