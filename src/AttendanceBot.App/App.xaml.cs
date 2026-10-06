using AttendanceBot.Core.Interfaces;
using AttendanceBot.Infrastructure.Persistence;
using AttendanceBot.Infrastructure.Security;
using AttendanceBot.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using System.IO;
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
                    services.AddTransient<MainWindow>();
                })
                .Build();

            await _host.StartAsync();
            var factory = _host.Services.GetRequiredService<IDbContextFactory<AttendanceDbContext>>();
            await using (var db = await factory.CreateDbContextAsync())
                await db.Database.EnsureCreatedAsync();

            _host.Services.GetRequiredService<MainWindow>().Show();
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

    protected override async void OnExit(ExitEventArgs e)
    {
        if (_host is not null)
        {
            await _host.StopAsync();
            _host.Dispose();
        }
        base.OnExit(e);
    }
}
