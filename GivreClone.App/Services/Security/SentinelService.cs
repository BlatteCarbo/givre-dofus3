using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Runtime.InteropServices;

namespace GivreClone.App.Services.Security;

/// <summary>
/// « Givre Sentinel » : collecte des SIGNAUX sur NOTRE propre processus (débogueur attaché, points d'arrêt matériels,
/// profileur .NET, outils d'analyse ouverts) et les remonte au serveur via le canal signé "sentinel". Fonctionne comme
/// la partie poste d'un outil RMM/UEM/MDM : télémétrie étendue autorisée par CGU (cadre détaillé dans
/// <c>docs/web-sentinel-rmm.md</c>) — on peut donc remonter le chemin disque, le PID, le titre de fenêtre et l'état de
/// signature Authenticode d'une détection, pour aider le support à qualifier une alerte.
///
/// ⚠️ SIGNALEMENT SEUL. Ce service N'AGIT JAMAIS : il ne tue pas, ne ralentit pas, ne « décoy » pas. La décision
/// (suspension, kill, dégradation, prise en main à distance) est prise CÔTÉ SERVEUR après revue humaine (panel admin
/// + bot Discord), puis redescend par une commande signée. Agir automatiquement côté client apprendrait à l'attaquant
/// ce qui l'a trahi et punirait un faux positif.
///
/// Périmètre : UNIQUEMENT notre processus pour les détections sensibles (débogueur, breakpoints). L'énumération des
/// processus/fenêtres sert à repérer les OUTILS D'ANALYSE ouverts ; on ne remonte JAMAIS la liste complète des
/// processus de l'utilisateur, seulement les outils reconnus. AOT-safe (P/Invoke user-mode, pas de crypto virtualisée).
/// </summary>
public static class SentinelService
{
    /// <summary>Un signal détecté : code court stable + sévérité indicative + méta pour qualifier (chemin, PID, titre
    /// de fenêtre, signature Authenticode). Null quand non applicable (ex : debugger managé n'a ni path ni pid).</summary>
    public readonly record struct Signal(string Code, int Severity, string? Path = null, int? Pid = null, string? Window = null, bool? Signed = null);

    // Outils d'analyse — listes COURTES. On ne remonte QUE le nom de l'outil reconnu + sa méta. Classés par gravité :
    // un débogueur/décompilateur OUVERT pendant que le bot tourne est un signal FORT même non attaché.
    private const int SevReTool = 85, SevReToolRenamed = 95, SevNetTool = 50, SevSysTool = 30;

    private static readonly string[] ReTools =
    {
        "x64dbg", "x32dbg", "x96dbg", "ollydbg", "windbg", "windbgx", "dbgx.shell", "ida", "ida64", "ida32", "idag", "idaq", "idat",
        "ghidra", "ghidrarun", "dnspy", "dnspyex", "ilspy", "dotpeek", "de4dot", "reclass", "reclass.net", "binaryninja",
        "megadumper", "extremedumper", "pe-sieve", "frida", "frida-server", "petools", "lordpe", "importrec", "titanhide",
    };
    private static readonly string[] ReToolPrefixes = { "cheatengine", "scylla", "x64dbg", "x32dbg", "ida64", "dnspy", "reclass" };
    private static readonly string[] NetTools = { "httptoolkit", "mitmproxy", "mitmdump", "mitmweb", "fiddler", "fiddler everywhere", "charles", "wireshark", "tshark" };
    private static readonly string[] SysTools = { "processhacker", "systeminformer" };

    private static readonly (string Token, string Tool)[] WindowTokens =
    {
        ("x64dbg", "x64dbg"), ("x32dbg", "x32dbg"), ("ollydbg", "ollydbg"), ("windbg", "windbg"), ("ida pro", "ida"),
        ("ghidra", "ghidra"), ("dnspy", "dnspy"), ("ilspy", "ilspy"), ("dotpeek", "dotpeek"), ("cheat engine", "cheatengine"),
        ("reclass", "reclass"), ("scylla", "scylla"), ("binary ninja", "binaryninja"), ("extremedumper", "extremedumper"),
    };
    private static readonly HashSet<string> TitleIgnoredProcesses = new(StringComparer.OrdinalIgnoreCase)
    {
        "chrome", "msedge", "firefox", "opera", "opera_gx", "brave", "vivaldi", "iexplore", "discord", "slack", "teams", "ms-teams",
        "telegram", "whatsapp", "code", "devenv", "rider64", "notepad", "notepad++", "explorer", "obs64", "spotify", "givrebot",
    };

    private static readonly (string Token, string Tool)[] VersionTokens =
    {
        ("x64dbg", "x64dbg"), ("x32dbg", "x32dbg"), ("ollydbg", "ollydbg"), ("interactive disassembler", "ida"), ("ida pro", "ida"),
        ("dnspy", "dnspy"), ("ilspy", "ilspy"), ("dotpeek", "dotpeek"), ("de4dot", "de4dot"), ("cheat engine", "cheatengine"),
        ("reclass", "reclass"), ("scylla", "scylla"), ("binary ninja", "binaryninja"), ("extremedumper", "extremedumper"),
        ("megadumper", "megadumper"), ("pe-sieve", "pe-sieve"), ("titanhide", "titanhide"),
    };

    // Caches : lecture d'infos de version (lente) + état « signé » Authenticode faite UNE SEULE FOIS par chemin.
    private static readonly Dictionary<string, string> _versionToolCache = new(StringComparer.OrdinalIgnoreCase);
    private static readonly Dictionary<string, bool> _signedCache = new(StringComparer.OrdinalIgnoreCase);

    /// <summary>Scanne NOTRE processus et renvoie les signaux détectés (vide = rien d'anormal). Best-effort, non bloquant.</summary>
    public static IReadOnlyList<Signal> Scan()
    {
        var sigs = new List<Signal>();
        var seen = new HashSet<string>();
        void Add(Signal s) { if (seen.Add(s.Code)) sigs.Add(s); }

        // 1) Débogueur managé attaché (CLR).
        try { if (Debugger.IsAttached) Add(new Signal("managed_debugger", 100)); } catch { }
        try { if (Debugger.IsLogging()) Add(new Signal("managed_debug_logging", 40)); } catch { }

        // 2) Débogueur natif (user-mode, NOTRE processus).
        try { if (IsDebuggerPresent()) Add(new Signal("native_debugger", 100)); } catch { }
        try { if (CheckRemoteDebuggerPresent(GetCurrentProcess(), out bool rem) && rem) Add(new Signal("remote_debugger", 100)); } catch { }

        // 3) Port de débogage via NtQueryInformationProcess (ProcessDebugPort=7) : non nul = un débogueur est attaché.
        try
        {
            if (NtQueryInformationProcess(GetCurrentProcess(), 7 /*ProcessDebugPort*/, out IntPtr port, IntPtr.Size, IntPtr.Zero) == 0
                && port != IntPtr.Zero) Add(new Signal("debug_port", 90));
        }
        catch { }

        // 4) Points d'arrêt MATÉRIELS : registres de débogage Dr0-Dr3/Dr7 non nuls sur l'un de NOS threads.
        try { if (HasHardwareBreakpoints()) Add(new Signal("hardware_breakpoints", 90)); } catch { }

        // 5) Profileur .NET activé par variable d'environnement = vecteur classique d'instrumentation/d'analyse managée.
        try
        {
            foreach (var v in new[] { "COR_ENABLE_PROFILING", "CORECLR_ENABLE_PROFILING" })
                if (Environment.GetEnvironmentVariable(v) is "1") { Add(new Signal("clr_profiler", 70)); break; }
            foreach (var v in new[] { "COR_PROFILER", "CORECLR_PROFILER" })
                if (!string.IsNullOrEmpty(Environment.GetEnvironmentVariable(v))) { Add(new Signal("clr_profiler", 70)); break; }
        }
        catch { }

        // 6) Outils d'analyse OUVERTS. Trois sources : (a) nom de processus, (b) infos de version (attrape un exe
        //    renommé), (c) titres de fenêtres visibles. Chaque signal porte chemin/PID/signé pour que le support
        //    identifie clairement de quel exe il s'agit (cas RMM demandé par l'owner).
        try
        {
            // Instantané pid -> (nom de processus, chemin). Un seul passage pour tout le reste.
            var procs = new Dictionary<uint, (string Name, string? Path)>();
            foreach (var p in Process.GetProcesses())
            {
                try { procs[(uint)p.Id] = (p.ProcessName.ToLowerInvariant(), ImagePath((uint)p.Id)); }
                catch { } finally { try { p.Dispose(); } catch { } }
            }

            // Suivi des outils déjà rapportés par leur nom de processus (évite d'annoncer un même outil 2 fois sous
            // deux codes différents si on le reconnaît ensuite par la version info ou le titre de fenêtre).
            var byProcess = new HashSet<string>();

            // (a) Par nom de processus.
            foreach (var (pid, info) in procs)
            {
                string tool;
                int sev;
                if (ReTools.Contains(info.Name) || ReToolPrefixes.Any(info.Name.StartsWith)) { tool = Canonical(info.Name); sev = SevReTool; }
                else if (NetTools.Contains(info.Name)) { tool = info.Name; sev = SevNetTool; }
                else if (SysTools.Contains(info.Name)) { tool = info.Name; sev = SevSysTool; }
                else continue;
                byProcess.Add(tool);
                Add(new Signal("analysis_tool:" + tool, sev, info.Path, (int)pid, Signed: IsAuthenticodeSigned(info.Path)));
            }

            // (b) Par infos de version (ProductName / FileDescription). Un x64dbg renommé en notepad2.exe se trahit ici.
            foreach (var (pid, info) in procs)
            {
                if (TitleIgnoredProcesses.Contains(info.Name)) continue;
                if (info.Path is null) continue;
                var vtool = ToolFromVersionInfo(info.Path);
                if (vtool is null || byProcess.Contains(vtool)) continue;
                byProcess.Add(vtool);
                bool renamed = !info.Name.StartsWith(vtool, StringComparison.Ordinal);
                string code = "analysis_tool:" + vtool + (renamed ? ":renamed" : "");
                Add(new Signal(code, renamed ? SevReToolRenamed : SevReTool, info.Path, (int)pid, Signed: IsAuthenticodeSigned(info.Path)));
            }

            // (c) Par titre de fenêtre (Ghidra hébergé par java.exe, outils renommés dont la fenêtre garde son nom).
            foreach (var (pid, title) in VisibleWindowTitles())
            {
                if (!procs.TryGetValue(pid, out var info) || TitleIgnoredProcesses.Contains(info.Name)) continue;
                foreach (var (token, tool) in WindowTokens)
                {
                    if (title.IndexOf(token, StringComparison.OrdinalIgnoreCase) < 0) continue;
                    if (byProcess.Contains(tool)) break;
                    bool renamed = !info.Name.StartsWith(tool, StringComparison.Ordinal) && info.Name is not ("java" or "javaw" or "python" or "pythonw");
                    string code = "analysis_tool:" + tool + (renamed ? ":renamed" : "");
                    byProcess.Add(tool);
                    Add(new Signal(code, renamed ? SevReToolRenamed : SevReTool, info.Path, (int)pid, Clip(title, 120), IsAuthenticodeSigned(info.Path)));
                    break;
                }
            }
        }
        catch { }

        return sigs;
    }

    private static string? Clip(string? s, int max) => s is null ? null : (s.Length > max ? s[..max] : s);

    private static string? ToolFromVersionInfo(string path)
    {
        lock (_versionToolCache)
            if (_versionToolCache.TryGetValue(path, out var cached)) return cached.Length == 0 ? null : cached;
        string found = "";
        try
        {
            var vi = FileVersionInfo.GetVersionInfo(path);
            string hay = ((vi.ProductName ?? "") + " | " + (vi.FileDescription ?? "")).ToLowerInvariant();
            foreach (var (token, tool) in VersionTokens)
                if (hay.Contains(token, StringComparison.Ordinal)) { found = tool; break; }
        }
        catch { }
        lock (_versionToolCache) { if (_versionToolCache.Count > 4096) _versionToolCache.Clear(); _versionToolCache[path] = found; }
        return found.Length == 0 ? null : found;
    }

    // État Authenticode d'un exe (true = signature VALIDE). Null si le chemin est inconnu / lecture refusée.
    // L'info sert au serveur à distinguer un outil officiel (ex x64dbg signé) d'un exe renommé/trafiqué (très suspect).
    private static bool? IsAuthenticodeSigned(string? path)
    {
        if (string.IsNullOrEmpty(path)) return null;
        lock (_signedCache) if (_signedCache.TryGetValue(path, out var cached)) return cached;
        bool ok = false;
        try
        {
            var info = new WINTRUST_FILE_INFO { cbStruct = (uint)Marshal.SizeOf<WINTRUST_FILE_INFO>(), pcwszFilePath = path };
            IntPtr fileInfo = Marshal.AllocHGlobal((int)info.cbStruct);
            try
            {
                Marshal.StructureToPtr(info, fileInfo, false);
                var data = new WINTRUST_DATA
                {
                    cbStruct = (uint)Marshal.SizeOf<WINTRUST_DATA>(), dwUIChoice = 2 /*WTD_UI_NONE*/,
                    fdwRevocationChecks = 0 /*WTD_REVOKE_NONE*/, dwUnionChoice = 1 /*WTD_CHOICE_FILE*/,
                    pFile = fileInfo, dwStateAction = 0, dwProvFlags = 0x00000020 /*WTD_CACHE_ONLY_URL_RETRIEVAL*/ | 0x00000100 /*WTD_SAFER_FLAG*/,
                };
                var policy = new Guid("00AAC56B-CD44-11D0-8CC2-00C04FC295EE");  // WINTRUST_ACTION_GENERIC_VERIFY_V2
                int res = WinVerifyTrust(IntPtr.Zero, ref policy, ref data);
                ok = res == 0;
            }
            finally { Marshal.FreeHGlobal(fileInfo); }
        }
        catch { }
        lock (_signedCache) { if (_signedCache.Count > 4096) _signedCache.Clear(); _signedCache[path] = ok; }
        return ok;
    }

    // Chemin de l'exe d'un processus (accès « informations limitées » : marche aussi sur un processus élevé). null si refusé.
    private static string? ImagePath(uint pid)
    {
        IntPtr h = OpenProcess(0x1000 /*PROCESS_QUERY_LIMITED_INFORMATION*/, false, pid);
        if (h == IntPtr.Zero) return null;
        try
        {
            var buf = new char[1024];
            uint len = (uint)buf.Length;
            return QueryFullProcessImageNameW(h, 0, buf, ref len) && len > 0 ? new string(buf, 0, (int)len) : null;
        }
        catch { return null; }
        finally { CloseHandle(h); }
    }

    private static string Canonical(string processName)
    {
        foreach (var p in new[] { "cheatengine", "scylla", "dnspy", "reclass" })
            if (processName.StartsWith(p, StringComparison.Ordinal)) return p;
        if (processName.StartsWith("x64dbg", StringComparison.Ordinal)) return "x64dbg";
        if (processName.StartsWith("x32dbg", StringComparison.Ordinal)) return "x32dbg";
        if (processName.StartsWith("ida", StringComparison.Ordinal)) return "ida";
        return processName;
    }

    private static IEnumerable<(uint Pid, string Title)> VisibleWindowTitles()
    {
        var list = new List<(uint, string)>();
        var buf = new char[256];
        IntPtr h = GetTopWindow(IntPtr.Zero);
        for (int guard = 0; h != IntPtr.Zero && guard < 5000; guard++, h = GetWindow(h, 2 /*GW_HWNDNEXT*/))
        {
            try
            {
                if (!IsWindowVisible(h)) continue;
                int n = GetWindowTextW(h, buf, buf.Length);
                if (n <= 0) continue;
                GetWindowThreadProcessId(h, out uint pid);
                list.Add((pid, new string(buf, 0, n)));
            }
            catch { }
        }
        return list;
    }

    private static bool HasHardwareBreakpoints()
    {
        uint myPid = (uint)Environment.ProcessId, myTid = GetCurrentThreadId();
        IntPtr snap = CreateToolhelp32Snapshot(0x00000004 /*TH32CS_SNAPTHREAD*/, 0);
        if (snap == IntPtr.Zero || snap == new IntPtr(-1)) return false;
        try
        {
            var te = new THREADENTRY32 { dwSize = (uint)Marshal.SizeOf<THREADENTRY32>() };
            if (!Thread32First(snap, ref te)) return false;
            do
            {
                if (te.th32OwnerProcessID != myPid || te.th32ThreadID == myTid) continue;
                IntPtr h = OpenThread(0x0008 /*THREAD_GET_CONTEXT*/ | 0x0040 /*THREAD_QUERY_INFORMATION*/, false, te.th32ThreadID);
                if (h == IntPtr.Zero) continue;
                try
                {
                    var ctx = new CONTEXT64 { ContextFlags = 0x00100010 /*CONTEXT_DEBUG_REGISTERS (amd64)*/ };
                    if (GetThreadContext(h, ref ctx))
                        if (ctx.Dr0 != 0 || ctx.Dr1 != 0 || ctx.Dr2 != 0 || ctx.Dr3 != 0 || (ctx.Dr7 & 0xFF) != 0)
                            return true;
                }
                finally { CloseHandle(h); }
            } while (Thread32Next(snap, ref te));
        }
        catch { }
        finally { CloseHandle(snap); }
        return false;
    }

    [DllImport("kernel32.dll")] private static extern bool IsDebuggerPresent();
    [DllImport("kernel32.dll")] private static extern bool CheckRemoteDebuggerPresent(IntPtr hProcess, out bool isDebuggerPresent);
    [DllImport("kernel32.dll")] private static extern IntPtr GetCurrentProcess();
    [DllImport("kernel32.dll")] private static extern uint GetCurrentThreadId();
    [DllImport("kernel32.dll")] private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll")] private static extern bool Thread32First(IntPtr snap, ref THREADENTRY32 te);
    [DllImport("kernel32.dll")] private static extern bool Thread32Next(IntPtr snap, ref THREADENTRY32 te);
    [DllImport("kernel32.dll")] private static extern IntPtr OpenThread(uint access, bool inherit, uint tid);
    [DllImport("kernel32.dll")] private static extern bool GetThreadContext(IntPtr hThread, ref CONTEXT64 ctx);
    [DllImport("kernel32.dll")] private static extern bool CloseHandle(IntPtr h);
    [DllImport("ntdll.dll")] private static extern int NtQueryInformationProcess(IntPtr h, int cls, out IntPtr info, int len, IntPtr retLen);
    [DllImport("kernel32.dll")] private static extern IntPtr OpenProcess(uint access, bool inherit, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)] private static extern bool QueryFullProcessImageNameW(IntPtr h, uint flags, [Out] char[] buf, ref uint size);
    [DllImport("user32.dll")] private static extern IntPtr GetTopWindow(IntPtr hWnd);
    [DllImport("user32.dll")] private static extern IntPtr GetWindow(IntPtr hWnd, uint cmd);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hWnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(IntPtr hWnd, [Out] char[] text, int max);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint pid);
    [DllImport("wintrust.dll", SetLastError = true)] private static extern int WinVerifyTrust(IntPtr hwnd, ref Guid pgActionID, ref WINTRUST_DATA pWVTData);

    [StructLayout(LayoutKind.Sequential)]
    private struct THREADENTRY32
    {
        public uint dwSize, cntUsage, th32ThreadID, th32OwnerProcessID;
        public int tpBasePri, tpDeltaPri; public uint dwFlags;
    }

    [StructLayout(LayoutKind.Explicit, Size = 1232)]
    private struct CONTEXT64
    {
        [FieldOffset(0x30)] public uint ContextFlags;
        [FieldOffset(0x48)] public ulong Dr0;
        [FieldOffset(0x50)] public ulong Dr1;
        [FieldOffset(0x58)] public ulong Dr2;
        [FieldOffset(0x60)] public ulong Dr3;
        [FieldOffset(0x68)] public ulong Dr6;
        [FieldOffset(0x70)] public ulong Dr7;
    }

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct WINTRUST_FILE_INFO
    {
        public uint cbStruct; [MarshalAs(UnmanagedType.LPWStr)] public string pcwszFilePath;
        public IntPtr hFile; public IntPtr pgKnownSubject;
    }

    [StructLayout(LayoutKind.Sequential)]
    private struct WINTRUST_DATA
    {
        public uint cbStruct;
        public IntPtr pPolicyCallbackData;
        public IntPtr pSIPClientData;
        public uint dwUIChoice;
        public uint fdwRevocationChecks;
        public uint dwUnionChoice;
        public IntPtr pFile;
        public uint dwStateAction;
        public IntPtr hWVTStateData;
        public IntPtr pwszURLReference;
        public uint dwProvFlags;
        public uint dwUIContext;
    }
}
