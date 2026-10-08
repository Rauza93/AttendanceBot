using AttendanceBot.Core.Interfaces;
using AttendanceBot.Infrastructure.Persistence;
using AttendanceBot.Infrastructure.Security;
using AttendanceBot.Infrastructure.Services;
using AttendanceBot.Core.Services;
using AttendanceBot.Automation;
using AttendanceBot.Core.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.IO;
using System.Diagnostics;
using Microsoft.Win32;
using System.Net.Http;
using System.Windows;
using MessageBox = System.Windows.MessageBox;

namespace AttendanceBot.App;

public partial class App : System.Windows.Application
{
    private IHost? _host;

    protected override async void OnStartup(StartupEventArgs e)
    {
        base.OnStartup(e);
        try
        {
            var dataDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AttendanceBot");
            Directory.CreateDirectory(dataDirectory);
            var logDirectory = Path.Combine(dataDirectory, "Logs");
            Directory.CreateDirectory(logDirectory);
            var databasePath = Path.Combine(dataDirectory, "attendance.db");

            _host = Host.CreateDefaultBuilder()
                .ConfigureServices((_, services) =>
                {
                    services.AddDbContextFactory<AttendanceDbContext>(options => options.UseSqlite($"Data Source={databasePath}"));
                    services.AddSingleton<ISecretStore, DpapiSecretStore>();
                    services.AddSingleton<ITelegramService>(provider =>
                        new TelegramService(new HttpClient(), provider.GetRequiredService<ISecretStore>()));
                    services.AddSingleton<AutomationRuntimeService>();
                    services.AddSingleton<INetworkChecker, NetworkChecker>();
                    services.AddSingleton(new NetworkRules(null, null, null, null));
                    services.AddSingleton<ProviderOptions>();
                    services.AddSingleton<IAttendanceProvider, GpsAttendanceProvider>();
                    services.AddSingleton<IAttendanceProvider, NetworkAttendanceProvider>();
                    services.AddSingleton<AttendanceAutomationService>();
                    services.AddTransient<MainWindow>();
                })
                .Build();

            await _host.StartAsync();
            var factory = _host.Services.GetRequiredService<IDbContextFactory<AttendanceDbContext>>();
            await using (var db = await factory.CreateDbContextAsync())
            {
                await db.Database.EnsureCreatedAsync();
                await db.Database.MigrateAsync();
            }

            var window = _host.Services.GetRequiredService<MainWindow>();
            window.Show();
            await using (var db = await factory.CreateDbContextAsync())
            {
                var settings = await db.ApplicationSettings.AsNoTracking().ToDictionaryAsync(x => x.Key, x => x.Value);
                if (settings.TryGetValue("General.StartWithWindows", out var startWindows) && startWindows == "true" &&
                    settings.TryGetValue("General.AutoStartAutomation", out var autoStart) && autoStart == "true")
                {
                    try
                    {
                        await _host.Services.GetRequiredService<AttendanceAutomationService>().StartAsync(automatic: true);
                        if (settings.TryGetValue("General.MinimizeToTray", out var minimize) && minimize == "true") window.Hide();
                    }
                    catch (Exception startError)
                    {
                        _host.Services.GetRequiredService<AutomationRuntimeService>().SetError(startError.GetBaseException().Message);
                        await File.AppendAllTextAsync(Path.Combine(logDirectory, "startup.log"), $"{DateTimeOffset.Now:O} Auto-start failed: {startError}\n");
                    }
                }
            }
        }
        catch (Exception ex)
        {
            try
            {
                var logDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AttendanceBot", "Logs");
                Directory.CreateDirectory(logDirectory);
                await File.AppendAllTextAsync(Path.Combine(logDirectory, "startup.log"), $"{DateTimeOffset.Now:O} {ex}\n");
            }
            catch { }
            MessageBox.Show($"AttendanceBot gagal dimulai: {ex.Message}", "AttendanceBot", MessageBoxButton.OK, MessageBoxImage.Error);
            Shutdown(-1);
        }
    }

    public static void ConfigureWindowsStartup(bool enabled)
    {
        if (!OperatingSystem.IsWindows()) return;
        using var key = Registry.CurrentUser.OpenSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", writable: true)
            ?? Registry.CurrentUser.CreateSubKey("Software\\Microsoft\\Windows\\CurrentVersion\\Run", writable: true);
        if (!enabled) key.DeleteValue("AttendanceBot", throwOnMissingValue: false);
        else
        {
            var executable = Process.GetCurrentProcess().MainModule?.FileName;
            if (!string.IsNullOrWhiteSpace(executable)) key.SetValue("AttendanceBot", $"\"{executable}\"");
        }
    }

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            try { await _host.Services.GetRequiredService<AttendanceAutomationService>().StopAsync(); } catch { }
            await _host.StopAsync();
            _host.Dispose();
        }
        base.OnExit(e);
    }
}
