using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.App.Services;
using GivreClone.App.Services.Eco;
using GivreClone.App.Services.LotM;
using GivreClone.Protocol;

namespace GivreClone.App.ViewModels;

public partial class SmithMagicViewModel
{
    public HashSet<int> RejectedWorkshopElements { get; } = new();
    public Func<uint, IReadOnlyList<MapInteractive>?>? InteractivesForPid { get; set; }
    public Func<uint, long>? CurrentMap { get; set; }
    public Func<uint, long, CancellationToken, Task<bool>>? Travel { get; set; }
    /// <summary>Entre dans l'atelier dont le nom est celui du repère, puis confirme la carte intérieure. Ne passe aucune rune.</summary>
    public Func<uint, string, CancellationToken, Task<bool>>? EnterInterior { get; set; }
    /// <summary>Marche jusqu'à une case d'où la machine (élément, uid du skill) s'utilise. Sans ce pas, le serveur refuse l'usage à distance.</summary>
    public Func<uint, int, int, CancellationToken, Task<bool>>? ApproachWorkshop { get; set; }
    public Func<long, int?> RequiredSkill { get; set; } = gid => SkillForItem(Stores.Launch.GameData, gid);
    public Func<uint, IReadOnlyList<WorldPlace>> WorkshopPlaces { get; set; } = pid =>
    {
        var launch = Stores.Launch;
        long map = launch.CurrentMap(pid) ?? 0;
        if (launch.GameData is not { } db || launch.MapCoordOf(map) is not (int x, int y))
            return Array.Empty<WorldPlace>();
        return db.Places("forgemag", new PlaceOrigin(x, y, launch.WorldMapOf(map) ?? 1), 50)
            ?? Array.Empty<WorldPlace>();
    };

    // Use the installed game's relationship, not translated item names or guessed profession IDs.
    /// <summary>SkillData 84 « Entrer ». Le world-graph (porte type 32) ouvre l'atelier avec 184 « Utiliser »
    /// (Bonta 522088, Brâkmar 522940, Pandala 520505). Seul l'interactif au nom du repère est franchi.</summary>
    public const int EnterBuildingSkillId = 84;
    public const int UseBuildingSkillId = 184;

    public static InteractiveSkill? DoorSkill(MapInteractive door)
        => door.Skills.FirstOrDefault(s => s.Enabled && s.SkillId == UseBuildingSkillId)
        ?? door.Skills.FirstOrDefault(s => s.Enabled && s.SkillId == EnterBuildingSkillId);

    public static MapInteractive? WorkshopDoor(IEnumerable<MapInteractive>? interactives, string placeName)
    {
        if (interactives is null || string.IsNullOrWhiteSpace(placeName)) return null;
        string wanted = PlaceText.Normalize(placeName);
        return interactives.FirstOrDefault(i => i.CellId >= 0
            && PlaceText.Normalize(i.Name ?? "") == wanted
            && DoorSkill(i) is not null);
    }

    public static int? SkillForItem(GameDb? db, long gid)
    {
        if (db?.Row("ItemData", gid.ToString(CultureInfo.InvariantCulture)) is not { } item
            || !item.TryGetValue("typeId", out var type) || type is null) return null;
        foreach (int skill in db.ModifyingSkills(Convert.ToInt32(type, CultureInfo.InvariantCulture)))
            if (RuneCatalog.SmithSkills.Contains(skill))
                return skill;
        return null;
    }

    private void CheckSession(uint pid, CancellationToken ct)
    {
        ct.ThrowIfCancellationRequested();
        if (pid != (uint)_pid) throw new OperationCanceledException("Le personnage a changé.", ct);
    }

    public (int ElementId, int SkillUid)? CurrentWorkshop(uint pid, int skill)
        => InteractivesForPid?.Invoke(pid)?.Where(i => !RejectedWorkshopElements.Contains(i.ElementId)).SelectMany(i => i.Skills
            .Where(s => s.Enabled && s.SkillId == skill)
            .Select(s => ((int ElementId, int SkillUid)?)(i.ElementId, s.SkillInstanceUid))).FirstOrDefault();

    /// <summary>Only returns a live compatible interactive after confirmed arrival for the captured bot.</summary>
    public async Task<(int ElementId, int SkillUid)?> PrepareWorkshopAsync(uint pid, long gid, CancellationToken ct)
    {
        while (await FindWorkshopAsync(pid, gid, ct).ConfigureAwait(true) is { } machine)
        {
            if (await ApproachAsync(pid, machine, "l'atelier", ct).ConfigureAwait(true) is { } reachable)
                return reachable;
            RejectedWorkshopElements.Add(machine.ElementId);
        }
        return null;
    }

    private async Task<(int ElementId, int SkillUid)?> ApproachAsync(uint pid, (int ElementId, int SkillUid)? machine, string what, CancellationToken ct)
    {
        if (machine is not { } found || ApproachWorkshop is null) return machine;
        CheckSession(pid, ct);
        if (await ApproachWorkshop(pid, found.ElementId, found.SkillUid, ct).ConfigureAwait(true)) return found;
        Log($"Impossible de s'approcher de {what} : aucune rune passée.");
        return null;
    }

    private async Task<(int ElementId, int SkillUid)?> FindWorkshopAsync(uint pid, long gid, CancellationToken ct)
    {
        CheckSession(pid, ct);
        if (RequiredSkill(gid) is not { } skill)
        { Log("Métier de forgemagie de cet objet inconnu dans les données du jeu."); return null; }
        if (CurrentWorkshop(pid, skill) is { } local) return local;
        if (Travel is null || CurrentMap is null)
        { Log("Trajet vers l'atelier indisponible."); return null; }
        foreach (var place in WorkshopPlaces(pid).Where(p => p.MapId > 0).DistinctBy(p => p.MapId))
        {
            CheckSession(pid, ct);
            Log($"Trajet vers {place.Name} ({place.Coords})…");
            bool arrived = await Travel(pid, place.MapId, ct).ConfigureAwait(true);
            CheckSession(pid, ct);
            if (arrived && CurrentMap(pid) == place.MapId && CurrentWorkshop(pid, skill) is { } workshop)
                return workshop;
            // Le repère HintData est la carte de la PORTE. Le skill du métier n'existe qu'à l'intérieur.
            if (arrived && CurrentMap(pid) == place.MapId && EnterInterior is not null
                && WorkshopDoor(InteractivesForPid?.Invoke(pid), place.Name) is not null)
            {
                CheckSession(pid, ct);
                long outside = CurrentMap(pid);
                Log("Entrée dans " + place.Name + " (" + place.Coords + ")...");
                bool entered = await EnterInterior(pid, place.Name, ct).ConfigureAwait(true);
                CheckSession(pid, ct);
                if (entered && CurrentMap(pid) is long inside && inside != 0 && inside != outside
                    && CurrentWorkshop(pid, skill) is { } interior)
                    return interior;
            }
        }
        Log("Aucun atelier compatible rejoint : aucune rune passée.");
        return null;
    }

    public Func<int?> CrushSkill { get; set; } = () => CrushSkillId(Stores.Launch.GameData);
    public Func<uint, IReadOnlyList<WorldPlace>> CrusherPlaces { get; set; } = _ => Array.Empty<WorldPlace>();

    public async Task<(int ElementId, int SkillUid)?> PrepareCrusherAsync(uint pid, CancellationToken ct)
        => await ApproachAsync(pid, await FindCrusherAsync(pid, ct).ConfigureAwait(true), "du concasseur", ct).ConfigureAwait(true);

    private async Task<(int ElementId, int SkillUid)?> FindCrusherAsync(uint pid, CancellationToken ct)
    {
        CheckSession(pid, ct);
        if (CrushSkill() is not int skill)
        { Log("Compétence Briser des objets introuvable : aucun objet cassé."); return null; }
        if (CurrentWorkshop(pid, skill) is { } local) return local;
        if (Travel is null || CurrentMap is null)
        { Log("Trajet vers le concasseur indisponible : aucun objet cassé."); return null; }
        var places = CrusherPlaces(pid).Where(place => place.MapId > 0).DistinctBy(place => place.MapId).ToArray();
        if (places.Length == 0)
        { Log("Aucun concasseur connu dans les données du jeu : aucun objet cassé."); return null; }
        foreach (var place in places)
        {
            CheckSession(pid, ct);
            Log($"Trajet vers {place.Name} ({place.Coords})…");
            bool arrived = await Travel(pid, place.MapId, ct).ConfigureAwait(true);
            CheckSession(pid, ct);
            if (arrived && CurrentMap(pid) == place.MapId && CurrentWorkshop(pid, skill) is { } crusher)
                return crusher;
        }
        Log("Aucun concasseur rejoint : aucun objet cassé.");
        return null;
    }

    public static int? ItemLevel(GameDb? db, long gid)
        => db?.Row("ItemData", gid.ToString(CultureInfo.InvariantCulture)) is { } row ? Positive(row, "level") : null;

    public static int? JobForSkill(GameDb? db, int skill)
        => db?.Row("SkillData", skill.ToString(CultureInfo.InvariantCulture)) is { } row ? Positive(row, "parentJobId") : null;

    public static int? CrushSkillId(GameDb? db)
    {
        if (db is null || !db.IsKnownTable("SkillData")) return null;
        foreach (var row in db.SearchText("SkillData", "Briser des objets", 20))
        {
            if (!row.TryGetValue("nameId", out var name) || name is null) continue;
            if (db.Text(Convert.ToInt64(name, CultureInfo.InvariantCulture)) != "Briser des objets") continue;
            if (row.TryGetValue("id", out var id) && id is not null)
                return Convert.ToInt32(id, CultureInfo.InvariantCulture);
        }
        return null;
    }

    public static bool HasEquipmentSlot(GameDb? db, long gid)
    {
        if (db?.Row("ItemData", gid.ToString(CultureInfo.InvariantCulture)) is not { } item) return false;
        if (Positive(item, "typeId") is not int type) return false;
        if (db.Row("ItemTypeData", type.ToString(CultureInfo.InvariantCulture)) is not { } kind) return false;
        if (Positive(kind, "superTypeId") is not int super) return false;
        if (db.Row("ItemSuperTypeData", super.ToString(CultureInfo.InvariantCulture)) is not { } slot) return false;
        if (!slot.TryGetValue("possiblePositions", out var raw) || raw is null) return false;
        var text = Convert.ToString(raw, CultureInfo.InvariantCulture) ?? "";
        return text.IndexOfAny("0123456789".ToCharArray()) >= 0;
    }

    public static bool IsGatheredResource(GameDb? db, long gid)
        => db?.IsGatheredItem(gid) == true;

    private static int? Positive(IReadOnlyDictionary<string, object?> row, string key)
    {
        if (!row.TryGetValue(key, out var value) || value is null) return null;
        int number = Convert.ToInt32(value, CultureInfo.InvariantCulture);
        return number > 0 ? number : null;
    }
}
