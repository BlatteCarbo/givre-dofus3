using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;

namespace GivreClone.App.Services.Combat;

/// <summary>Script Lua de farm de capture d'âmes : n'attaque que les groupes qui contiennent un des archimonstres visés (FORCE_MONSTERS), joue les tours avec l'IA de combat
/// intégrée et passe par soul.tryCapture() avant chaque tour (pierre équipée, sort relancé, capture seulement si la fin du combat est sûre pendant l'état). Pur.</summary>
public static class SoulScript
{
    public static string Build(IReadOnlyList<(int Id, string Name)> monsters, int maxPodsPercent = 90)
    {
        if (monsters.Count == 0) throw new ArgumentException("aucun monstre visé");
        static string Clean(string s) => s.Replace("\r", " ").Replace("\n", " ");
        var sb = new StringBuilder();
        sb.Append("-- Capture d'âmes — script généré par Givre.\n");
        sb.Append("-- N'attaque que les groupes contenant un des archimonstres listés ; la capture n'est lancée que si une pierre d'âme assez puissante est équipée,\n");
        sb.Append("-- que le sort est relancé et que la fin du combat est SÛRE pendant les 2 tours de l'état (dégâts mesurés pendant le combat, marge de 25 %).\n");
        sb.Append("-- ATTENTION : la pierre d'âme prend l'emplacement de l'arme. Garde des pierres de la bonne taille dans le sac.\n\n");
        sb.Append("MAX_PODS = ").Append(Math.Clamp(maxPodsPercent, 10, 100)).Append("\n");
        sb.Append("FORCE_MONSTERS = { ").Append(string.Join(", ", monsters.Select(m => m.Id))).Append(" }   -- ").Append(Clean(string.Join(", ", monsters.Select(m => m.Name)))).Append("\n\n");
        sb.Append("function onTurn()\n");
        sb.Append("  soul.observe()   -- mesure les dégâts par tour de l'équipe\n");
        sb.Append("  local action, reason = soul.tryCapture({ capturable = true, window = 2, margin = 1.25 })\n");
        sb.Append("  if action == \"equipped\" or action == \"cast\" then print(\"[capture] \" .. action .. \" : \" .. reason) end\n");
        sb.Append("  combat:playTurn()   -- le reste du tour : IA de combat du profil\n");
        sb.Append("end\n\n");
        sb.Append("function move()\n");
        sb.Append("  map:forceFight()   -- attend un groupe éligible sur la carte, puis l'attaque\n");
        sb.Append("  return true\n");
        sb.Append("end\n");
        return sb.ToString();
    }
}
