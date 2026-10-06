using AttendanceBot.Core.Interfaces;
using AttendanceBot.Core.Models;
using AttendanceBot.Core.Services;
using AttendanceBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using System.Globalization;
using System.Windows;
using System.Windows.Controls;
using Button = System.Windows.Controls.Button;
using CheckBox = System.Windows.Controls.CheckBox;
using MessageBox = System.Windows.MessageBox;
using Mouse = System.Windows.Input.Mouse;

namespace AttendanceBot.App;

public partial class MainWindow : Window
{
    private readonly IDbContextFactory<AttendanceDbContext> _dbFactory;
    private readonly ISecretStore _secrets;
    private readonly ITelegramService _telegram;
    private int? _editingProfileId;
    private bool _loading;

    public MainWindow(IDbContextFactory<AttendanceDbContext> dbFactory, ISecretStore secrets, ITelegramService telegram)
    {
        _dbFactory = dbFactory;
        _secrets = secrets;
        _telegram = telegram;
        InitializeComponent();
        Loaded += async (_, _) => await ReloadAllAsync();
    }

    private async Task ReloadAllAsync()
    {
        try
        {
            _loading = true;
            await LoadProfilesAsync();
            await LoadScheduleAsync();
            await LoadSettingsAsync();
            await LoadHistoryAsync();
            await LoadDashboardAsync();
            await LoadLogsAsync();
            StatusText.Text = "Data berhasil dimuat.";
        }
        catch (Exception ex) { ShowError("Data gagal dimuat", ex); }
        finally { _loading = false; }
    }

    private async Task LoadProfilesAsync(int? selectId = null)
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var profiles = await db.AttendanceProfiles.AsNoTracking().OrderBy(p => p.QueueOrder).ThenBy(p => p.Name).ToListAsync();
        ProfileList.ItemsSource = profiles;
        if (selectId.HasValue) ProfileList.SelectedItem = profiles.FirstOrDefault(p => p.Id == selectId.Value);
    }

    private async Task LoadScheduleAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var list = await db.Schedules.AsNoTracking().ToListAsync();
        var clockIn = list.FirstOrDefault(s => s.AttendanceType == AttendanceType.ClockIn);
        var clockOut = list.FirstOrDefault(s => s.AttendanceType == AttendanceType.ClockOut);
        ClockInEnabled.IsChecked = clockIn?.Enabled ?? false;
        ClockOutEnabled.IsChecked = clockOut?.Enabled ?? false;
        ClockInTime.Text = (clockIn?.LocalTime ?? new TimeOnly(8, 0)).ToString("HH:mm", CultureInfo.InvariantCulture);
        ClockOutTime.Text = (clockOut?.LocalTime ?? new TimeOnly(17, 0)).ToString("HH:mm", CultureInfo.InvariantCulture);
        SetDays((clockIn?.ActiveDays ?? clockOut?.ActiveDays) ?? DayOfWeekFlags.EveryDay);
        var settings = await ReadSettingsAsync(db);
        ProfileDelay.Text = Get(settings, "Schedule.ProfileDelaySeconds", "0");
        RetryEnabled.IsChecked = Get(settings, "Schedule.RetryEnabled", "false") == "true";
        MaxRetry.Text = Get(settings, "Schedule.MaxRetry", "3");
        RetryDelay.Text = Get(settings, "Schedule.RetryDelaySeconds", "30");
    }

    private async Task LoadSettingsAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var values = await ReadSettingsAsync(db);
        StartWithWindows.IsChecked = Get(values, "General.StartWithWindows", "false") == "true";
        MinimizeToTray.IsChecked = Get(values, "General.MinimizeToTray", "false") == "true";
        RequiredSsid.Text = Get(values, "Network.RequiredSsid", "");
        IpRange.Text = Get(values, "Network.IpRange", "");
        Gateway.Text = Get(values, "Network.Gateway", "");
        EdgePath.Text = Get(values, "Browser.EdgePath", "");
        BrowserVisible.IsChecked = Get(values, "Browser.Visible", "true") == "true";
        BrowserTimeout.Text = Get(values, "Browser.TimeoutSeconds", "60");
        ScreenshotEnabled.IsChecked = Get(values, "Browser.ScreenshotEnabled", "false") == "true";
        var telegram = await db.TelegramSettings.OrderBy(t => t.Id).FirstOrDefaultAsync();
        TelegramEnabled.IsChecked = telegram?.Enabled ?? false;
        ChatId.Text = telegram?.ChatId ?? "";
        NotifySuccess.IsChecked = telegram?.NotifySuccess ?? true;
        NotifyFailure.IsChecked = telegram?.NotifyFailure ?? true;
        SendSummary.IsChecked = telegram?.SendSummary ?? true;
        SendScreenshot.IsChecked = telegram?.SendScreenshot ?? false;
        BotToken.Password = _secrets.Unprotect(telegram?.BotTokenSecret, "telegram-bot-token") ?? "";
    }

    private async Task LoadHistoryAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var results = await db.AttendanceResults.AsNoTracking().OrderByDescending(r => r.StartedAt).Take(200).ToListAsync();
        HistoryList.ItemsSource = results.Select(r => $"{r.StartedAt:yyyy-MM-dd HH:mm:ss} | Profile #{r.ProfileId} | {r.Provider} | {r.AttendanceType} | {r.Status} | {r.ResultMessage ?? r.ErrorMessage}").ToList();
    }

    private async Task LoadDashboardAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        NoProfilesText.Visibility = await db.AttendanceProfiles.AnyAsync() ? Visibility.Collapsed : Visibility.Visible;
        NoScheduleText.Visibility = await db.Schedules.AnyAsync(s => s.Enabled) ? Visibility.Collapsed : Visibility.Visible;
        var results = await db.AttendanceResults.AsNoTracking().OrderByDescending(r => r.StartedAt).Take(20).ToListAsync();
        DashboardResults.ItemsSource = results.Select(r => $"{r.StartedAt:yyyy-MM-dd HH:mm:ss} | Profile #{r.ProfileId} | {r.Provider} | {r.AttendanceType} | {r.Status} | {r.ResultMessage ?? r.ErrorMessage}").ToList();
    }

    private async Task LoadLogsAsync()
    {
        await using var db = await _dbFactory.CreateDbContextAsync();
        var entries = await db.AuditLogs.AsNoTracking().OrderByDescending(l => l.Timestamp).Take(200).ToListAsync();
        LogsList.ItemsSource = entries.Select(l => $"{l.Timestamp:yyyy-MM-dd HH:mm:ss} | {l.Level} | {l.Category} | {l.Action} | {l.Message}").ToList();
    }

    private void ProfileList_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || ProfileList.SelectedItem is not AttendanceProfile profile) return;
        _editingProfileId = profile.Id;
        ProfileName.Text = profile.Name;
        EmployeeCode.Text = profile.EmployeeCode ?? "";
        QueueOrder.Text = profile.QueueOrder.ToString(CultureInfo.InvariantCulture);
        ProfileEnabled.IsChecked = profile.Enabled;
        GpsEnabled.IsChecked = profile.Website1Enabled;
        NetworkEnabled.IsChecked = profile.Website2Enabled;
        GpsUsername.Text = profile.Website1Username;
        GpsPassword.Password = _secrets.Unprotect(profile.Website1PasswordSecret, $"profile:{profile.Id}:gps") ?? "";
        NetworkUsername.Text = profile.Website2Username;
        NetworkPassword.Password = _secrets.Unprotect(profile.Website2PasswordSecret, $"profile:{profile.Id}:network") ?? "";
        ProfileNotes.Text = profile.Notes ?? "";
    }

    private void NewProfile_Click(object sender, RoutedEventArgs e) => ClearProfileForm();
    private void AddProfileFromDashboard_Click(object sender, RoutedEventArgs e) { Pages.SelectedIndex = 1; ClearProfileForm(); }
    private void OpenSchedule_Click(object sender, RoutedEventArgs e) => Pages.SelectedIndex = 2;
    private void CancelProfile_Click(object sender, RoutedEventArgs e) { ClearProfileForm(); ProfileList.SelectedItem = null; }

    private void ClearProfileForm()
    {
        _editingProfileId = null;
        ProfileName.Clear(); EmployeeCode.Clear(); QueueOrder.Text = "0";
        ProfileEnabled.IsChecked = true; GpsEnabled.IsChecked = true; NetworkEnabled.IsChecked = true;
        GpsUsername.Clear(); GpsPassword.Clear(); NetworkUsername.Clear(); NetworkPassword.Clear(); ProfileNotes.Clear();
    }

    private async void SaveProfile_Click(object sender, RoutedEventArgs e)
    {
        var validationError = ProfileInputValidator.Validate(ProfileName.Text, QueueOrder.Text,
            GpsEnabled.IsChecked == true, GpsUsername.Text, GpsPassword.Password,
            NetworkEnabled.IsChecked == true, NetworkUsername.Text, NetworkPassword.Password, out var queueOrder);
        if (validationError is not null)
        { MessageBox.Show(validationError, "Validasi", MessageBoxButton.OK, MessageBoxImage.Warning); return; }

        await RunSaveAsync(async () =>
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            AttendanceProfile profile;
            if (_editingProfileId is int id) profile = await db.AttendanceProfiles.FirstAsync(p => p.Id == id);
            else { profile = new AttendanceProfile(); db.AttendanceProfiles.Add(profile); }
            profile.Name = ProfileName.Text.Trim(); profile.EmployeeCode = NullIfEmpty(EmployeeCode.Text);
            profile.QueueOrder = queueOrder; profile.Enabled = ProfileEnabled.IsChecked == true;
            profile.Website1Enabled = GpsEnabled.IsChecked == true; profile.Website2Enabled = NetworkEnabled.IsChecked == true;
            profile.Website1Username = GpsUsername.Text.Trim(); profile.Website2Username = NetworkUsername.Text.Trim();
            if (!string.IsNullOrEmpty(GpsPassword.Password)) profile.Website1PasswordSecret = _secrets.Protect(GpsPassword.Password, $"profile:{profile.Id}:gps");
            if (!string.IsNullOrEmpty(NetworkPassword.Password)) profile.Website2PasswordSecret = _secrets.Protect(NetworkPassword.Password, $"profile:{profile.Id}:network");
            profile.Notes = NullIfEmpty(ProfileNotes.Text); profile.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
            if (!string.IsNullOrEmpty(GpsPassword.Password)) profile.Website1PasswordSecret = _secrets.Protect(GpsPassword.Password, $"profile:{profile.Id}:gps");
            if (!string.IsNullOrEmpty(NetworkPassword.Password)) profile.Website2PasswordSecret = _secrets.Protect(NetworkPassword.Password, $"profile:{profile.Id}:network");
            await db.SaveChangesAsync();
            _editingProfileId = profile.Id;
            await LoadProfilesAsync(profile.Id);
            await LoadDashboardAsync();
        });
    }

    private async void DeleteProfile_Click(object sender, RoutedEventArgs e)
    {
        var id = _editingProfileId ?? (ProfileList.SelectedItem as AttendanceProfile)?.Id;
        if (id is null) { MessageBox.Show("Pilih profile yang akan dihapus.", "AttendanceBot"); return; }
        if (MessageBox.Show("Hapus profile yang dipilih? Riwayat absensi akan tetap tersimpan.", "Konfirmasi", MessageBoxButton.YesNo, MessageBoxImage.Question) != MessageBoxResult.Yes) return;
        await RunSaveAsync(async () =>
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var profile = await db.AttendanceProfiles.FindAsync(id.Value);
            if (profile is not null) { db.AttendanceProfiles.Remove(profile); await db.SaveChangesAsync(); }
            ClearProfileForm(); await LoadProfilesAsync(); await LoadDashboardAsync();
        });
    }

    private async void SaveSchedule_Click(object sender, RoutedEventArgs e)
    {
        if (!TimeOnly.TryParseExact(ClockInTime.Text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var inTime) ||
            !TimeOnly.TryParseExact(ClockOutTime.Text, "HH:mm", CultureInfo.InvariantCulture, DateTimeStyles.None, out var outTime))
        { MessageBox.Show("Jam harus menggunakan format HH:mm, misalnya 08:30.", "Validasi", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        if (!int.TryParse(ProfileDelay.Text, out var delay) || delay < 0 || !int.TryParse(MaxRetry.Text, out var maxRetry) || maxRetry < 0 || !int.TryParse(RetryDelay.Text, out var retryDelay) || retryDelay < 0)
        { MessageBox.Show("Delay dan jumlah retry harus berupa angka nol atau lebih.", "Validasi", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        var days = GetDays();
        if (days == DayOfWeekFlags.None) { MessageBox.Show("Pilih minimal satu hari.", "Validasi", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        await RunSaveAsync(async () =>
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            await UpsertScheduleAsync(db, AttendanceType.ClockIn, ClockInEnabled.IsChecked == true, inTime, days);
            await UpsertScheduleAsync(db, AttendanceType.ClockOut, ClockOutEnabled.IsChecked == true, outTime, days);
            await SaveSettingsValuesAsync(db, new Dictionary<string, string>
            {
                ["Schedule.ProfileDelaySeconds"] = delay.ToString(CultureInfo.InvariantCulture),
                ["Schedule.RetryEnabled"] = (RetryEnabled.IsChecked == true).ToString().ToLowerInvariant(),
                ["Schedule.MaxRetry"] = maxRetry.ToString(CultureInfo.InvariantCulture),
                ["Schedule.RetryDelaySeconds"] = retryDelay.ToString(CultureInfo.InvariantCulture)
            });
            await db.SaveChangesAsync();
            await LoadScheduleAsync(); await LoadDashboardAsync();
        });
    }

    private static async Task UpsertScheduleAsync(AttendanceDbContext db, AttendanceType type, bool enabled, TimeOnly time, DayOfWeekFlags days)
    {
        var schedule = await db.Schedules.FirstOrDefaultAsync(s => s.AttendanceType == type);
        if (schedule is null) { schedule = new Schedule { AttendanceType = type }; db.Schedules.Add(schedule); }
        schedule.Enabled = enabled; schedule.LocalTime = time; schedule.ActiveDays = days;
    }

    private async void SaveSettings_Click(object sender, RoutedEventArgs e)
    {
        if (!int.TryParse(BrowserTimeout.Text, out var timeout) || timeout <= 0)
        { MessageBox.Show("Timeout harus berupa angka lebih besar dari nol.", "Validasi", MessageBoxButton.OK, MessageBoxImage.Warning); return; }
        await RunSaveAsync(async () =>
        {
            await using var db = await _dbFactory.CreateDbContextAsync();
            var values = new Dictionary<string, string>
            {
                ["General.StartWithWindows"] = Bool(StartWithWindows), ["General.MinimizeToTray"] = Bool(MinimizeToTray),
                ["Network.RequiredSsid"] = RequiredSsid.Text.Trim(), ["Network.IpRange"] = IpRange.Text.Trim(), ["Network.Gateway"] = Gateway.Text.Trim(),
                ["Browser.EdgePath"] = EdgePath.Text.Trim(), ["Browser.Visible"] = Bool(BrowserVisible),
                ["Browser.TimeoutSeconds"] = timeout.ToString(CultureInfo.InvariantCulture), ["Browser.ScreenshotEnabled"] = Bool(ScreenshotEnabled)
            };
            await SaveSettingsValuesAsync(db, values);
            var telegram = await db.TelegramSettings.OrderBy(t => t.Id).FirstOrDefaultAsync();
            if (telegram is null) { telegram = new TelegramSetting(); db.TelegramSettings.Add(telegram); }
            telegram.Enabled = TelegramEnabled.IsChecked == true; telegram.ChatId = NullIfEmpty(ChatId.Text);
            telegram.NotifySuccess = NotifySuccess.IsChecked == true; telegram.NotifyFailure = NotifyFailure.IsChecked == true;
            telegram.SendSummary = SendSummary.IsChecked == true; telegram.SendScreenshot = SendScreenshot.IsChecked == true;
            if (!string.IsNullOrWhiteSpace(BotToken.Password)) telegram.BotTokenSecret = _secrets.Protect(BotToken.Password, "telegram-bot-token");
            await db.SaveChangesAsync();
            await LoadSettingsAsync();
        });
    }

    private async void TestTelegram_Click(object sender, RoutedEventArgs e)
    {
        try
        {
            SetBusy(true);
            await using var db = await _dbFactory.CreateDbContextAsync();
            var telegram = await db.TelegramSettings.OrderBy(t => t.Id).FirstOrDefaultAsync() ?? new TelegramSetting();
            telegram.Enabled = true; telegram.ChatId = NullIfEmpty(ChatId.Text) ?? telegram.ChatId;
            if (!string.IsNullOrWhiteSpace(BotToken.Password)) telegram.BotTokenSecret = _secrets.Protect(BotToken.Password, "telegram-bot-token");
            var ok = await _telegram.TestAsync(telegram, CancellationToken.None);
            MessageBox.Show(ok ? "Test Telegram berhasil." : "Test Telegram gagal. Periksa token, Chat ID, dan koneksi internet.", "AttendanceBot", MessageBoxButton.OK, ok ? MessageBoxImage.Information : MessageBoxImage.Warning);
        }
        catch (Exception ex) { ShowError("Test Telegram gagal", ex); }
        finally { SetBusy(false); }
    }

    private async Task RunSaveAsync(Func<Task> save)
    {
        try { SetBusy(true); await save(); StatusText.Text = "Data berhasil disimpan."; MessageBox.Show("Data berhasil disimpan.", "AttendanceBot", MessageBoxButton.OK, MessageBoxImage.Information); }
        catch (Exception ex) { StatusText.Text = "Penyimpanan gagal."; ShowError("Data gagal disimpan", ex); }
        finally { SetBusy(false); }
    }

    private void SetBusy(bool busy)
    {
        Mouse.OverrideCursor = busy ? System.Windows.Input.Cursors.Wait : null;
        foreach (var button in FindVisualChildren<Button>(this)) button.IsEnabled = !busy;
    }

    private static IEnumerable<T> FindVisualChildren<T>(DependencyObject parent) where T : DependencyObject
    {
        for (var i = 0; i < System.Windows.Media.VisualTreeHelper.GetChildrenCount(parent); i++)
        {
            var child = System.Windows.Media.VisualTreeHelper.GetChild(parent, i);
            if (child is T typed) yield return typed;
            foreach (var descendant in FindVisualChildren<T>(child)) yield return descendant;
        }
    }

    private static void ShowError(string action, Exception ex) => MessageBox.Show($"{action}: {ex.GetBaseException().Message}", "AttendanceBot", MessageBoxButton.OK, MessageBoxImage.Error);

    private static async Task<Dictionary<string, string>> ReadSettingsAsync(AttendanceDbContext db) =>
        await db.ApplicationSettings.AsNoTracking().ToDictionaryAsync(x => x.Key, x => x.Value);

    private static string Get(IReadOnlyDictionary<string, string> values, string key, string fallback) => values.TryGetValue(key, out var value) ? value : fallback;
    private static string Bool(System.Windows.Controls.CheckBox box) => (box.IsChecked == true).ToString().ToLowerInvariant();
    private static string? NullIfEmpty(string value) => string.IsNullOrWhiteSpace(value) ? null : value.Trim();

    private static async Task SaveSettingsValuesAsync(AttendanceDbContext db, IReadOnlyDictionary<string, string> values)
    {
        foreach (var pair in values)
        {
            var setting = await db.ApplicationSettings.FindAsync(pair.Key);
            if (setting is null) db.ApplicationSettings.Add(new ApplicationSetting { Key = pair.Key, Value = pair.Value });
            else setting.Value = pair.Value;
        }
    }

    private DayOfWeekFlags GetDays()
    {
        var days = DayOfWeekFlags.None;
        if (Monday.IsChecked == true) days |= DayOfWeekFlags.Monday;
        if (Tuesday.IsChecked == true) days |= DayOfWeekFlags.Tuesday;
        if (Wednesday.IsChecked == true) days |= DayOfWeekFlags.Wednesday;
        if (Thursday.IsChecked == true) days |= DayOfWeekFlags.Thursday;
        if (Friday.IsChecked == true) days |= DayOfWeekFlags.Friday;
        if (Saturday.IsChecked == true) days |= DayOfWeekFlags.Saturday;
        if (Sunday.IsChecked == true) days |= DayOfWeekFlags.Sunday;
        return days;
    }

    private void SetDays(DayOfWeekFlags days)
    {
        Monday.IsChecked = days.HasFlag(DayOfWeekFlags.Monday); Tuesday.IsChecked = days.HasFlag(DayOfWeekFlags.Tuesday);
        Wednesday.IsChecked = days.HasFlag(DayOfWeekFlags.Wednesday); Thursday.IsChecked = days.HasFlag(DayOfWeekFlags.Thursday);
        Friday.IsChecked = days.HasFlag(DayOfWeekFlags.Friday); Saturday.IsChecked = days.HasFlag(DayOfWeekFlags.Saturday);
        Sunday.IsChecked = days.HasFlag(DayOfWeekFlags.Sunday);
    }

    private async void Pages_SelectionChanged(object sender, SelectionChangedEventArgs e)
    {
        if (_loading || !ReferenceEquals(e.Source, Pages)) return;
        try
        {
            switch (Pages.SelectedIndex)
            {
                case 0: await LoadDashboardAsync(); break;
                case 1: await LoadProfilesAsync(_editingProfileId); break;
                case 2: await LoadScheduleAsync(); break;
                case 3: await LoadHistoryAsync(); break;
                case 4: await LoadSettingsAsync(); break;
                case 5: await LoadLogsAsync(); break;
            }
        }
        catch (Exception ex) { ShowError("Halaman gagal dimuat", ex); }
    }
}
