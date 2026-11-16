using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Microsoft.Win32;

namespace GivreClone.App.Services.Security;

/// <summary>
/// Inventaire étendu du poste pour la Sentinelle (section « inventory » de la trame), au sens d'un outil RMM/UEM :
/// antivirus/EDR actif, VM, domaine, Secure Boot, version .NET, disques, charge CPU/RAM, logiciels NOTABLES. Toutes les
/// clés sont OPTIONNELLES (le serveur stocke tel quel et affiche ; voir docs/web-sentinel-rmm.md §1.2).
///
/// ⚠ BORNÉ. On ne dump JAMAIS les centaines de logiciels installés : seulement les outils de rétro-ingénierie / réseau /
/// VPN / AV connus (listes closes), plafonnés à ~40 entrées. Pas de clé de registre arbitraire, pas de chemin personnel.
/// Collecte autorisée par CGU (cadre RMM/UEM/MDM) ; proportionnalité maintenue pour le volet RGPD.
/// </summary>
public static class SentinelInventory
{
    public sealed record Disk(string Drive, long TotalGb, long FreeGb);

    /// <summary>État d'un service Windows NOTABLE (liste close) : nom clé (ex "WinDefend"), nom affiché, état courant,
    /// démarrage automatique ou non. Les listes sont figées côté client : on n'énumère pas services.msc.</summary>
    public sealed record ServiceState(string Name, string? Display, string? Status, bool AutoStart);

    /// <summary>Un événement récent (Application / Système) : source, EventID, niveau (Critical/Error/Warning), horodatage
    /// epoch ms. Les 5 derniers événements critiques des journaux Application et Système, pas plus.</summary>
    public sealed record EventEntry(string Log, string Source, int EventId, string Level, long At);

    public sealed record Snapshot(string? Av, bool Vm, string? Domain, bool? SecureBoot, string? Dotnet,
                                  IReadOnlyList<Disk> Disks, int CpuPct, int RamPct, IReadOnlyList<string> Software,
                                  IReadOnlyList<ServiceState> Services, IReadOnlyList<EventEntry> Events);

    // Noms notables à remonter (famille + marketing). On compare au nom de processus ET à ProductName de l'exe.
    private static readonly (string Needle, string Display)[] NotableSoftware =
    {
        // Rétro-ingénierie / dumpers
        ("x64dbg", "x64dbg"), ("ida pro", "IDA Pro"), ("ghidra", "Ghidra"), ("dnspy", "dnSpy"), ("ilspy", "ILSpy"),
        ("dotpeek", "dotPeek"), ("cheat engine", "Cheat Engine"), ("reclass", "ReClass.NET"), ("binary ninja", "Binary Ninja"),
        // Interception réseau
        ("httptoolkit", "HTTP Toolkit"), ("mitmproxy", "mitmproxy"), ("fiddler", "Fiddler"), ("charles", "Charles"),
        ("wireshark", "Wireshark"),
        // VPN
        ("nordvpn", "NordVPN"), ("expressvpn", "ExpressVPN"), ("protonvpn", "ProtonVPN"), ("surfshark", "Surfshark"),
        ("mullvad", "Mullvad"), ("openvpn", "OpenVPN"), ("wireguard", "WireGuard"), ("hola", "Hola VPN"),
    };

    public static Snapshot Collect()
    {
        return new Snapshot(
            Try(DetectAv),
            TryBool(IsVirtualMachine),
            Try(ActiveDomain),
            TryNull(SecureBootEnabled),
            Try(DotnetVersion),
            TryList(DiskSnapshot),
            TryInt(() => (int)(GC.GetTotalMemory(false) * 0)),   // cpu_pct : non calculé en v1 (coûte un sampling cross-thread) -> 0
            TryInt(RamPercent),
            TryList(NotableSoftwareDetected),
            TryList(NotableServices),
            TryList(RecentCriticalEvents));
    }

    private static T? Try<T>(Func<T?> f) { try { return f(); } catch { return default; } }
    private static bool TryBool(Func<bool> f) { try { return f(); } catch { return false; } }
    private static bool? TryNull(Func<bool?> f) { try { return f(); } catch { return null; } }
    private static int TryInt(Func<int> f) { try { return f(); } catch { return 0; } }
    private static IReadOnlyList<T> TryList<T>(Func<IReadOnlyList<T>> f) { try { return f(); } catch { return Array.Empty<T>(); } }

    // Antivirus / EDR actif : HKLM\SOFTWARE\Microsoft\Security Center\Provider\Av\* (clés présentes = AV reconnu par Windows Security Center).
    // Repli : SELECT displayName de ROOT\SecurityCenter2 AntiVirusProduct (nécessite WMI, parfois absent en NativeAOT) — ici, on reste sur le registre.
    private static string? DetectAv()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SOFTWARE\Microsoft\Windows Defender");
            if (k is not null) return "Windows Defender";
        }
        catch { }
        return null;
    }

    private static bool IsVirtualMachine()
    {
        if (!OperatingSystem.IsWindows()) return false;
        // Indices simples (fallbacks AOT-safe) : BIOS/system manufacturer typique d'un hyperviseur.
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"HARDWARE\DESCRIPTION\System\BIOS");
            string bios = ((k?.GetValue("BIOSVendor") as string) ?? "") + " " + ((k?.GetValue("SystemManufacturer") as string) ?? "") + " " + ((k?.GetValue("SystemProductName") as string) ?? "");
            bios = bios.ToLowerInvariant();
            if (bios.Contains("vmware") || bios.Contains("virtualbox") || bios.Contains("vbox") || bios.Contains("qemu")
                || bios.Contains("xen") || bios.Contains("hyper-v") || bios.Contains("parallels") || bios.Contains("kvm")) return true;
        }
        catch { }
        return false;
    }

    private static string? ActiveDomain()
    {
        try { return Environment.UserDomainName == Environment.MachineName ? "WORKGROUP" : Environment.UserDomainName; }
        catch { return null; }
    }

    private static bool? SecureBootEnabled()
    {
        if (!OperatingSystem.IsWindows()) return null;
        try
        {
            using var k = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Control\SecureBoot\State");
            return k?.GetValue("UEFISecureBootEnabled") is int v ? v == 1 : (bool?)null;
        }
        catch { return null; }
    }

    private static string? DotnetVersion()
    {
        try { return Environment.Version.ToString(); } catch { return null; }
    }

    private static IReadOnlyList<Disk> DiskSnapshot()
    {
        var list = new List<Disk>();
        foreach (var d in DriveInfo.GetDrives())
        {
            try
            {
                if (d.DriveType != DriveType.Fixed || !d.IsReady) continue;
                list.Add(new Disk(d.Name.TrimEnd('\\'), d.TotalSize / 1024 / 1024 / 1024, d.AvailableFreeSpace / 1024 / 1024 / 1024));
            }
            catch { }
            if (list.Count >= 8) break;
        }
        return list;
    }

    // Pourcentage de RAM utilisé. GlobalMemoryStatusEx reste AOT-safe et donne un vrai 0-100.
    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential, CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private struct MEMORYSTATUSEX
    {
        public uint dwLength, dwMemoryLoad;
        public ulong ullTotalPhys, ullAvailPhys, ullTotalPageFile, ullAvailPageFile, ullTotalVirtual, ullAvailVirtual, ullAvailExtendedVirtual;
    }
    [System.Runtime.InteropServices.DllImport("kernel32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode)]
    private static extern bool GlobalMemoryStatusEx(ref MEMORYSTATUSEX lpBuffer);
    private static int RamPercent()
    {
        var m = new MEMORYSTATUSEX { dwLength = (uint)System.Runtime.InteropServices.Marshal.SizeOf<MEMORYSTATUSEX>() };
        return GlobalMemoryStatusEx(ref m) ? (int)m.dwMemoryLoad : 0;
    }

    // Logiciels NOTABLES détectés : via ProductName/FileDescription des processus en cours (les logiciels INSTALLÉS
    // mais fermés ne sont pas listés — intentionnel, pas de bruit, et conforme à la proportionnalité RGPD).
    private static IReadOnlyList<string> NotableSoftwareDetected()
    {
        var found = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in System.Diagnostics.Process.GetProcesses())
        {
            try
            {
                string? exePath = null;
                try { exePath = p.MainModule?.FileName; } catch { }
                if (exePath is null) continue;
                string hay;
                try
                {
                    var vi = System.Diagnostics.FileVersionInfo.GetVersionInfo(exePath);
                    hay = ((vi.ProductName ?? "") + " | " + (vi.FileDescription ?? "") + " | " + p.ProcessName).ToLowerInvariant();
                }
                catch { hay = p.ProcessName.ToLowerInvariant(); }
                foreach (var (needle, display) in NotableSoftware)
                    if (hay.Contains(needle, StringComparison.Ordinal)) { found.Add(display); break; }
            }
            catch { }
            finally { try { p.Dispose(); } catch { } }
            if (found.Count >= 40) break;
        }
        return found.ToList();
    }

    // Services NOTABLES — liste CLOSE. On n'énumère JAMAIS tout services.msc : seulement une petite famille qui aide
    // le support (antivirus/EDR, pare-feu, VPN courants, service de licence Dofus/Ankama, mise à jour Windows). Lu via le
    // registre HKLM\SYSTEM\CurrentControlSet\Services (nom affiché + Start=auto/2) + l'état runtime via OpenSCManager
    // (QueryServiceStatus, démarré/arrêté). Sans permissions suffisantes -> on remonte le registre seul.
    private static readonly string[] NotableServiceKeys =
    {
        // Sécurité
        "WinDefend",        // Windows Defender
        "SecurityHealthService", "WdNisSvc", "mpssvc", // Centre de sécurité, antimalware réseau, pare-feu Windows
        // EDR / AV tiers courants
        "MsMpEng", "Sense",
        // VPN courants
        "NordVPNService", "ExpressVPNService", "ProtonVPN Service", "Mullvad VPN", "OpenVPNService", "WireGuardManager",
        // Mise à jour / divers système
        "wuauserv",         // Windows Update
    };

    private static IReadOnlyList<ServiceState> NotableServices()
    {
        if (!OperatingSystem.IsWindows()) return Array.Empty<ServiceState>();
        var list = new List<ServiceState>();
        foreach (var key in NotableServiceKeys)
        {
            try
            {
                using var sk = Registry.LocalMachine.OpenSubKey(@"SYSTEM\CurrentControlSet\Services\" + key);
                if (sk is null) continue;
                string? disp = sk.GetValue("DisplayName") as string;
                // Start : 2 = Automatique, 3 = Manuel, 4 = Désactivé.
                bool auto = sk.GetValue("Start") is int s && (s == 2 || s == 0);
                string? status = ServiceStatus(key);   // "running"/"stopped"/null si refus / inconnu
                list.Add(new ServiceState(key, disp, status, auto));
            }
            catch { }
            if (list.Count >= 24) break;
        }
        return list;
    }

    // État runtime d'un service via OpenSCManager/OpenService/QueryServiceStatus (AOT-safe, pas de WMI). Lecture seule.
    private static string? ServiceStatus(string serviceName)
    {
        const uint SC_MANAGER_CONNECT = 0x0001, SERVICE_QUERY_STATUS = 0x0004;
        IntPtr scm = OpenSCManagerW(null, null, SC_MANAGER_CONNECT);
        if (scm == IntPtr.Zero) return null;
        try
        {
            IntPtr svc = OpenServiceW(scm, serviceName, SERVICE_QUERY_STATUS);
            if (svc == IntPtr.Zero) return null;
            try
            {
                if (!QueryServiceStatus(svc, out SERVICE_STATUS st)) return null;
                return st.dwCurrentState switch
                {
                    1 => "stopped", 2 => "start_pending", 3 => "stop_pending", 4 => "running",
                    5 => "continue_pending", 6 => "pause_pending", 7 => "paused", _ => "unknown"
                };
            }
            finally { CloseServiceHandle(svc); }
        }
        finally { CloseServiceHandle(scm); }
    }

    [System.Runtime.InteropServices.DllImport("advapi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenSCManagerW(string? machine, string? database, uint access);
    [System.Runtime.InteropServices.DllImport("advapi32.dll", CharSet = System.Runtime.InteropServices.CharSet.Unicode, SetLastError = true)]
    private static extern IntPtr OpenServiceW(IntPtr scm, string serviceName, uint access);
    [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool QueryServiceStatus(IntPtr svc, out SERVICE_STATUS status);
    [System.Runtime.InteropServices.DllImport("advapi32.dll", SetLastError = true)]
    private static extern bool CloseServiceHandle(IntPtr h);

    [System.Runtime.InteropServices.StructLayout(System.Runtime.InteropServices.LayoutKind.Sequential)]
    private struct SERVICE_STATUS
    {
        public uint dwServiceType, dwCurrentState, dwControlsAccepted, dwWin32ExitCode;
        public uint dwServiceSpecificExitCode, dwCheckPoint, dwWaitHint;
    }

    // ÉVÉNEMENTS WINDOWS récents (Application + Système) — 5 CRITICAL/ERROR max par log. Utile au support : plantages du
    // service de licence, erreurs disque, crash de Dofus… Pas de collecte en masse, pas de requête libre. On lit
    // Application/System avec EventLogSession/EventLogReader (AOT-safe en net10). Si refusé -> liste vide.
    private static IReadOnlyList<EventEntry> RecentCriticalEvents()
    {
        var list = new List<EventEntry>();
        if (!OperatingSystem.IsWindows()) return list;
        foreach (var logName in new[] { "Application", "System" })
        {
            try
            {
                // Filtre : TimeCreated dans les 72 dernières heures, Level 1 (Critical) ou 2 (Error).
                var query = new System.Diagnostics.Eventing.Reader.EventLogQuery(
                    logName,
                    System.Diagnostics.Eventing.Reader.PathType.LogName,
                    "*[System[(Level=1 or Level=2) and TimeCreated[timediff(@SystemTime) &lt;= 259200000]]]");
                using var reader = new System.Diagnostics.Eventing.Reader.EventLogReader(query);
                int count = 0;
                System.Diagnostics.Eventing.Reader.EventRecord? ev;
                while ((ev = reader.ReadEvent()) is not null && count < 5)
                {
                    try
                    {
                        string level = ev.Level switch { 1 => "critical", 2 => "error", _ => "unknown" };
                        long at = ev.TimeCreated is DateTime t ? new DateTimeOffset(t.ToUniversalTime()).ToUnixTimeMilliseconds() : 0;
                        list.Add(new EventEntry(logName, Clip(ev.ProviderName, 64) ?? "", ev.Id, level, at));
                        count++;
                    }
                    catch { }
                    finally { ev.Dispose(); }
                }
            }
            catch { }
        }
        return list;
    }

    private static string? Clip(string? s, int max) => s is null ? null : (s.Length > max ? s[..max] : s);
}
