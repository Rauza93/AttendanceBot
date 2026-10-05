using AttendanceBot.Core.Interfaces;
using AttendanceBot.Infrastructure.Persistence;
using AttendanceBot.Infrastructure.Security;
using AttendanceBot.Infrastructure.Services;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;
using Serilog;
using AttendanceBot.Worker;

var dataRoot = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "AttendanceBot");
Directory.CreateDirectory(dataRoot);
Log.Logger = new LoggerConfiguration().Enrich.FromLogContext().WriteTo.File(Path.Combine(dataRoot, "Logs", "attendance-.log"), rollingInterval: RollingInterval.Day, retainedFileCountLimit: 31, shared: true).CreateLogger();
try
{
    var builder = Host.CreateApplicationBuilder(args);
    builder.Logging.ClearProviders(); builder.Services.AddSerilog();
    builder.Services.AddDbContext<AttendanceDbContext>(o => o.UseSqlite($"Data Source={Path.Combine(dataRoot, "attendance.db")}"));
    builder.Services.AddSingleton<ISecretStore, DpapiSecretStore>();
    builder.Services.AddSingleton<INetworkChecker, NetworkChecker>();
    builder.Services.AddHttpClient<ITelegramService, TelegramService>();
    builder.Services.AddHostedService<WorkerService>();
    await builder.Build().RunAsync();
}
finally { await Log.CloseAndFlushAsync(); }
