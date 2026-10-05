using AttendanceBot.Core.Interfaces;
using AttendanceBot.Core.Models;

namespace AttendanceBot.Automation;

public sealed class GpsAttendanceProvider(ProviderOptions options) : ConfiguredProviderBase(options)
{
    public override ProviderKind Kind => ProviderKind.Gps;
    protected override bool PrerequisitesAvailable(ProviderContext context, out string message)
    {
        message = "Windows Location Service and Edge permission must be enabled by the operator; GPS spoofing is never used.";
        return !string.IsNullOrWhiteSpace(options.LoginUrl);
    }
}
