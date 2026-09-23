using System.Net;
using System.Net.Sockets;

namespace OAuthLearn.Api.Auth;

/// <summary>Approximate network address for the sessions list; the full address is never stored (research R9).</summary>
public static class IpMasker
{
    public static string? Mask(IPAddress? address)
    {
        if (address is null)
        {
            return null;
        }

        if (address.IsIPv4MappedToIPv6)
        {
            address = address.MapToIPv4();
        }

        if (address.AddressFamily == AddressFamily.InterNetwork)
        {
            var bytes = address.GetAddressBytes();
            return $"{bytes[0]}.{bytes[1]}.{bytes[2]}.x";
        }

        if (address.AddressFamily == AddressFamily.InterNetworkV6)
        {
            var bytes = address.GetAddressBytes();
            var groups = Enumerable.Range(0, 4).Select(i => ((bytes[2 * i] << 8) | bytes[2 * i + 1]).ToString("x"));
            return string.Join(':', groups) + ":…";
        }

        return null;
    }
}
