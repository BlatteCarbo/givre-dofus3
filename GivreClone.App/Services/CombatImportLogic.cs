using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using GivreClone.App.Services.SpellParity;

namespace GivreClone.App.Services;

public sealed record SettingChange(string Key, string Label, string Current, string Incoming)
{
    public bool Differs => !string.Equals(Current, Incoming, StringComparison.Ordinal);
}

public sealed record SpellChange(int SpellId, SpellConfig? Current, SpellConfig Incoming)
{
    public bool IsNew => Current is null;
    public bool Differs => Current is null || !CombatImportLogic.SameSpell(Current, Incoming);
}

/// <summary>Écarts entre le profil courant et celui d'un autre personnage. <see cref="Removed"/> : sorts courants absents de la source,
/// perdus si on REMPLACE (jamais en fusion).</summary>
public sealed record ImportPlan(IReadOnlyList<SettingChange> Settings, IReadOnlyList<SpellChange> Spells, IReadOnlyList<SpellConfig> Removed)
{
    public int ChangedSettings => Settings.Count(s => s.Differs);
    public int ChangedSpells => Spells.Count(s => s.Differs);
    public bool Identical => ChangedSettings == 0 && ChangedSpells == 0 && Removed.Count == 0;
}

/// <summary>
/// Import d'un profil de combat d'un autre personnage (MZP23) : calcul des différences, puis application des seuls éléments cochés,
/// en FUSION (les sorts cochés sont ajoutés/remplacés, les autres restent) ou en REMPLACEMENT (la rotation devient exactement la
/// sélection ; les sorts courants non retenus sont retirés). Logique pure ; le choix de ces règles est celui de ce lot.
/// </summary>
public static class CombatImportLogic
{
    private static string Bool(bool b) => b ? "oui" : "non";

    private static string PlacementLabel(PlacementMode mode) => mode switch
    {
        PlacementMode.Keep => "Aucun",
        PlacementMode.Near => "Proche",
        PlacementMode.Far => "Loin",
        _ => "Selon le style",
    };

    private static readonly (string Key, string Label, Func<CombatProfile, string> Get, Action<CombatProfile, CombatProfile> Copy)[] Table =
    {
        ("AutoFight", "Combat automatique", p => Bool(p.AutoFight), (t, s) => t.AutoFight = s.AutoFight),
        ("AutoSpells", "Sorts automatiques", p => Bool(p.AutoSpells), (t, s) => t.AutoSpells = s.AutoSpells),
        ("AutoElements", "Elements automatiques", p => p.AutoElements.ToString(), (t, s) => t.AutoElements = s.AutoElements),
        ("Style", "Style", p => p.Style.ToString(), (t, s) => t.Style = s.Style),
        ("PlacementMode", "Placement", p => PlacementLabel(p.PlacementMode), (t, s) => t.PlacementMode = s.PlacementMode),
        ("MovementPolicy", "Ne jamais approcher", p => p.MovementPolicy == CombatMovementPolicy.NeverApproach ? "oui" : "non", (t, s) => t.MovementPolicy = s.MovementPolicy),
        ("Target", "Ciblage", p => p.Target.ToString(), (t, s) => t.Target = s.Target),
        ("FinishKill", "Achever en priorité", p => Bool(p.FinishKill), (t, s) => t.FinishKill = s.FinishKill),
        ("Speed", "Vitesse", p => p.Speed.ToString(), (t, s) => t.Speed = s.Speed),
        ("MaxCastsPerTurn", "Lancers max par tour", p => p.MaxCastsPerTurn.ToString(CultureInfo.InvariantCulture), (t, s) => t.MaxCastsPerTurn = s.MaxCastsPerTurn),
        ("KiteMinDistance", "Distance de kite min", p => p.KiteMinDistance.ToString(CultureInfo.InvariantCulture), (t, s) => t.KiteMinDistance = s.KiteMinDistance),
        ("KiteMaxDistance", "Distance de kite max", p => p.KiteMaxDistance.ToString(CultureInfo.InvariantCulture), (t, s) => t.KiteMaxDistance = s.KiteMaxDistance),
        ("FleeBelowHpPercent", "Fuite sous PV %", p => p.FleeBelowHpPercent.ToString(CultureInfo.InvariantCulture), (t, s) => t.FleeBelowHpPercent = s.FleeBelowHpPercent),
        ("AutoLevelStats", "Montée de niveau auto", p => Bool(p.AutoLevelStats), (t, s) => t.AutoLevelStats = s.AutoLevelStats),
        ("LevelStatTarget", "Caractéristique cible", p => p.LevelStatTarget.ToString(), (t, s) => t.LevelStatTarget = s.LevelStatTarget),
        ("AutoPreFight", "Pré-combat automatique", p => Bool(p.AutoPreFight), (t, s) => t.AutoPreFight = s.AutoPreFight),
        ("AutoChallenge", "Défi automatique", p => Bool(p.AutoChallenge), (t, s) => t.AutoChallenge = s.AutoChallenge),
        ("ChallengeRules", "Règles de défis", p => Bool(p.ChallengeRules), (t, s) => t.ChallengeRules = s.ChallengeRules),
        ("ChallengeLoot", "Défis : butin", p => Bool(p.ChallengeLoot), (t, s) => t.ChallengeLoot = s.ChallengeLoot),
        ("ChallengeLists", "Défis acceptés / refusés", p => $"+{string.Join(",", p.ChallengeOn.OrderBy(i => i))} -{string.Join(",", p.ChallengeOff.OrderBy(i => i))}",
            (t, s) => { t.ChallengeOn = new(s.ChallengeOn); t.ChallengeOff = new(s.ChallengeOff); }),
        ("AutoRevive", "Résurrection automatique", p => Bool(p.AutoRevive), (t, s) => t.AutoRevive = s.AutoRevive),
        ("NoAnim", "Sans animation", p => Bool(p.NoAnim), (t, s) => t.NoAnim = s.NoAnim),
        ("Tactical", "Mode tactique", p => Bool(p.Tactical), (t, s) => t.Tactical = s.Tactical),
        ("Collapse", "Attentes réduites", p => Bool(p.Collapse), (t, s) => t.Collapse = s.Collapse),
        ("InstantMove", "Déplacements instantanés", p => Bool(p.InstantMove), (t, s) => t.InstantMove = s.InstantMove),
        ("LockJoin", "Verrouiller : rejoindre", p => Bool(p.LockJoin), (t, s) => t.LockJoin = s.LockJoin),
        ("LockParty", "Verrouiller : groupe seul", p => Bool(p.LockParty), (t, s) => t.LockParty = s.LockParty),
        ("LockSpectators", "Verrouiller : spectateurs", p => Bool(p.LockSpectators), (t, s) => t.LockSpectators = s.LockSpectators),
        ("TurboAckAll", "Turbo : tout acquitter", p => Bool(p.TurboAckAll), (t, s) => t.TurboAckAll = s.TurboAckAll),
        ("TurboEagerReady", "Turbo : prêt anticipé", p => Bool(p.TurboEagerReady), (t, s) => t.TurboEagerReady = s.TurboEagerReady),
        ("ChallengeAbandonHp", "Abandon de défi sous PV %", p => p.ChallengeAbandonHp.ToString(CultureInfo.InvariantCulture), (t, s) => t.ChallengeAbandonHp = s.ChallengeAbandonHp),
    };

    /// <summary>Deux sorts sont identiques si tous leurs réglages le sont.</summary>
    public static bool SameSpell(SpellConfig a, SpellConfig b)
        => a.SpellId == b.SpellId && a.Priority == b.Priority && a.PerTurn == b.PerTurn && a.Cooldown == b.Cooldown && a.Enabled == b.Enabled
        && a.OnMe == b.OnMe && a.TargetKind == b.TargetKind && a.SelfBuff == b.SelfBuff && a.IsSummon == b.IsSummon && a.MaxSummons == b.MaxSummons
        && a.MinAp == b.MinAp && a.MinTurn == b.MinTurn && a.MinEnemies == b.MinEnemies && a.MinAoeHits == b.MinAoeHits
        && a.SelfHpMin == b.SelfHpMin && a.SelfHpMax == b.SelfHpMax && a.TgtHpMin == b.TgtHpMin && a.TgtHpMax == b.TgtHpMax
        && a.CastRangeMin == b.CastRangeMin && a.CastRangeMax == b.CastRangeMax && a.AllowApproach == b.AllowApproach;

    /// <summary>Décrit les différences de ciblage, limites et conditions en premier pour éviter
    /// d'afficher deux profils « identiques » lorsqu'ils sont en réalité différents.</summary>
    public static string Describe(SpellConfig s, SpellConfig? comparedTo = null)
        => SpellParityDisplay.Describe(s, comparedTo);

    public static ImportPlan Diff(CombatProfile current, CombatProfile incoming, bool replace)
    {
        var settings = Table.Select(t => new SettingChange(t.Key, t.Label, t.Get(current), t.Get(incoming))).ToList();
        var spells = incoming.Spells.OrderBy(s => s.Priority)
            .Select(s => new SpellChange(s.SpellId, current.Spells.FirstOrDefault(c => c.SpellId == s.SpellId), s)).ToList();
        var removed = replace ? current.Spells.Where(c => incoming.Spells.All(s => s.SpellId != c.SpellId)).ToList() : new List<SpellConfig>();
        return new ImportPlan(settings, spells, removed);
    }

    public sealed record ApplyResult(int Settings, int SpellsAdded, int SpellsReplaced, int SpellsRemoved);

    /// <summary>Applique à <paramref name="target"/> (en place) les réglages et sorts cochés. Clés/ids inconnus ignorés.</summary>
    public static ApplyResult Apply(CombatProfile target, CombatProfile source, ISet<string> settingKeys, ISet<int> spellIds, bool replace)
    {
        int ns = 0;
        foreach (var t in Table) if (settingKeys.Contains(t.Key)) { t.Copy(target, source); ns++; }

        var chosen = source.Spells.Where(s => spellIds.Contains(s.SpellId)).OrderBy(s => s.Priority).Select(s => s.Clone()).ToList();
        int added = 0, replaced = 0, removed = 0;
        if (replace)
        {
            removed = target.Spells.Count(c => chosen.All(s => s.SpellId != c.SpellId));
            replaced = chosen.Count(s => target.Spells.Any(c => c.SpellId == s.SpellId));
            added = chosen.Count - replaced;
            target.Spells = chosen;
        }
        else
        {
            foreach (var s in chosen)
            {
                int i = target.Spells.FindIndex(c => c.SpellId == s.SpellId);
                if (i >= 0) { target.Spells[i] = s; replaced++; } else { target.Spells.Add(s); added++; }
            }
        }
        return new ApplyResult(ns, added, replaced, removed);
    }

    /// <summary>Résumé d'un plan : « 3 réglage(s), 2 sort(s) différent(s), 1 sort(s) retiré(s) » ; rien → « Profils identiques ».</summary>
    public static string Summary(ImportPlan p)
    {
        if (p.Identical) return "Profils identiques";
        var parts = new List<string>();
        if (p.ChangedSettings > 0) parts.Add($"{p.ChangedSettings} réglage(s)");
        if (p.ChangedSpells > 0) parts.Add($"{p.ChangedSpells} sort(s) différent(s)");
        if (p.Removed.Count > 0) parts.Add($"{p.Removed.Count} sort(s) retiré(s)");
        return string.Join(", ", parts);
    }
}

/// <summary>Verrous de début de combat : un verrou est posé s'il est demandé par le script OU par le profil du personnage.</summary>
public readonly record struct FightLocks(bool Spectators, bool Join, bool Party)
{
    public bool Any => Spectators || Join || Party;

    public static FightLocks Merge(FightLocks script, CombatProfile? profile)
        => new(script.Spectators || (profile?.LockSpectators ?? false), script.Join || (profile?.LockJoin ?? false), script.Party || (profile?.LockParty ?? false));
}
