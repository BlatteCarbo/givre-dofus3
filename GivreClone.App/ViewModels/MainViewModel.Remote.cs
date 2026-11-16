using System;
using System.Collections.Generic;
using System.Linq;
using System.Net.NetworkInformation;
using System.Net.Sockets;
using GivreClone.App.Services;
using GivreClone.App.Services.Mcp;

namespace GivreClone.App.ViewModels;

// Contrôle à distance (téléphone) : démarrage / arrêt à chaud depuis les réglages. Désactivé par défaut.
public partial class MainViewModel
{
    public RemoteControlServer? RemoteControl { get; private set; }

    /// <summary>Démarre (true) ou arrête (false) le serveur. Retourne false si le démarrage échoue (port occupé).</summary>
    public bool SetRemote(bool on)
    {
        AppConfig.Current.RemoteEnabled = on;
        AppConfig.Save();
        RemoteControl?.Dispose(); RemoteControl = null;
        if (!on) return true;
        RemoteControl = new RemoteControlServer(LocalControl ?? new LocalControlBridge(BotsVM));
        if (RemoteControl.Start()) return true;
        RemoteControl = null;
        AppConfig.Current.RemoteEnabled = false; AppConfig.Save();
        return false;
    }

    public void StartRemoteIfEnabled() { if (AppConfig.Current.RemoteEnabled && RemoteControl is null) SetRemote(true); }

    /// <summary>Adresses où le téléphone peut joindre l'app : Tailscale (100.x) en premier, puis le réseau local si autorisé.</summary>
    public static IReadOnlyList<string> RemoteAddresses()
    {
        var res = new List<string>();
        try
        {
            foreach (var ni in NetworkInterface.GetAllNetworkInterfaces().Where(n => n.OperationalStatus == OperationalStatus.Up))
                foreach (var ua in ni.GetIPProperties().UnicastAddresses)
                    if (ua.Address.AddressFamily == AddressFamily.InterNetwork)
                    {
                        var b = ua.Address.GetAddressBytes();
                        bool tailscale = b[0] == 100 && b[1] >= 64 && b[1] <= 127;
                        bool lan = b[0] == 10 || (b[0] == 172 && b[1] >= 16 && b[1] <= 31) || (b[0] == 192 && b[1] == 168);
                        if (tailscale || (lan && AppConfig.Current.RemoteAllowLan)) res.Add($"http://{ua.Address}:{AppConfig.Current.RemotePort}" + (tailscale ? "  (Tailscale)" : "  (réseau local)"));
                    }
        }
        catch (NetworkInformationException) { }
        return res.OrderByDescending(a => a.Contains("Tailscale")).ToList();
    }
}
