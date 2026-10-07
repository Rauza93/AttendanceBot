using AttendanceBot.Core.Interfaces;
using AttendanceBot.Core.Models;
using AttendanceBot.Infrastructure.Persistence;
using Microsoft.EntityFrameworkCore;

namespace AttendanceBot.Automation;

public sealed class NetworkAttendanceProvider : ConfiguredProviderBase
{
    private readonly INetworkChecker _checker;
    private readonly NetworkRules _rules;
    private readonly IDbContextFactory<AttendanceDbContext> _dbFactory;
    public NetworkAttendanceProvider(ProviderOptions options, INetworkChecker checker, NetworkRules rules, IDbContextFactory<AttendanceDbContext> dbFactory) : base(options)
    { _checker = checker; _rules = rules; _dbFactory = dbFactory; }
    public override ProviderKind Kind => ProviderKind.Network;
    protected override bool PrerequisitesAvailable(ProviderContext context, out string message)
    {
        using var db = _dbFactory.CreateDbContext();
        var config = db.WebsiteConfigurations.AsNoTracking().SingleOrDefault(x => x.ProviderType == "NETWORK");
        if (config is null) { message = "Website Jaringan belum dikonfigurasi"; return false; }
        if (!config.Enabled) { message = "Website Jaringan dinonaktifkan"; return false; }
        Options.LoginUrl = config.LoginUrl; Options.AttendanceUrl = config.AttendanceUrl;
        var result = _checker.CheckAsync(_rules, CancellationToken.None).GetAwaiter().GetResult();
        message = result.Message;
        return result.IsAllowed;
    }
}
