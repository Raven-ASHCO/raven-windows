using System;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;

namespace Raven.Windows.Core.Presentation;

/// <summary>
/// Whether this computer reaches its network over Wi-Fi: the interface that holds the address
/// raven-node reports (node.status "address"), else the one with a default gateway. A computer on
/// a cable is "on your network", never "on your Wi-Fi" (E1).
/// </summary>
public static class NetworkKind
{
    public static bool IsWiFi(string? address)
    {
        try
        {
            NetworkInterface[] up = NetworkInterface.GetAllNetworkInterfaces()
                .Where(n => n.OperationalStatus == OperationalStatus.Up
                            && n.NetworkInterfaceType != NetworkInterfaceType.Loopback)
                .ToArray();
            NetworkInterface? carrier = null;
            if (IPAddress.TryParse(address, out IPAddress? ip))
                carrier = up.FirstOrDefault(n => n.GetIPProperties().UnicastAddresses.Any(u => u.Address.Equals(ip)));
            carrier ??= up.FirstOrDefault(n => n.GetIPProperties().GatewayAddresses.Count > 0);
            return carrier?.NetworkInterfaceType == NetworkInterfaceType.Wireless80211;
        }
        catch (Exception)
        {
            return false; // unknown: the plain "your network"
        }
    }
}
