using System;
using System.Runtime.InteropServices;
using System.Runtime.Intrinsics.X86;
using System.Runtime.Versioning;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Win32;

namespace GivreClone.App.Services.License;

/// <summary>
/// Empreinte matérielle envoyée au serveur Ikuware. PORTÉE BYTE-FOR-BYTE de l'algo du launcher/DLL Ikuware
/// (PyxLauncher <c>Auth.cpp::ComputeHwid</c> = <c>Pyx_IkuwareClient::ComputeHwid</c>) :
///   <c>sha256_hex( MachineGuid | serieVolume(C:) | brandCPUID | "IkU-hwid-v1-2026" )</c>  (séparateur '|', sel APPENDÉ).
///
/// SYNCHRO OBLIGATOIRE : GivreClone.App + GivreKama.dll + GivreMods.dll s'authentifient TOUS à l'API -> ils DOIVENT
/// produire le MÊME HWID (même algo que l'écosystème Ikuware), sinon HWID_MISMATCH entre composants. AOT-safe
/// (registre, P/Invoke, intrinsèque CPUID). Changer le sel/ordre/sources casserait TOUS les bindings existants.
/// </summary>
public static class Hwid
{
    private const string Salt = "IkU-hwid-v1-2026";   // = launcher/DLL. NE PAS changer.

    private static string? _cached;

    public static string Compute()
    {
        if (_cached is not null) return _cached;
        // Ordre EXACT du launcher : MachineGuid | serieVolume(C:) | brandCPUID | sel. Parts ASCII/UTF-8 -> UTF8.GetBytes
        // reproduit les octets hachés par le C++ (BcSha256 sur material.data()).
        var material = MachineGuid() + "|" + SystemDriveSerial() + "|" + CpuBrand() + "|" + Salt;
        _cached = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(material))).ToLowerInvariant();
        return _cached;
    }

    // MachineGuid : HKLM\SOFTWARE\Microsoft\Cryptography, vue 64-bit (comme GetMachineGuid du launcher, KEY_WOW64_64KEY).
    [SupportedOSPlatform("windows")]
    private static string MachineGuid()
    {
        try
        {
            using var hklm = RegistryKey.OpenBaseKey(RegistryHive.LocalMachine, RegistryView.Registry64);
            using var key = hklm.OpenSubKey(@"SOFTWARE\Microsoft\Cryptography");
            return key?.GetValue("MachineGuid") as string ?? "";
        }
        catch { return ""; }
    }

    [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
    private static extern bool GetVolumeInformationW(
        string rootPathName, char[]? volumeNameBuffer, int volumeNameSize,
        out uint volumeSerialNumber, out uint maximumComponentLength, out uint fileSystemFlags,
        char[]? fileSystemNameBuffer, int fileSystemNameSize);

    // Série du volume C:\ FIXE (comme GetSystemDriveSerial du launcher), format %08lX = hex MAJUSCULE 8 ("X8").
    private static string SystemDriveSerial()
    {
        try
        {
            return GetVolumeInformationW(@"C:\", null, 0, out var serial, out _, out _, null, 0)
                ? serial.ToString("X8")
                : "";
        }
        catch { return ""; }
    }

    // Marque CPU via CPUID 0x80000002..4 (brand brut jusqu'au 1er octet nul), IDENTIQUE à GetCpuBrand du launcher :
    //   - non supporté -> repli "%08X%08X" de EDX,EAX de CPUID(1) ;
    //   - supporté     -> 48 octets (Eax|Ebx|Ecx|Edx little-endian x3) dans un buffer de 64, std::string s'arrête au 1er nul.
    private static string CpuBrand()
    {
        if (!X86Base.IsSupported) return "";
        var max = X86Base.CpuId(unchecked((int)0x80000000), 0);
        if ((uint)max.Eax < 0x80000004u)
        {
            var one = X86Base.CpuId(1, 0);
            return $"{(uint)one.Edx:X8}{(uint)one.Eax:X8}";
        }
        var brand = new byte[64];
        for (int fn = 0; fn < 3; fn++)
        {
            var r = X86Base.CpuId(unchecked((int)(0x80000002 + fn)), 0);
            int off = fn * 16;
            BitConverter.TryWriteBytes(brand.AsSpan(off), r.Eax);
            BitConverter.TryWriteBytes(brand.AsSpan(off + 4), r.Ebx);
            BitConverter.TryWriteBytes(brand.AsSpan(off + 8), r.Ecx);
            BitConverter.TryWriteBytes(brand.AsSpan(off + 12), r.Edx);
        }
        int nul = Array.IndexOf(brand, (byte)0);
        if (nul < 0) nul = brand.Length;
        return Encoding.Latin1.GetString(brand, 0, nul);   // brand ASCII -> 1 octet = 1 char -> UTF8 identique au C++
    }
}
