using System.Collections.Generic;
using System.Linq;
using System.Net;
using System.Net.NetworkInformation;
using System.Net.Sockets;

namespace GivreClone.App.Services;

public sealed record NetAdapter(string Id, string Name, IPAddress Ip, string Description);

public static class NetworkInterfaces
{
    /// <summary>Interfaces actives (non-loopback) ayant une adresse IPv4. Id = GUID stable de l'interface.</summary>
    public static List<NetAdapter> List()
    {
        var result = new List<NetAdapter>();
        foreach (var ni in NetworkInterface.GetAllNetworkInterfaces())
        {
            if (ni.OperationalStatus != OperationalStatus.Up || ni.NetworkInterfaceType == NetworkInterfaceType.Loopback)
                continue;
            var ip = ni.GetIPProperties().UnicastAddresses
                .FirstOrDefault(u => u.Address.AddressFamily == AddressFamily.InterNetwork)?.Address;
            if (ip is null) continue;
            result.Add(new NetAdapter(ni.Id, ni.Name, ip, ni.Description));
        }
        return result;
    }
}
