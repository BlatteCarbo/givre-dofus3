using System;
using System.Collections.Generic;
using System.Linq;
using GivreClone.Protocol;

namespace GivreClone.App.Services;

public enum MapEntityKind { Self, Player, Monster, Npc, Resource }

[Flags]
public enum MapEntityFilter { None = 0, Monsters = 1, Npcs = 2, Players = 4, Resources = 8, All = 15 }

/// <summary>Une entité inspectable de la carte. <see cref="Key"/> est STABLE d'un rafraîchissement à l'autre (id d'acteur ou d'élément) :
/// c'est elle qui conserve la sélection, jamais la cellule ni la position dans la liste.</summary>
public sealed record MapEntity(string Key, MapEntityKind Kind, int CellId, string Title, string? Subtitle, int MinLevel, int MaxLevel, object Source)
{
    public MapActor? Actor => Source as MapActor;
    public MapInteractive? Interactive => Source as MapInteractive;
}

/// <summary>Logique PURE de la couche d'inspection de la carte (testable sans Avalonia) : construction des entités à partir des
/// acteurs / interactifs reçus, filtres, recherche, sélection stable. Aucune donnée n'est inventée : une valeur inconnue reste null / 0.</summary>
public static class MapInspection
{
    public static string ActorKey(long actorId) => "a:" + actorId;
    public static string InteractiveKey(int elementId) => "i:" + elementId;

    public static IReadOnlyList<MapEntity> Build(IReadOnlyList<MapActor>? actors, IReadOnlyList<MapInteractive>? interactives, string? selfName)
    {
        var list = new List<MapEntity>((actors?.Count ?? 0) + (interactives?.Count ?? 0));
        if (actors is not null)
            foreach (var a in actors)
            {
                if (a.CellId is < 0 or >= 560) continue;
                bool self = a.Kind == MapActorKind.Player && selfName is not null && string.Equals(a.Name, selfName, StringComparison.Ordinal);
                var kind = self ? MapEntityKind.Self : a.Kind switch
                {
                    MapActorKind.Player => MapEntityKind.Player,
                    MapActorKind.Monster => MapEntityKind.Monster,
                    _ => MapEntityKind.Npc,   // Npc, TreasureHint, Unknown : tous des « PNJ » affichables
                };
                int min = 0, max = 0;
                if (a.Members is { Count: > 0 } ms)
                {
                    var lv = ms.Select(m => m.Level).Where(l => l > 0).ToList();
                    if (lv.Count > 0) { min = lv.Min(); max = lv.Max(); }
                }
                list.Add(new MapEntity(ActorKey(a.ActorId), kind, a.CellId, a.Name ?? "?", null, min, max, a));
            }
        if (interactives is not null)
            foreach (var it in interactives)
                if (it.CellId is >= 0 and < 560)
                    list.Add(new MapEntity(InteractiveKey(it.ElementId), MapEntityKind.Resource, it.CellId, it.Name ?? ("#" + it.ElementTypeId), it.ActionName, 0, 0, it));
        return list;
    }

    public static bool Passes(MapEntity e, MapEntityFilter f) => e.Kind switch
    {
        MapEntityKind.Self => true,   // notre perso n'est jamais masqué
        MapEntityKind.Monster => f.HasFlag(MapEntityFilter.Monsters),
        MapEntityKind.Npc => f.HasFlag(MapEntityFilter.Npcs),
        MapEntityKind.Player => f.HasFlag(MapEntityFilter.Players),
        _ => f.HasFlag(MapEntityFilter.Resources),
    };

    /// <summary>Recherche : sous-chaîne insensible à la casse/accents sur le titre, le sous-titre et le nom des monstres du groupe, ou id exact.</summary>
    public static bool Matches(MapEntity e, string? search)
    {
        if (string.IsNullOrWhiteSpace(search)) return true;
        string q = Fold(search.Trim());
        if (Fold(e.Title).Contains(q) || (e.Subtitle is not null && Fold(e.Subtitle).Contains(q))) return true;
        if (e.Actor?.Monsters is { } ms && ms.Any(m => Fold(m.Name).Contains(q))) return true;
        return e.Key.EndsWith(":" + search.Trim(), StringComparison.Ordinal);
    }

    private static string Fold(string s)
    {
        var d = s.Normalize(System.Text.NormalizationForm.FormD);
        var sb = new System.Text.StringBuilder(d.Length);
        foreach (char c in d)
            if (System.Globalization.CharUnicodeInfo.GetUnicodeCategory(c) != System.Globalization.UnicodeCategory.NonSpacingMark) sb.Append(char.ToLowerInvariant(c));
        return sb.ToString();
    }

    /// <summary>Entités d'une cellule, dans un ordre stable (monstres, PNJ, joueurs, ressources) puis par clé.</summary>
    public static IReadOnlyList<MapEntity> AtCell(IEnumerable<MapEntity> all, int cell) =>
        all.Where(e => e.CellId == cell).OrderBy(e => e.Kind).ThenBy(e => e.Key, StringComparer.Ordinal).ToList();

    /// <summary>La sélection survit tant que la MÊME entité (clé) existe et que la carte n'a pas changé (époque) ; sinon null.</summary>
    public static string? Reconcile(string? selectedKey, IEnumerable<MapEntity> all, int selectedEpoch, int currentEpoch)
    {
        if (selectedKey is null || selectedEpoch != currentEpoch) return null;
        return all.Any(e => e.Key == selectedKey) ? selectedKey : null;
    }

    /// <summary>Entité suivante / précédente (navigation clavier) dans l'ordre d'affichage ; part du début/de la fin sans sélection.</summary>
    public static MapEntity? Cycle(IReadOnlyList<MapEntity> ordered, string? currentKey, int dir)
    {
        if (ordered.Count == 0) return null;
        int i = currentKey is null ? -1 : ordered.ToList().FindIndex(e => e.Key == currentKey);
        int n = i < 0 ? (dir >= 0 ? 0 : ordered.Count - 1) : ((i + dir) % ordered.Count + ordered.Count) % ordered.Count;
        return ordered[n];
    }

    public static string Freshness(DateTime? at, DateTime now)
    {
        if (at is null) return "";
        double s = Math.Max(0, (now - at.Value).TotalSeconds);
        return s < 60 ? $"{(int)s} s" : s < 3600 ? $"{(int)(s / 60)} min" : $"{(int)(s / 3600)} h";
    }

    public static string LevelText(MapEntity e) =>
        e.MaxLevel <= 0 ? "" : e.MinLevel == e.MaxLevel ? e.MaxLevel.ToString() : $"{e.MinLevel}–{e.MaxLevel}";
}

/// <summary>État PUR de l'inspection d'une carte (sans Avalonia) : sources, filtres, recherche, sélection stable par clé, sélecteur de
/// superposition. <c>MapInspectorViewModel</c> n'est qu'une enveloppe d'affichage autour de ce modèle.</summary>
public sealed class MapInspectionModel
{
    private IReadOnlyList<MapActor>? _actors;
    private IReadOnlyList<MapInteractive>? _interactives;
    private string? _selfName;
    private int _epoch, _selectedEpoch;

    public MapEntityFilter Filter { get; set; } = MapEntityFilter.All;
    public string Search { get; set; } = "";
    public IReadOnlyList<MapEntity> All { get; private set; } = Array.Empty<MapEntity>();
    public IReadOnlyList<MapEntity> Shown { get; private set; } = Array.Empty<MapEntity>();
    public IReadOnlyList<MapEntity> Results { get; private set; } = Array.Empty<MapEntity>();
    /// <summary>Époque de carte courante (change à chaque nouvelle carte).</summary>
    public int Epoch => _epoch;
    public IReadOnlyList<MapEntity> Candidates { get; set; } = Array.Empty<MapEntity>();
    public MapEntity? Selected { get; private set; }
    public bool Searching => !string.IsNullOrWhiteSpace(Search);
    public IReadOnlyList<MapActor>? VisibleActors { get; private set; }
    public IReadOnlyList<MapInteractive>? VisibleInteractives { get; private set; }
    public AttackConfirmation Attack { get; } = new();
    /// <summary>Vrai quand un aperçu « Combattre » vient d'être annulé parce que la cible a changé (groupe déplacé / modifié / disparu).</summary>
    public bool AttackExpired { get; set; }

    /// <summary>Recalcule tout. Un changement d'<paramref name="epoch"/> (nouvelle carte) invalide la sélection.</summary>
    public void Update(IReadOnlyList<MapActor>? actors, IReadOnlyList<MapInteractive>? interactives, string? selfName, int epoch)
    {
        _actors = actors; _interactives = interactives; _selfName = selfName;
        if (epoch != _epoch) { _epoch = epoch; Clear(); }
        Rebuild();
    }

    public void Rebuild()
    {
        All = MapInspection.Build(_actors, _interactives, _selfName);
        Shown = All.Where(e => MapInspection.Passes(e, Filter)).ToList();
        var keys = Shown.Select(e => e.Key).ToHashSet(StringComparer.Ordinal);
        VisibleActors = _actors?.Where(a => keys.Contains(MapInspection.ActorKey(a.ActorId))).ToList();
        VisibleInteractives = _interactives?.Where(i => keys.Contains(MapInspection.InteractiveKey(i.ElementId))).ToList();
        Results = Searching
            ? Shown.Where(e => MapInspection.Matches(e, Search)).OrderBy(e => e.Kind).ThenBy(e => e.Title, StringComparer.CurrentCultureIgnoreCase).ToList()
            : Array.Empty<MapEntity>();

        // Sélection stable : retrouvée par clé ; absente, masquée par un filtre ou carte différente -> invalidée.
        string? key = MapInspection.Reconcile(Selected?.Key, Shown, _selectedEpoch, _epoch);
        if (key is null) { if (Attack.Pending) AttackExpired = true; if (Selected is not null) Clear(); }
        else
        {
            Selected = Shown.First(e => e.Key == key);
            if (Candidates.Count > 1) Candidates = MapInspection.AtCell(Shown, Selected.CellId);
            if (Attack.Pending && !Attack.StillValid(Selected)) { Attack.Cancel(); AttackExpired = true; }
        }
    }

    /// <summary>Clic sur une cellule : true si elle contient une entité visible (hors notre perso seul) -> fiche / sélecteur ; false = cellule vide.</summary>
    public bool InspectCell(int cell)
    {
        var here = MapInspection.AtCell(Shown, cell);
        if (here.Count == 0 || (here.Count == 1 && here[0].Kind == MapEntityKind.Self)) return false;
        Candidates = here;
        Select(here.FirstOrDefault(e => e.Key == Selected?.Key) ?? here[0]);
        return true;
    }

    public void Select(MapEntity? e)
    {
        if (e is null) { Clear(); return; }
        _selectedEpoch = _epoch;
        if (Selected is not null && Selected.Key != e.Key) Attack.Cancel();   // un aperçu ne survit pas à un changement de cible
        Selected = e;
        if (Candidates.Count > 0 && Candidates[0].CellId != e.CellId) Candidates = Array.Empty<MapEntity>();
    }

    public void Clear() { Selected = null; Candidates = Array.Empty<MapEntity>(); Attack.Cancel(); }

    /// <summary>Accès rapide : sélectionne notre perso (s'il est sur la carte). Renvoie false s'il est inconnu.</summary>
    public bool SelectSelf()
    {
        var me = All.FirstOrDefault(e => e.Kind == MapEntityKind.Self);
        if (me is null) return false;
        Candidates = Array.Empty<MapEntity>();
        Select(me);
        return true;
    }

    /// <summary>Navigation clavier : entité suivante / précédente (ordre de cellule), hors notre perso, en respectant filtres et recherche.</summary>
    public void Cycle(int dir)
    {
        var ordered = Shown.Where(e => e.Kind != MapEntityKind.Self && MapInspection.Matches(e, Search))
            .OrderBy(e => e.CellId).ThenBy(e => e.Kind).ThenBy(e => e.Key, StringComparer.Ordinal).ToList();
        if (MapInspection.Cycle(ordered, Selected?.Key, dir) is not { } next) return;
        Candidates = MapInspection.AtCell(Shown, next.CellId);
        Select(next);
    }
}

/// <summary>XP de base d'un groupe de monstres. <see cref="Known"/> = somme des XP de base des membres dont le niveau est connu ET trouvé dans la base du jeu ;
/// <see cref="Complete"/> = tous les membres sont couverts. Aucune valeur n'est devinée : un membre sans niveau exact n'est pas compté.</summary>
public readonly record struct GroupXp(long Known, int KnownMembers, int TotalMembers)
{
    public bool Complete => TotalMembers > 0 && KnownMembers == TotalMembers;
    public bool Any => KnownMembers > 0;

    /// <param name="xpByLevel">genericId -> (niveau de grade -> XP de base), ou null si la base du jeu est absente.</param>
    public static GroupXp Compute(MapActor group, Func<int, IReadOnlyDictionary<int, long>?> xpByLevel)
    {
        int total = group.Monsters?.Sum(m => m.Count) ?? 0;
        if (group.Members is not { Count: > 0 } members) return new GroupXp(0, 0, total);
        long sum = 0; int known = 0;
        foreach (var m in members)
            if (m.Level > 0 && xpByLevel(m.GenericId) is { } byLevel && byLevel.TryGetValue(m.Level, out long xp)) { sum += xp; known++; }
        return new GroupXp(sum, known, Math.Max(total, members.Count));
    }
}

/// <summary>Demande « Combattre » en DEUX temps : aperçu (cible figée) puis confirmation explicite. La confirmation n'est valable que si
/// l'entité est toujours la même (clé), à la même cellule et avec la même composition ; sinon elle est refusée et il faut reconfirmer.</summary>
public sealed class AttackConfirmation
{
    private string? _key; private int _cell; private string? _signature;
    public bool Pending => _key is not null;

    public static string Signature(MapActor a) =>
        string.Join("|", (a.Members ?? Array.Empty<MonsterMember>()).Select(m => $"{m.GenericId}:{m.Level}").OrderBy(x => x, StringComparer.Ordinal))
        + "/" + string.Join("|", (a.Monsters ?? Array.Empty<MonsterEntry>()).Select(m => $"{m.GenericId}x{m.Count}").OrderBy(x => x, StringComparer.Ordinal));

    public void Begin(MapEntity e)
    {
        if (e.Actor is not { Kind: MapActorKind.Monster } a) { Cancel(); return; }
        _key = e.Key; _cell = e.CellId; _signature = Signature(a);
    }

    public void Cancel() { _key = null; _signature = null; }

    /// <summary>Renvoie le groupe à attaquer si la cible est inchangée ; sinon null (et l'aperçu est annulé).</summary>
    public MapActor? Confirm(MapEntity? current)
    {
        bool same = _key is not null && current is { Actor: { Kind: MapActorKind.Monster } a } && current.Key == _key
                    && current.CellId == _cell && Signature(a) == _signature;
        var result = same ? current!.Actor : null;
        Cancel();
        return result;
    }

    /// <summary>La cible de l'aperçu a-t-elle changé ? (l'UI retire alors l'aperçu au lieu de laisser confirmer une cible périmée)</summary>
    public bool StillValid(MapEntity? current) =>
        _key is not null && current is { Actor: { Kind: MapActorKind.Monster } a } && current.Key == _key && current.CellId == _cell && Signature(a) == _signature;
}

/// <summary>Phase d'une action de service / PNJ, distinguées explicitement : rejoindre le bâtiment, ouverture demandée, ouverture confirmée
/// (réponse du jeu reçue), sans réponse, refusée. Aucune phase « réussie » n'est déduite d'un simple envoi.</summary>
public enum ServicePhase { None, Joining, Requested, Opened, NoAnswer, Refused }

public enum DialogSpeaker { Npc, You, System }
public sealed record DialogEntry(DialogSpeaker Who, string Text);

/// <summary>Historique COURT d'un dialogue PNJ (dernières lignes seulement). Une question identique reçue deux fois de suite n'est pas dupliquée.
/// Les lignes viennent uniquement d'événements reçus ou d'actions réellement envoyées par l'utilisateur.</summary>
public sealed class NpcDialogLog
{
    public const int Max = 8;
    private readonly List<DialogEntry> _items = new();
    public IReadOnlyList<DialogEntry> Items => _items;

    public void Add(DialogSpeaker who, string text)
    {
        if (string.IsNullOrWhiteSpace(text)) return;
        if (_items.Count > 0 && _items[^1].Who == who && _items[^1].Text == text) return;
        _items.Add(new DialogEntry(who, text));
        if (_items.Count > Max) _items.RemoveAt(0);
    }

    public void Clear() => _items.Clear();
}

/// <summary>Distinction personnage contrôlé / autre joueur, et ce qui est réellement affichable pour chacun (jamais de profil inventé).</summary>
public static class CharacterInspection
{
    public static bool IsControlled(MapEntity e) => e.Kind == MapEntityKind.Self;
    public static bool IsOtherPlayer(MapEntity e) => e.Kind == MapEntityKind.Player;

    /// <summary>Un autre joueur n'expose au bot que son nom, sa cellule et son identifiant : pas de métiers, d'équipement ni de statistiques.</summary>
    public static bool HasEquipment(MapEntity e) => false;   // aucun paquet d'équipement exploité : l'onglet Équipement n'existe pas
    public static bool CanShowStats(MapEntity e) => IsControlled(e);
}

/// <summary>Mode d'interaction de la carte : INSPECTION (un clic ouvre une fiche, ne déplace jamais) ou ACTION (un clic prévisualise le trajet,
/// un second clic sur la même cellule confirme le déplacement).</summary>
public enum MapInteractionMode { Inspect, Act }

public enum ActionState { None, Waiting, Confirmed, Refused, Unknown }

/// <summary>Retour d'action unifié : attente, confirmé, refusé, inconnu. « Confirmé » n'est posé que sur une preuve (position atteinte, réponse du
/// jeu) ; sans preuve dans le délai l'état devient « inconnu » — jamais « réussi » par défaut.</summary>
public sealed class MapActionTracker
{
    public ActionState State { get; private set; }
    public string Label { get; private set; } = "";
    private int _target = -1;
    private long _startedMs, _timeoutMs;
    private Func<bool>? _probe;

    /// <param name="target">cellule à atteindre (déplacement) ou -1 ; <paramref name="probe"/> = preuve externe de réussite (ex. combat lancé).</param>
    public void Begin(string label, int target, long nowMs, long timeoutMs = 8000, Func<bool>? probe = null)
    { Label = label; _target = target; _startedMs = nowMs; _timeoutMs = timeoutMs; _probe = probe; State = ActionState.Waiting; }

    public void Refuse(string label) { Label = label; State = ActionState.Refused; _probe = null; }
    public void Clear() { State = ActionState.None; Label = ""; _probe = null; }
    public void Confirm() { if (State == ActionState.Waiting) State = ActionState.Confirmed; }

    /// <summary>À appeler à chaque rafraîchissement : transforme « attente » en confirmé (preuve) ou inconnu (délai écoulé).</summary>
    public void Poll(int? selfCell, long nowMs)
    {
        if (State != ActionState.Waiting) return;
        if ((_target >= 0 && selfCell == _target) || (_probe is not null && SafeProbe())) { State = ActionState.Confirmed; return; }
        if (nowMs - _startedMs >= _timeoutMs) State = ActionState.Unknown;
    }

    private bool SafeProbe() { try { return _probe!(); } catch { return false; } }
}

/// <summary>Aperçu de trajet avant déplacement : chemin cellule par cellule calculé par le pathfinder existant ; vide si la cible est inatteignable.</summary>
public static class MapPathPreview
{
    public static IReadOnlyList<int> Compute(int source, int target, Func<int, bool> walkable)
    {
        if (source == target || (uint)source >= 560 || (uint)target >= 560 || !walkable(target)) return Array.Empty<int>();
        var key = MapPathfinder.KeyMovements(source, target, walkable, allowDiag: true);
        if (key.Count == 0) return Array.Empty<int>();
        var full = MapPathfinder.ExpandPath(key);
        return full.Count >= 2 ? full : Array.Empty<int>();
    }
}

/// <summary>Un lieu favori (service, PNJ, atelier) identifié par carte + cellule : les identifiants d'acteurs sont contextuels et ne survivent pas.</summary>
public sealed class MapFavorite
{
    public long MapId { get; set; }
    public int CellId { get; set; }
    public string Label { get; set; } = "";
    public string Kind { get; set; } = "";
}

public static class MapFavorites
{
    public static bool IsFavorite(IEnumerable<MapFavorite> all, long mapId, int cell) => all.Any(f => f.MapId == mapId && f.CellId == cell);

    /// <summary>Ajoute ou retire (bascule). Un même lieu n'est jamais enregistré deux fois ; la liste est bornée.</summary>
    public static List<MapFavorite> Toggle(IEnumerable<MapFavorite> all, long mapId, int cell, string label, string kind, int max = 60)
    {
        var list = all.ToList();
        int i = list.FindIndex(f => f.MapId == mapId && f.CellId == cell);
        if (i >= 0) { list.RemoveAt(i); return list; }
        if (mapId == 0) return list;   // carte inconnue : on n'enregistre rien d'ambigu
        list.Add(new MapFavorite { MapId = mapId, CellId = cell, Label = label, Kind = kind });
        if (list.Count > max) list.RemoveAt(0);
        return list;
    }
}

/// <summary>Plan de craft : ingrédients possédés / requis, quantité maximale réalisable et ce qui manque. Calcul pur sur les quantités connues du sac.</summary>
public sealed record CraftIngredientLine(long Gid, string Name, int PerCraft, int Have);
public sealed record StationCraftPlan(IReadOnlyList<CraftIngredientLine> Lines, int MaxCraftable)
{
    public bool CanCraft(int count) => count >= 1 && count <= MaxCraftable;
    public static StationCraftPlan Compute(IReadOnlyList<(long Gid, string Name, int Qty)> ingredients, Func<long, int> have)
    {
        var lines = ingredients.Select(i => new CraftIngredientLine(i.Gid, i.Name, i.Qty, Math.Max(0, have(i.Gid)))).ToList();
        int max = lines.Count == 0 || lines.Any(l => l.PerCraft <= 0) ? 0 : lines.Min(l => l.Have / l.PerCraft);
        return new StationCraftPlan(lines, max);
    }
}

public static class CraftDecomposition
{
    /// <summary>Décompose récursivement des ingrédients jusqu'aux objets NON craftables (comme le « tout décomposer » d'un outil d'HDV). Profondeur et
    /// taille bornées ; une boucle de recettes est coupée. <paramref name="recipeOf"/> : gid -> ingrédients de sa recette, ou null si non craftable.</summary>
    public static IReadOnlyList<(long Gid, string Name, int Qty)> Flatten(IReadOnlyList<(long Gid, string Name, int Qty)> ingredients,
        Func<long, IReadOnlyList<(long Gid, string Name, int Qty)>?> recipeOf, int maxDepth = 6)
    {
        var totals = new Dictionary<long, (string Name, int Qty)>();
        void Walk(IReadOnlyList<(long Gid, string Name, int Qty)> list, int mult, int depth, HashSet<long> path)
        {
            foreach (var (gid, name, qty) in list)
            {
                long q = (long)qty * mult;
                if (q <= 0 || q > 1_000_000) continue;
                var sub = depth < maxDepth && !path.Contains(gid) ? recipeOf(gid) : null;
                if (sub is { Count: > 0 }) { path.Add(gid); Walk(sub, (int)q, depth + 1, path); path.Remove(gid); }
                else totals[gid] = totals.TryGetValue(gid, out var t) ? (t.Name, (int)Math.Min(int.MaxValue, t.Qty + q)) : (name, (int)q);
            }
        }
        Walk(ingredients, 1, 0, new HashSet<long>());
        return totals.Select(kv => (kv.Key, kv.Value.Name, kv.Value.Qty)).OrderBy(x => x.Name, StringComparer.CurrentCultureIgnoreCase).ToList();
    }
}

public enum HarvestStop { None, Done, PodsFull, InFight, MapChanged, Cancelled, InteractionOpen }

/// <summary>Planification de « tout récolter » : cible suivante = la plus proche ressource encore DISPONIBLE du même type et pas déjà tentée ;
/// arrêts de sécurité évalués avant CHAQUE exemplaire (pods pleins, combat, changement de carte, interface ouverte, annulation).</summary>
public static class HarvestAllPlan
{
    public static MapEntity? NextTarget(IEnumerable<MapEntity> group, int? selfCell, ISet<string> tried)
    {
        var candidates = group.Where(e => e.Interactive is { Harvestable: true } && !tried.Contains(e.Key)).ToList();
        if (candidates.Count == 0) return null;
        return selfCell is int me
            ? candidates.OrderBy(e => MapPathfinder.CellDistance(me, e.CellId)).ThenBy(e => e.Key, StringComparer.Ordinal).First()
            : candidates.OrderBy(e => e.Key, StringComparer.Ordinal).First();
    }

    public static HarvestStop Check(bool cancelled, bool inFight, bool podsFull, bool mapChanged, bool interactionOpen) =>
        cancelled ? HarvestStop.Cancelled : mapChanged ? HarvestStop.MapChanged : inFight ? HarvestStop.InFight
        : podsFull ? HarvestStop.PodsFull : interactionOpen ? HarvestStop.InteractionOpen : HarvestStop.None;
}
