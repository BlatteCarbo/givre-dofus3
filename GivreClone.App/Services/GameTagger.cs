using System;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GivreClone.App.Services;

/// <summary>
/// Ajoute «  [IP: x.x.x.x]  » au titre de la fenetre du jeu pour confirmer visuellement, d'un coup d'oeil,
/// l'IP de sortie du client (proxy assigne, ou IP reelle sans proxy). Reapplique un moment car le client
/// Unity fixe/reecrit son titre tard pendant le chargement.
/// </summary>
public static class GameTagger
{
    private delegate bool EnumWindowsProc(IntPtr hwnd, IntPtr lParam);

    [DllImport("user32.dll")] private static extern bool EnumWindows(EnumWindowsProc cb, IntPtr lParam);
    [DllImport("user32.dll")] private static extern uint GetWindowThreadProcessId(IntPtr hwnd, out uint pid);
    [DllImport("user32.dll")] private static extern bool IsWindowVisible(IntPtr hwnd);
    [DllImport("user32.dll")] private static extern int GetWindowTextLengthW(IntPtr hwnd);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern int GetWindowTextW(IntPtr hwnd, StringBuilder s, int max);
    [DllImport("user32.dll", CharSet = CharSet.Unicode)] private static extern bool SetWindowTextW(IntPtr hwnd, string s);

    private const string Marker = "  [IP:";

    /// <summary>Trouve la fenetre principale du PID, y appose le tag IP, et le reapplique ~90 s.</summary>
    public static async Task TagAsync(int pid, string ip, CancellationToken ct = default)
    {
        var tag = $"{Marker} {ip}]";
        for (var i = 0; i < 45 && !ct.IsCancellationRequested; i++)
        {
            var hwnd = FindMainWindow((uint)pid);
            if (hwnd != IntPtr.Zero)
            {
                var title = GetTitle(hwnd);
                if (!title.Contains(Marker, StringComparison.Ordinal))   // le jeu l'a (re)ecrit -> on reappose
                    SetWindowTextW(hwnd, StripTag(title) + tag);
            }
            try { await Task.Delay(2000, ct).ConfigureAwait(false); } catch { break; }
        }
    }

    public static IntPtr FindMainWindowForOverlay(uint pid) => FindMainWindow(pid);

    private static IntPtr FindMainWindow(uint pid)
    {
        var found = IntPtr.Zero;
        EnumWindows((hwnd, _) =>
        {
            if (GetWindowThreadProcessId(hwnd, out var wpid) != 0 && wpid == pid
                && IsWindowVisible(hwnd) && GetWindowTextLengthW(hwnd) > 0)
            {
                found = hwnd;
                return false;   // stop l'enumeration
            }
            return true;
        }, IntPtr.Zero);
        return found;
    }

    private static string GetTitle(IntPtr hwnd)
    {
        var len = GetWindowTextLengthW(hwnd);
        var sb = new StringBuilder(len + 2);
        GetWindowTextW(hwnd, sb, sb.Capacity);
        return sb.ToString();
    }

    private static string StripTag(string title)
    {
        var idx = title.IndexOf(Marker, StringComparison.Ordinal);
        return idx >= 0 ? title[..idx] : title;
    }
}
