using AttendanceBot.Core.Interfaces;
using AttendanceBot.Core.Models;

namespace AttendanceBot.Automation;

public sealed class NetworkAttendanceProvider(ProviderOptions options, INetworkChecker checker, NetworkRules rules) : ConfiguredProviderBase(options)
{
    public override ProviderKind Kind => ProviderKind.Network;
    protected override bool PrerequisitesAvailable(ProviderContext context, out string message) { var r = checker.CheckAsync(rules, CancellationToken.None).GetAwaiter().GetResult(); message = r.Message; return r.IsAllowed && !string.IsNullOrWhiteSpace(options.LoginUrl); }
}
