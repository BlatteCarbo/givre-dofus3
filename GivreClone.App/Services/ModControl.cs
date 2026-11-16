using System;
using System.Globalization;
using System.IO;

namespace GivreClone.App.Services;

/// <summary>
/// Pilote les MODS NATIFS de GivreMods.dll (une DLL injectee par client) via un fichier de controle par PID, lu en
/// continu (~500 ms) par la DLL : %LOCALAPPDATA%\GivreLauncher\mods-&lt;PID&gt;.ini (skipper=0/1, tactical=0/1). MEME chemin
/// que GivreLauncher -> la DLL est partagee, les mods s'activent/desactivent a chaud par client, sans relancer.
/// - skipper  = Combat Animation Skipper (no-anim : termine instantanement les animations de combat non bouclees).
/// - tactical = mode tactique natif (masque le decor).
/// </summary>
public static class ModControl
{
    // Historic distribution uses GivreLauncher. The independent clone must
    // write only inside its private run and never leave a control file that
    // a concurrently running original installation could consume.
    private static readonly string Dir =
        MZP25NativeModPaths.Controls(MZP19LocalClone.Enabled, MZP19LocalClone.DataRoot,
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));

    private static string FileFor(int pid) =>
        Path.Combine(Dir, "mods-" + pid.ToString(CultureInfo.InvariantCulture) + ".ini");

    /// <summary>Ecrit l'etat des mods pour un client precis (best-effort). La DLL le relit toutes les ~500 ms.</summary>
    public static void WriteForPid(int pid, bool noAnim, bool tactical, bool collapse = false, bool instantMove = false)
    {
        // This file is in a historically shared GivreLauncher directory.
        // An arbitrary PID from a stale UI binding must never create/change
        // a mod command for another owner's live game.
        if (!MZP25NativeModOwnership.MayControl(pid)) return;
        try
        {
            Directory.CreateDirectory(Dir);
            File.WriteAllText(FileFor(pid),
                $"skipper={(noAnim ? 1 : 0)}\r\ntactical={(tactical ? 1 : 0)}\r\n" +
                $"collapse={(collapse ? 1 : 0)}\r\ninstantmove={(instantMove ? 1 : 0)}\r\n");
        }
        catch { /* best-effort */ }
    }

    /// <summary>Supprime le fichier de controle d'un client ferme (nettoyage).</summary>
    public static void RemoveForPid(int pid)
    {
        try { if (MZP25NativeModOwnership.MayControl(pid)) File.Delete(FileFor(pid)); }
        catch { /* best-effort */ }
    }
}
