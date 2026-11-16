using System;
using System.Runtime.InteropServices;

namespace GivreClone.App.Services;

/// <summary>
/// Verifie qu'un module (DLL) est REELLEMENT charge dans un process cible (snapshot Toolhelp32). Sur x64,
/// GetExitCodeThread(LoadLibraryW) ne renvoie que les 32 bits bas du HMODULE -&gt; peut valoir 0 alors que la DLL
/// est bien chargee. On ne se fie donc PAS a la valeur de retour de l'injecteur : on confirme par la liste des modules.
/// C'est le garde-fou anti-fuite : tant que GivreKama n'est pas confirme charge, le trafic n'est pas redirige.
/// </summary>
public static class NativeModules
{
    public static bool IsLoaded(int pid, string moduleName)
    {
        IntPtr snap = CreateToolhelp32Snapshot(TH32CS_SNAPMODULE | TH32CS_SNAPMODULE32, (uint)pid);
        if (snap == INVALID_HANDLE_VALUE) return false;
        try
        {
            var me = new MODULEENTRY32 { dwSize = (uint)Marshal.SizeOf<MODULEENTRY32>() };
            if (!Module32First(snap, ref me)) return false;
            do
            {
                if (string.Equals(me.szModule, moduleName, StringComparison.OrdinalIgnoreCase)) return true;
            } while (Module32Next(snap, ref me));
            return false;
        }
        finally { CloseHandle(snap); }
    }

    private const uint TH32CS_SNAPMODULE = 0x00000008;
    private const uint TH32CS_SNAPMODULE32 = 0x00000010;
    private static readonly IntPtr INVALID_HANDLE_VALUE = new(-1);

    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Unicode)]
    private struct MODULEENTRY32
    {
        public uint dwSize;
        public uint th32ModuleID;
        public uint th32ProcessID;
        public uint GlblcntUsage;
        public uint ProccntUsage;
        public IntPtr modBaseAddr;
        public uint modBaseSize;
        public IntPtr hModule;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 256)] public string szModule;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 260)] public string szExePath;
    }

    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern IntPtr CreateToolhelp32Snapshot(uint flags, uint pid);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Module32First(IntPtr snap, ref MODULEENTRY32 me);
    [DllImport("kernel32.dll", CharSet = CharSet.Unicode)]
    private static extern bool Module32Next(IntPtr snap, ref MODULEENTRY32 me);
    [DllImport("kernel32.dll", SetLastError = true)]
    private static extern bool CloseHandle(IntPtr h);
}
