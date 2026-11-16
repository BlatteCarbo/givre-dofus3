using System.Collections.Generic;

namespace GivreClone.App.Services;

/// <summary>
/// Lot C2 : réglages de combat posés par un SCRIPT Lua (combat:setStyle, setTargetMode, setChallengeMode…), par bot.
/// Couche EN MÉMOIRE au-dessus du profil poussé par l'UI (jamais sauvegardée dans combat.json) : le profil effectif du
/// moteur = base UI + ces surcharges, re-clampé par les droits. Une édition de l'onglet Combat ne les écrase donc pas ;
/// elles sont effacées au (re)démarrage / arrêt du script ou par combat:resetProfile().
/// L'auto-combat (combat:setAutoFight) N'EST PAS ici : il reste porté par l'override C1 (propriété du run, lu en direct
/// par le moteur), qui survit déjà aux poussées UI.
/// </summary>
public sealed class ScriptCombatOverrides
{
    public bool? AutoPreFight, AutoChallenge, ChallengeByScript, FinishKill, AutoSpells;
    public CombatStyle? Style;
    public TargetMode? Target;
    public CombatSpeed? Speed;
    public int? MaxCastsPerTurn, KiteMin, KiteMax;

    /// <summary>Distance exacte de <c>fightBasic:setApproachDistance</c> : min et max deviennent <paramref name="distance"/>
    /// (0–20). Les champs kite de l'onglet Combat restent indépendants tant que ce callback ne les a pas posés tous les deux.</summary>
    public void HoldExactApproachDistance(int distance)
    {
        int d = Math.Clamp(distance, 0, 20);
        KiteMin = d;
        KiteMax = d;
    }
    public Dictionary<int, bool> SpellEnabled { get; } = new();
    /// <summary>Rotation COMPLÈTE posée par le script: remplace les sorts
    /// de l'onglet Combat tant qu'elle n'est pas null (liste vide = aucun sort).</summary>
    public List<SpellConfig>? Spells;

    public bool IsEmpty =>
        AutoPreFight is null && AutoChallenge is null && ChallengeByScript is null && FinishKill is null && AutoSpells is null
        && Style is null && Target is null && Speed is null
        && MaxCastsPerTurn is null && KiteMin is null && KiteMax is null
        && SpellEnabled.Count == 0 && Spells is null;

    /// <summary>Pur. <paramref name="p"/> est un CLONE FRAIS (jamais l'instance stockée dans _combat) ; ré-applique les
    /// clamps de droits (un script ne contourne pas Perm.Fight).</summary>
    public CombatProfile ApplyTo(CombatProfile p, bool canFight)
    {
        if (AutoPreFight is { } apf) p.AutoPreFight = apf;
        if (AutoChallenge is { } ach) p.AutoChallenge = ach;
        if (ChallengeByScript is { } cbs) p.ChallengeByScript = cbs;
        if (FinishKill is { } fk) p.FinishKill = fk;
        if (Style is { } st) p.Style = st;
        if (Target is { } tg) p.Target = tg;
        if (Speed is { } sp) p.Speed = sp;
        if (MaxCastsPerTurn is { } mc) p.MaxCastsPerTurn = mc;
        if (KiteMin is { } kmin) p.KiteMinDistance = kmin;
        if (KiteMax is { } kmax) p.KiteMaxDistance = kmax;
        if (Spells is { } list) p.Spells = new CombatProfile { Spells = list }.Clone().Spells;   // copie profonde
        foreach (var kv in SpellEnabled)
        {
            bool found = false;
            foreach (var s in p.Spells)
                if (s.SpellId == kv.Key) { s.Enabled = kv.Value; found = true; }
            // Sort appris HORS liste désactivé par le script : ligne désactivée -> le mode automatique l'écarte aussi.
            if (!found && !kv.Value) p.Spells.Add(new SpellConfig { SpellId = kv.Key, Enabled = false, Priority = int.MaxValue });
        }
        // Mode automatique : posé par le script, sinon coupé dès que le script impose SA rotation (clearSpells / addSpell).
        if (AutoSpells is { } auto) p.AutoSpells = auto;
        else if (Spells is not null) p.AutoSpells = false;
        p.AutoFight &= canFight;
        p.AutoPreFight &= canFight;
        p.AutoChallenge &= canFight;
        p.ChallengeByScript &= canFight;
        return p;
    }
}
