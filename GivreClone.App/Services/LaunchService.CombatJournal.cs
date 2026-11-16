using System.Linq;
using GivreLauncher.Core.Transport;

namespace GivreClone.App.Services;

public sealed partial class LaunchService
{
    /// <summary>Ligne de PV au début de chacun de MES tours (alimente le replay de combat) : moi, alliés et ennemis vivants, PV absolus et pourcentage. Ne touche à rien d'autre.</summary>
    private void NoteTurnHp(uint pid, int turnNo)
    {
        try
        {
            var all = FightersDetail(pid);
            if (all.Count == 0) return;
            string Fmt(ScriptFighter f) => f.MaxLife > 0 ? $"{f.Cell}:{f.Life}/{f.MaxLife}" : $"{f.Cell}:{f.HpPercent}%";
            var me = all.FirstOrDefault(f => f.IsSelf);
            var allies = all.Where(f => !f.IsEnemy && !f.IsSelf && !f.IsSummon && f.Alive).Select(Fmt);
            // Pas de filtre Alive : un ennemi encore suivi n'a PAS reçu son annonce de mort, même si ses PV suivis sont à 0.
            var enemies = all.Where(f => f.IsEnemy && !f.IsSummon).Select(Fmt);
            string mine = me.MaxLife > 0 ? $"{me.Life}/{me.MaxLife}" : $"{me.HpPercent}% (PV absolus inconnus)";
            TransportLog.Line($"[combat] PV tour {turnNo} : moi {mine} | alliés [{string.Join(", ", allies)}] | ennemis [{string.Join(", ", enemies)}]");
        }
        catch (System.Exception e) when (e is System.InvalidOperationException or System.NullReferenceException or System.ArgumentException) { /* purement informatif */ }
    }
}
