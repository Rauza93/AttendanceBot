using AttendanceBot.Core.Models;
using AttendanceBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging;

namespace AttendanceBot.Worker;

public sealed class WorkerService(ILogger<WorkerService> log, IServiceScopeFactory scopes) : BackgroundService
{
    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        using var scope = scopes.CreateScope(); var db = scope.ServiceProvider.GetRequiredService<AttendanceDbContext>();
        if (db.Database.GetMigrations().Any()) await db.Database.MigrateAsync(stoppingToken); else await db.Database.EnsureCreatedAsync(stoppingToken);
        while (!stoppingToken.IsCancellationRequested) { log.LogInformation("AttendanceBot background agent is running."); await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
    }
}
