using AttendanceBot.Core.Interfaces;
using AttendanceBot.Core.Models;
using AttendanceBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AttendanceBot.Automation;

public sealed class GpsAttendanceProvider : ConfiguredProviderBase
{
    private readonly IDbContextFactory<AttendanceDbContext> _dbFactory;
    public GpsAttendanceProvider(ProviderOptions options, IDbContextFactory<AttendanceDbContext> dbFactory) : base(options) => _dbFactory = dbFactory;
    public override ProviderKind Kind => ProviderKind.Gps;
    protected override bool PrerequisitesAvailable(ProviderContext context, out string message)
    {
        using var db = _dbFactory.CreateDbContext();
        var config = db.WebsiteConfigurations.AsNoTracking().SingleOrDefault(x => x.ProviderType == "GPS");
        if (config is null) { message = "Website GPS belum dikonfigurasi"; return false; }
        if (!config.Enabled) { message = "Website GPS dinonaktifkan"; return false; }
        Options.LoginUrl = config.LoginUrl;
        Options.AttendanceUrl = config.AttendanceUrl;
        message = "Windows Location Service dan Edge permission must be enabled by the operator; GPS spoofing is never used.";
        return true;
    }
}
