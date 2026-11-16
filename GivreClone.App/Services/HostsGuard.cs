using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;

namespace GivreClone.App.Services;

/// <summary>Détecte (lecture seule, ne modifie JAMAIS le fichier) une redirection ACTIVE d'un serveur Ankama vers la machine locale dans le fichier hosts : sans programme à l'écoute, le client Dofus
/// affiche « Le serveur ne répond pas » et les bots Socket résolvent le serveur en 127.0.0.1 puis échouent.</summary>
public static class HostsGuard
{
    /// <summary>Lignes actives (non commentées) qui envoient un nom Ankama vers 127.x / ::1 / 0.0.0.0.</summary>
    public static IReadOnlyList<string> ActiveRedirects(IEnumerable<string> lines)
    {
        var res = new List<string>();
        foreach (var raw in lines)
        {
            string line = raw.Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            int hash = line.IndexOf('#'); if (hash >= 0) line = line[..hash].Trim();
            var parts = line.Split(new[] { ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries);
            if (parts.Length < 2) continue;
            string ip = parts[0];
            bool loopback = ip.StartsWith("127.", StringComparison.Ordinal) || ip == "::1" || ip == "0.0.0.0";
            if (!loopback) continue;
            if (parts.Skip(1).Any(h => h.EndsWith("ankama-games.com", StringComparison.OrdinalIgnoreCase) || h.EndsWith("ankama.com", StringComparison.OrdinalIgnoreCase) || h.EndsWith("dofus.com", StringComparison.OrdinalIgnoreCase)))
                res.Add(string.Join(" ", parts));
        }
        return res;
    }

    /// <summary>Lit le fichier hosts de la machine ; liste vide si illisible.</summary>
    public static IReadOnlyList<string> ActiveRedirectsOnThisMachine()
    {
        try { return ActiveRedirects(File.ReadAllLines(Path.Combine(Environment.SystemDirectory, "drivers", "etc", "hosts"))); }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException) { return Array.Empty<string>(); }
    }
}
