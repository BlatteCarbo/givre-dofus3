namespace GivreClone.Engine.Recovery;

public enum SmithAdmission
{
    Forge,
    NotForgeable,
    WrongProfession,
    ItemLevelUnknown,
    ProfessionLevelUnknown,
    ItemAboveProfession,
    Bound,
}

public enum SmithLevelingStop
{
    Continue,
    MissingTarget,
    MissingRune,
    ProfessionUnknown,
    AlreadyAtTarget,
    NoForgeableItem,
    NoRune,
    WorkshopFailed,
}

/// <summary>Choix de forgemagie deja tranches : niveau du metier, montee, selection et brisage.
/// Aucune de ces methodes nenvoie de message ni ne lit le jeu.</summary>
public static class SmithProfessionGate
{
    public static SmithAdmission Admit(int? itemJobId, int? selectedJobId, int? itemLevel, int? professionLevel)
    {
        if (itemJobId is null) return SmithAdmission.NotForgeable;
        if (selectedJobId is int selected && selected != itemJobId) return SmithAdmission.WrongProfession;
        if (itemLevel is not int level || level < 1) return SmithAdmission.ItemLevelUnknown;
        if (professionLevel is not int known) return SmithAdmission.ProfessionLevelUnknown;
        if (known < level) return SmithAdmission.ItemAboveProfession;
        return SmithAdmission.Forge;
    }
}

public readonly record struct LevelingItem(long Uid, bool InBag, SmithAdmission Admission, int Level = 0);

public static class SmithLeveling
{
    public static SmithLevelingStop Gate(int? professionLevel, int targetLevel, bool runeChosen, int runeQuantity)
    {
        if (targetLevel < 1) return SmithLevelingStop.MissingTarget;
        if (!runeChosen) return SmithLevelingStop.MissingRune;
        if (professionLevel is null) return SmithLevelingStop.ProfessionUnknown;
        if (professionLevel.Value >= targetLevel) return SmithLevelingStop.AlreadyAtTarget;
        if (runeQuantity <= 0) return SmithLevelingStop.NoRune;
        return SmithLevelingStop.Continue;
    }

    /// <summary>Objet forgeable du plus haut niveau (plus d'XP par rune) ; à niveau égal, reste sur l'objet en cours, sinon le premier de la file.</summary>
    public static long? NextUid(IReadOnlyList<LevelingItem> items, long? stayOn)
    {
        var ready = items.Where(i => i.InBag && i.Admission == SmithAdmission.Forge).ToList();
        if (ready.Count == 0) return null;
        int best = ready.Max(i => i.Level);
        if (stayOn is long stay && ready.Any(i => i.Uid == stay && i.Level == best)) return stay;
        return ready.First(i => i.Level == best).Uid;
    }

    public static bool CanSpendRune(long spent, long unitPrice, long maxKamas)
    {
        if (spent < 0 || maxKamas < 0) return false;
        if (maxKamas == long.MaxValue) return true;
        if (unitPrice < 0) return false;
        return spent <= maxKamas && unitPrice <= maxKamas - spent;
    }
}

public static class SmithJournal
{
    public static string Skip(string name, SmithAdmission admission, int? jobLevel, int? itemLevel) => admission switch
    {
        SmithAdmission.NotForgeable => $"{name} : aucun atelier de forgemagie pour ce type d'objet, objet passé.",
        SmithAdmission.WrongProfession => $"{name} : métier de forgemagie différent du métier choisi, objet passé.",
        SmithAdmission.ItemLevelUnknown => $"{name} : niveau d'objet inconnu, objet passé.",
        SmithAdmission.ProfessionLevelUnknown => $"{name} : niveau du métier pas encore annoncé, objet passé.",
        SmithAdmission.ItemAboveProfession => $"{name} : métier {jobLevel}, objet {itemLevel}, objet trop haut, passé.",
        SmithAdmission.Bound => $"{name} : objet lié au personnage ou au compte, forgemagie impossible, objet passé.",
        _ => $"{name} : objet passé.",
    };

    public static string Leveling(SmithLevelingStop stop) => stop switch
    {
        SmithLevelingStop.MissingTarget => "Niveau cible manquant : aucune rune passée.",
        SmithLevelingStop.MissingRune => "Rune de montée manquante : aucune rune passée.",
        SmithLevelingStop.ProfessionUnknown => "Niveau du métier pas encore annoncé : aucune rune passée, aucun trajet.",
        SmithLevelingStop.AlreadyAtTarget => "Métier déjà au niveau cible : aucune rune passée, aucun trajet.",
        SmithLevelingStop.NoForgeableItem => "Aucun objet forgeable pour ce métier : aucune rune passée, aucun trajet.",
        SmithLevelingStop.NoRune => "Rune choisie absente du sac et de la banque : aucune rune passée, aucun trajet.",
        SmithLevelingStop.WorkshopFailed => "Aucun atelier compatible rejoint : aucune rune passée.",
        _ => "",
    };

    public static string? ForgeBlock(bool inBag, bool queued, SmithAdmission admission, int? jobLevel, int? itemLevel)
    {
        if (!inBag) return "équipé";
        if (queued) return "déjà dans la file";
        return admission switch
        {
            SmithAdmission.Forge => null,
            SmithAdmission.NotForgeable => "pas forgemageable",
            SmithAdmission.WrongProfession => "mauvais métier",
            SmithAdmission.ItemLevelUnknown => "niveau d'objet inconnu",
            SmithAdmission.ProfessionLevelUnknown => "niveau du métier pas encore annoncé",
            SmithAdmission.ItemAboveProfession => $"objet {itemLevel}, métier {jobLevel}",
            SmithAdmission.Bound => "objet lié au personnage ou au compte",
            _ => "indisponible",
        };
    }

    public static string? CrushBlock(bool inBag, bool rune, bool resource, bool hasSlot, bool listed, bool bound = false)
    {
        if (!inBag) return "équipé";
        if (bound) return "objet lié au personnage ou au compte";
        if (rune) return "rune";
        if (resource) return "ressource";
        if (!hasSlot) return "pas un équipement";
        if (listed) return "déjà dans la liste";
        return null;
    }
}

public readonly record struct BagPick(long Uid, string Name, int Level, string Type, string? BlockReason)
{
    public bool Allowed => string.IsNullOrEmpty(BlockReason);
}

public static class BagPicker
{
    public static IReadOnlyList<BagPick> Filter(IReadOnlyList<BagPick> rows, string? query)
    {
        if (string.IsNullOrWhiteSpace(query)) return rows.ToArray();
        var words = query.Trim().Split(" ", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        return rows.Where(row => words.All(word =>
            row.Name.Contains(word, StringComparison.OrdinalIgnoreCase)
            || row.Type.Contains(word, StringComparison.OrdinalIgnoreCase)
            || row.Level.ToString(System.Globalization.CultureInfo.InvariantCulture).Contains(word, StringComparison.Ordinal))).ToArray();
    }

    public static IReadOnlyList<long> VisibleAllowed(IReadOnlyList<BagPick> visible)
        => visible.Where(row => row.Allowed).Select(row => row.Uid).ToArray();

    public static long? Enter(IReadOnlyList<BagPick> visible)
    {
        var allowed = VisibleAllowed(visible);
        return allowed.Count == 1 ? allowed[0] : null;
    }

    public static IReadOnlyList<long> ShiftRange(IReadOnlyList<BagPick> visible, long anchor, long current)
    {
        int from = Index(visible, anchor), to = Index(visible, current);
        if (from < 0 || to < 0) return Array.Empty<long>();
        if (from > to) (from, to) = (to, from);
        return visible.Skip(from).Take(to - from + 1).Where(row => row.Allowed).Select(row => row.Uid).ToArray();
    }

    public static IReadOnlyList<long> AddOrder(IReadOnlyList<BagPick> visible, IReadOnlySet<long> checkedIds)
        => visible.Where(row => row.Allowed && checkedIds.Contains(row.Uid)).Select(row => row.Uid).ToArray();

    private static int Index(IReadOnlyList<BagPick> rows, long uid)
    {
        for (int i = 0; i < rows.Count; i++) if (rows[i].Uid == uid) return i;
        return -1;
    }
}

public readonly record struct CrushOne(bool Crushed, bool InterfaceOpen);

public readonly record struct CrushReport(bool Opened, int Crushed, IReadOnlyList<long> Failed, int Remaining);

public static class CrushSequence
{
    public static CrushReport Run(IReadOnlyList<long> uids, bool opened, Func<long, CrushOne> step)
    {
        if (!opened) return new(false, 0, Array.Empty<long>(), uids.Count);
        var failed = new List<long>();
        int crushed = 0;
        for (int i = 0; i < uids.Count; i++)
        {
            var one = step(uids[i]);
            if (one.Crushed) crushed++;
            else failed.Add(uids[i]);
            if (!one.InterfaceOpen) return new(true, crushed, failed, uids.Count - i - 1);
        }
        return new(true, crushed, failed, 0);
    }
}

public static class RuneStock
{
    public static int Visible(int bag, int bank, bool bankKnown)
        => Math.Max(0, bag) + (bankKnown ? Math.Max(0, bank) : 0);

    public static int? Uid(int? bagUid, int? bankUid) => bagUid ?? bankUid;
}
