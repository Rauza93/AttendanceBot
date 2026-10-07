using AttendanceBot.Core.Models;
using Microsoft.EntityFrameworkCore;

namespace AttendanceBot.Infrastructure.Persistence;

public sealed class AttendanceDbContext(DbContextOptions<AttendanceDbContext> options) : DbContext(options)
{
    public DbSet<AttendanceProfile> AttendanceProfiles => Set<AttendanceProfile>();
    public DbSet<WebsiteConfiguration> WebsiteConfigurations => Set<WebsiteConfiguration>();
    public DbSet<Schedule> Schedules => Set<Schedule>();
    public DbSet<AttendanceRun> AttendanceRuns => Set<AttendanceRun>();
    public DbSet<AttendanceResult> AttendanceResults => Set<AttendanceResult>();
    public DbSet<ApplicationSetting> ApplicationSettings => Set<ApplicationSetting>();
    public DbSet<TelegramSetting> TelegramSettings => Set<TelegramSetting>();
    public DbSet<AuditLog> AuditLogs => Set<AuditLog>();

    protected override void OnModelCreating(ModelBuilder b)
    {
        b.Entity<ApplicationSetting>().HasKey(x => x.Key);
        b.Entity<WebsiteConfiguration>().HasIndex(x => x.ProviderType).IsUnique();
        b.Entity<AttendanceResult>().HasIndex(x => new { x.ProfileId, x.Provider, x.AttendanceType, x.StartedAt });
        b.Entity<AttendanceProfile>().Property(x => x.Website1PasswordSecret).HasColumnName("Website1PasswordEncrypted");
        b.Entity<AttendanceProfile>().Property(x => x.Website2PasswordSecret).HasColumnName("Website2PasswordEncrypted");
        b.Entity<TelegramSetting>().Property(x => x.BotTokenSecret).HasColumnName("BotTokenEncrypted");
    }
}
