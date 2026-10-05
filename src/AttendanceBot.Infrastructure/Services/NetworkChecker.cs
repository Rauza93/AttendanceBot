using System.Net.NetworkInformation;
using System.Net;
using AttendanceBot.Core.Interfaces;

namespace AttendanceBot.Infrastructure.Services;

public sealed class NetworkChecker : INetworkChecker
{
    public Task<NetworkCheckResult> CheckAsync(NetworkRules rules, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        var interfaces = NetworkInterface.GetAllNetworkInterfaces().Where(x => x.OperationalStatus == OperationalStatus.Up).ToArray();
        var selected = interfaces.FirstOrDefault(x => string.IsNullOrWhiteSpace(rules.InterfaceName) || x.Name.Equals(rules.InterfaceName, StringComparison.OrdinalIgnoreCase));
        if (selected is null) return Task.FromResult(new NetworkCheckResult(false, "Required network interface is unavailable."));
        var props = selected.GetIPProperties();
        var ip = props.UnicastAddresses.FirstOrDefault(x => x.Address.AddressFamily == System.Net.Sockets.AddressFamily.InterNetwork)?.Address;
        var gateway = props.GatewayAddresses.FirstOrDefault()?.Address;
        if (!string.IsNullOrWhiteSpace(rules.Gateway) && !string.Equals(gateway?.ToString(), rules.Gateway, StringComparison.OrdinalIgnoreCase)) return Task.FromResult(new NetworkCheckResult(false, "Gateway does not match configured rule.", null, selected.Name, ip?.ToString()));
        if (!string.IsNullOrWhiteSpace(rules.AllowedIpRange) && (ip is null || !IsInCidr(ip, rules.AllowedIpRange))) return Task.FromResult(new NetworkCheckResult(false, "IP address is outside the allowed range.", null, selected.Name, ip?.ToString()));
        return Task.FromResult(new NetworkCheckResult(true, "Network prerequisites passed.", selected.Description, selected.Name, ip?.ToString()));
    }
    private static bool IsInCidr(IPAddress address, string cidr)
    {
        var parts = cidr.Split('/'); if (parts.Length != 2 || !IPAddress.TryParse(parts[0], out var network) || !int.TryParse(parts[1], out var bits)) return false;
        var a = address.GetAddressBytes(); var n = network.GetAddressBytes(); if (a.Length != n.Length || bits is < 0 or > 32) return false;
        for (var i = 0; i < a.Length; i++) { var mask = bits >= 8 ? 255 : bits <= 0 ? 0 : 256 - (1 << (8 - bits)); if ((a[i] & mask) != (n[i] & mask)) return false; bits -= Math.Min(bits, 8); } return true;
    }
}
