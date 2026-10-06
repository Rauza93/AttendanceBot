using AttendanceBot.Core.Interfaces;
using AttendanceBot.Core.Models;

namespace AttendanceBot.Automation;

public sealed class NetworkAttendanceProvider : ConfiguredProviderBase
{
    private readonly INetworkChecker _checker;
    private readonly NetworkRules _rules;
    public NetworkAttendanceProvider(ProviderOptions options, INetworkChecker checker, NetworkRules rules) : base(options)
    { _checker = checker; _rules = rules; }
    public override ProviderKind Kind => ProviderKind.Network;
    protected override bool PrerequisitesAvailable(ProviderContext context, out string message) { var r = _checker.CheckAsync(_rules, CancellationToken.None).GetAwaiter().GetResult(); message = r.Message; return r.IsAllowed && !string.IsNullOrWhiteSpace(Options.LoginUrl); }
}
