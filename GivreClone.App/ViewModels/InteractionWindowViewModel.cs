using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;
using GivreClone.Protocol;

namespace GivreClone.App.ViewModels;

/// <summary>Côté d'une ligne de la fenêtre d'interaction : décide de l'action de son bouton.</summary>
public enum ExchangeSide { Bag, Bank, MyTrade, TheirTrade, Shop }

/// <summary>Une ligne de la fenêtre d'interaction (pile du sac, du coffre, de l'échange ou objet de boutique).</summary>
public sealed class ExchangeItemRow
{
    public int TooltipPid => (int)Owner.PidValue;
    public InteractionWindowViewModel Owner { get; }
    public ExchangeSide Side { get; }
    public int Uid { get; }
    public long Gid { get; }
    public int Qty { get; }
    public long Price { get; }
    public Bitmap? Icon { get; }
    public bool HasIcon => Icon is not null;
    public string Name { get; }
    public string Detail { get; }
    public string ActionText { get; }
    public bool HasAction => ActionText.Length > 0;
    /// <summary>Catégorie du jeu (type d'objet) : filtre et tri de la fenêtre.</summary>
    public string Category { get; }
    /// <summary>Quantité de la case en grille (vide pour 1 et pour la boutique, comme dans le jeu).</summary>
    public string QtyBadge => Side == ExchangeSide.Shop || Qty <= 1 ? "" : Qty >= 100_000 ? $"{Qty / 1000}k" : Qty.ToString(CultureInfo.InvariantCulture);
    public bool HasQtyBadge => QtyBadge.Length > 0;
    /// <summary>Info-bulle de la case : nom, quantité ou prix, action d'un clic.</summary>
    public string Tip => HasAction ? $"{Name}\n{Detail}\n{Loc.Fmt("L.Iw.ClickTo", ActionText)}" : $"{Name}\n{Detail}";

    public ExchangeItemRow(InteractionWindowViewModel owner, ExchangeSide side, int uid, long gid, int qty, long price, string actionText)
    {
        Owner = owner; Side = side; Uid = uid; Gid = gid; Qty = qty; Price = price; ActionText = actionText;
        Name = Stores.Launch.ItemDisplayName(gid);
        Category = Stores.Launch.ItemCategory(gid) ?? "";
        Icon = Stores.InteractiveIcons.GetByIcon(Stores.Launch.ItemIconId(gid));
        Detail = side == ExchangeSide.Shop
            ? Loc.Fmt("L.Iw.ShopDetail", price.ToString("#,0", CultureInfo.InvariantCulture).Replace(",", " "), Stores.Launch.InventoryCount(owner.PidValue, gid))
            : "×" + qty.ToString(CultureInfo.InvariantCulture);
    }
}

/// <summary>Une destination du dialogue de téléportation ouvert (zaap, zaapi, zaap du havre-sac), comme une ligne de la fenêtre
/// du jeu : territoire, région, coordonnées, coût (rouge si les kamas ne suffisent pas), favori, point de sauvegarde.</summary>
public sealed partial class TeleportRow : ObservableObject
{
    public InteractionWindowViewModel Owner { get; }
    public long MapId { get; }
    public string Name { get; }
    public string Detail { get; }
    public string Region { get; }
    public string Coords { get; }
    public long Cost { get; }
    public string CostText { get; }
    public bool HasCost => Cost > 0;
    /// <summary>Coût payable avec les kamas du sac (inconnus = payable).</summary>
    public bool Affordable { get; }
    public bool TooExpensive => HasCost && !Affordable && !PriceUnknown;
    public bool PriceUnknown { get; }
    /// <summary>Zaap où le point de retour (respawn) est sauvegardé.</summary>
    public bool IsRespawn { get; }
    [ObservableProperty] public partial bool IsFavorite { get; set; }
    [ObservableProperty] public partial bool IsSelected { get; set; }

    public TeleportRow(InteractionWindowViewModel owner, long mapId, string name, string region, string coords, long cost, bool affordable, bool respawn, bool favorite, bool kamasKnown = true)
    {
        Owner = owner; MapId = mapId; Name = name; Region = region; Coords = coords; Cost = cost;
        Detail = string.Join(" · ", new[] { region, coords }.Where(x => x.Length > 0));
        CostText = cost.ToString("#,0", CultureInfo.InvariantCulture).Replace(",", " ") + " K";
        Affordable = kamasKnown && affordable;
        PriceUnknown = cost > 0 && !kamasKnown;
        IsRespawn = respawn; IsFavorite = favorite;
    }
}

/// <summary>
/// Fenêtre d'interaction d'UN bot, affichée sur la carte dès qu'un échange s'ouvre côté serveur — par l'app, un script ou le joueur
/// dans Dofus (MITM) : BANQUE (déposer / retirer / kamas), ÉCHANGE joueur (poser / retirer / kamas / prêt, demande reçue) et
/// BOUTIQUE PNJ (acheter / vendre). Les actions sont unitaires et non bloquantes ; l'état affiché vient toujours des accusés
/// serveur décodés (jamais d'une hypothèse locale). Mêmes chemins en MITM et en Socket.
/// </summary>
public sealed partial class InteractionWindowViewModel : ObservableObject
{
    private readonly Func<int> _pid;
    public InteractionWindowViewModel(Func<int> pid) => _pid = pid;
    internal uint PidValue => (uint)Math.Max(0, _pid());

    /// <summary>Acteurs de la carte (banquier, nom du partenaire d'échange).</summary>
    public Func<IReadOnlyList<MapActor>?>? ActorsProvider { get; set; }
    /// <summary>Vrai si un script tourne sur ce bot (les clics restent permis, un avertissement s'affiche).</summary>
    public Func<bool>? ScriptRunning { get; set; }

    [ObservableProperty] public partial ExchangeKind Kind { get; set; }
    public bool IsOpen => Kind != ExchangeKind.None;
    public bool IsBank => Kind == ExchangeKind.Bank;
    public bool IsTrade => Kind == ExchangeKind.Trade;
    public bool IsShop => Kind == ExchangeKind.Shop;
    public bool IsTeleport => Kind == ExchangeKind.Teleport;
    /// <summary>Banque, échange, boutique : les deux colonnes sac / autre côté (pas pour un zaap).</summary>
    public bool IsItemExchange => IsOpen && !IsTeleport;
    /// <summary>Rangée kamas + actions groupées : banque (dépôt / retrait) et échange (offre, valider).</summary>
    public bool HasKamasRow => IsBank || IsTrade;
    public bool IsVisible => IsOpen || HasPending;
    partial void OnKindChanged(ExchangeKind value)
    {
        OnPropertyChanged(nameof(IsOpen)); OnPropertyChanged(nameof(IsBank)); OnPropertyChanged(nameof(IsTrade));
        OnPropertyChanged(nameof(IsShop)); OnPropertyChanged(nameof(IsTeleport)); OnPropertyChanged(nameof(IsItemExchange)); OnPropertyChanged(nameof(HasKamasRow));
        OnPropertyChanged(nameof(IsVisible)); OnPropertyChanged(nameof(HasPending)); OnPropertyChanged(nameof(Title));
        OnPropertyChanged(nameof(RightTitle)); OnPropertyChanged(nameof(LeftTitle));
        Collapsed = false; Status = ""; Filter = ""; _sig = long.MinValue;
    }

    /// <summary>Fenêtre repliée (seule la barre de titre reste visible).</summary>
    [ObservableProperty] public partial bool Collapsed { get; set; }
    [RelayCommand] private void ToggleCollapsed() => Collapsed = !Collapsed;

    public string Title => Kind switch
    {
        ExchangeKind.Bank => Loc.T("L.Iw.Bank"),
        ExchangeKind.Trade => Loc.Fmt("L.Iw.TradeWith", PartnerName),
        ExchangeKind.Shop => ShopTitle,
        ExchangeKind.Teleport => TeleportTitle,
        _ => Loc.T("L.Iw.TradeRequest"),
    };
    public string LeftTitle => Loc.T("L.Iw.Bag");
    public string RightTitle => Kind switch
    {
        ExchangeKind.Bank => Loc.T("L.Iw.Chest"),
        ExchangeKind.Trade => Loc.T("L.Iw.MyOffer"),
        ExchangeKind.Shop => Loc.T("L.Iw.ForSale"),
        _ => "",
    };

    [ObservableProperty] public partial string PartnerName { get; set; } = "";
    partial void OnPartnerNameChanged(string value) => OnPropertyChanged(nameof(Title));
    [ObservableProperty] public partial string ShopTitle { get; set; } = "";
    partial void OnShopTitleChanged(string value) => OnPropertyChanged(nameof(Title));
    [ObservableProperty] public partial string TeleportTitle { get; set; } = "";
    partial void OnTeleportTitleChanged(string value) => OnPropertyChanged(nameof(Title));
    /// <summary>Destinations du zaap / zaapi ouvert (filtrées par la recherche), les plus proches de la carte courante d'abord.</summary>
    public ObservableCollection<TeleportRow> TeleportRows { get; } = new();

    /// <summary>Recherche par nom ou gid, appliquée aux deux colonnes.</summary>
    [ObservableProperty] public partial string Filter { get; set; } = "";
    partial void OnFilterChanged(string value) => _sig = long.MinValue;

    // ---- Téléportation (comme la fenêtre du jeu) : tri, favoris, sélection puis « Se téléporter », sauvegarde du zaap ----
    /// <summary>0 distance, 1 nom, 2 coût, 3 région.</summary>
    [ObservableProperty] public partial int TeleportSortIndex { get; set; }
    partial void OnTeleportSortIndexChanged(int value) => _sig = long.MinValue;
    [ObservableProperty] public partial bool FavoritesOnly { get; set; }
    partial void OnFavoritesOnlyChanged(bool value) => _sig = long.MinValue;
    [ObservableProperty] public partial TeleportRow? SelectedTeleport { get; set; }
    public bool HasSelectedTeleport => SelectedTeleport is not null;
    [ObservableProperty] public partial bool TeleportReadOnly { get; set; }
    public bool CanExecuteTeleport => HasSelectedTeleport && !TeleportReadOnly;
    public string TeleportReadOnlyHint => TeleportReadOnly
        ? "Anomalie temporelle : destinations consultables uniquement. Le protocole de téléportation et son accusé serveur ne sont pas encore vérifiés."
        : "";
    partial void OnTeleportReadOnlyChanged(bool value)
    {
        OnPropertyChanged(nameof(CanExecuteTeleport));
        OnPropertyChanged(nameof(TeleportReadOnlyHint));
    }
    partial void OnSelectedTeleportChanged(TeleportRow? oldValue, TeleportRow? newValue)
    {
        if (oldValue is not null) oldValue.IsSelected = false;
        if (newValue is not null) newValue.IsSelected = true;
        OnPropertyChanged(nameof(HasSelectedTeleport));
        OnPropertyChanged(nameof(CanExecuteTeleport));
    }
    /// <summary>« Sauvegarder le zaap » : seulement sur un vrai zaap (pas zaapi / havre-sac / prisme).</summary>
    [ObservableProperty] public partial bool CanSaveZaap { get; set; }
    [ObservableProperty] public partial string RespawnText { get; set; } = "";
    public bool HasRespawn => RespawnText.Length > 0;
    partial void OnRespawnTextChanged(string value) => OnPropertyChanged(nameof(HasRespawn));

    // ---- Banque / échange : catégorie, tri, objets visibles ----
    public ObservableCollection<string> Categories { get; } = new();
    [ObservableProperty] public partial string? Category { get; set; }
    partial void OnCategoryChanged(string? value) => _sig = long.MinValue;
    /// <summary>0 aucun tri, 1 nom, 2 quantité, 3 catégorie.</summary>
    [ObservableProperty] public partial int ItemSortIndex { get; set; }
    partial void OnItemSortIndexChanged(int value) => _sig = long.MinValue;
    [ObservableProperty] public partial string LeftVisibleText { get; set; } = "";
    [ObservableProperty] public partial string RightVisibleText { get; set; } = "";
    /// <summary>Objets en grille d'icônes (comme le jeu) ou en liste ; choix mémorisé.</summary>
    [ObservableProperty] public partial bool GridView { get; set; } = AppConfig.Current.InteractionGrid;
    public bool ListView => !GridView;
    partial void OnGridViewChanged(bool value) { OnPropertyChanged(nameof(ListView)); AppConfig.Current.InteractionGrid = value; AppConfig.Save(); }
    [RelayCommand] private void ToggleGridView() => GridView = !GridView;
    /// <summary>Quantité par clic (une pile ne donne jamais plus que ce qu'elle contient).</summary>
    [ObservableProperty] public partial int Quantity { get; set; } = 1;
    /// <summary>Coché = la pile entière à chaque clic (ignore Quantité).</summary>
    [ObservableProperty] public partial bool WholeStack { get; set; }
    private int EffectiveQty => WholeStack ? 0 : Math.Max(1, Quantity);

    public ObservableCollection<ExchangeItemRow> LeftRows { get; } = new();
    public ObservableCollection<ExchangeItemRow> RightRows { get; } = new();
    public ObservableCollection<ExchangeItemRow> TheirRows { get; } = new();

    [ObservableProperty] public partial string BagKamasText { get; set; } = "—";
    [ObservableProperty] public partial string RightKamasText { get; set; } = "—";
    [ObservableProperty] public partial string TheirKamasText { get; set; } = "0";
    /// <summary>Montant saisi pour les kamas (dépôt / retrait banque, ou total posé dans l'échange).</summary>
    [ObservableProperty] public partial long KamasAmount { get; set; }

    [ObservableProperty] public partial bool MeReady { get; set; }
    [ObservableProperty] public partial bool TheyReady { get; set; }
    public string ReadyText => MeReady ? Loc.T("L.Iw.Unready") : Loc.T("L.Iw.Ready");
    partial void OnMeReadyChanged(bool value) => OnPropertyChanged(nameof(ReadyText));

    // Demande d'échange REÇUE (affichée même sans échange ouvert : Accepter / Refuser).
    [ObservableProperty] public partial string PendingText { get; set; } = "";
    public bool HasPending => PendingText.Length > 0 && !IsOpen;
    partial void OnPendingTextChanged(string value) { OnPropertyChanged(nameof(HasPending)); OnPropertyChanged(nameof(IsVisible)); OnPropertyChanged(nameof(Title)); }

    [ObservableProperty] public partial string Status { get; set; } = "";
    public bool HasStatus => Status.Length > 0;
    partial void OnStatusChanged(string value) => OnPropertyChanged(nameof(HasStatus));
    [ObservableProperty] public partial bool ShowScriptWarning { get; set; }
    [ObservableProperty] public partial bool Busy { get; set; }

    private long _sig = long.MinValue;

    /// <summary>Relit l'état serveur (appelé au tick de l'UI pour le bot affiché). Reconstruit les listes seulement si elles changent.</summary>
    public void Poll()
    {
        uint pid = PidValue;
        if (pid == 0) { Kind = ExchangeKind.None; PendingText = ""; return; }
        var launch = Stores.Launch;
        Kind = launch.CurrentExchange(pid);
        ShowScriptWarning = IsOpen && ScriptRunning?.Invoke() == true;
        PendingText = !IsOpen && launch.TradePending(pid) is { } pend ? Loc.Fmt("L.Iw.PendingFrom", ActorName(pend.SourceId)) : "";
        if (!IsOpen)
        {
            if (LeftRows.Count + RightRows.Count + TheirRows.Count + TeleportRows.Count > 0) { LeftRows.Clear(); RightRows.Clear(); TheirRows.Clear(); TeleportRows.Clear(); }
            return;
        }
        if (IsTeleport) { PollTeleport(pid); return; }

        long kamas = launch.Kamas(pid);
        BagKamasText = kamas < 0 ? "—" : Fmt(kamas);
        var bag = launch.Inventory.Items(pid).Where(i => i.Position == 63 && i.Qty > 0).ToList();
        IReadOnlyList<(int Uid, long Gid, int Qty, long Price)> right, theirs = Array.Empty<(int, long, int, long)>();
        switch (Kind)
        {
            case ExchangeKind.Bank:
                right = launch.Bank.Items(pid).Where(i => i.Qty > 0).Select(i => (i.Uid, i.Gid, i.Qty, 0L)).ToList();
                long bk = launch.BankKamas(pid);
                RightKamasText = bk < 0 ? "—" : Fmt(bk);
                break;
            case ExchangeKind.Trade:
            {
                var c = launch.TradeView(pid);
                right = c?.Mine.Select(x => (x.Uid, x.Gid, x.Qty, 0L)).ToList() ?? new List<(int, long, int, long)>();
                theirs = c?.Theirs.Select(x => (x.Uid, x.Gid, x.Qty, 0L)).ToList() ?? new List<(int, long, int, long)>();
                RightKamasText = Fmt(c?.MyKamas ?? 0);
                TheirKamasText = Fmt(c?.TheirKamas ?? 0);
                MeReady = c?.MeReady ?? false;
                TheyReady = c?.TheyReady ?? false;
                PartnerName = ActorName(launch.TradePartner(pid));
                break;
            }
            default:
            {
                var (npc, items) = launch.ShopView(pid);
                right = items.Select(i => (0, (long)i.Gid, 0, i.Price)).ToList();
                ShopTitle = npc > 0 && launch.GameData?.NpcName((int)npc) is { Length: > 0 } n ? n : Loc.T("L.Iw.Shop");
                RightKamasText = "";
                break;
            }
        }

        long sig = HashCode.Combine((int)Kind, Filter, Stores.InteractiveIcons.IsLoaded, Category, ItemSortIndex);
        foreach (var i in bag) sig = unchecked(sig * 1000003 + i.Uid * 31L + i.Qty);
        foreach (var r in right) sig = unchecked(sig * 1000003 + r.Uid * 31L + r.Gid * 7 + r.Qty + r.Price);
        foreach (var r in theirs) sig = unchecked(sig * 1000003 + r.Uid * 17L + r.Qty);
        if (sig == _sig) return;
        _sig = sig;

        string bagAction = Kind switch { ExchangeKind.Bank => Loc.T("L.Iw.Deposit"), ExchangeKind.Trade => Loc.T("L.Iw.Put"), _ => Loc.T("L.Iw.Sell") };
        var leftAll = bag.Select(i => new ExchangeItemRow(this, ExchangeSide.Bag, i.Uid, i.Gid, i.Qty, 0, bagAction)).ToList();
        var rightSide = Kind switch { ExchangeKind.Bank => ExchangeSide.Bank, ExchangeKind.Trade => ExchangeSide.MyTrade, _ => ExchangeSide.Shop };
        string rightAction = Kind switch { ExchangeKind.Bank => Loc.T("L.Iw.Withdraw"), ExchangeKind.Trade => Loc.T("L.Iw.TakeBack"), _ => Loc.T("L.Iw.Buy") };
        var rightAll = right.Select(r => new ExchangeItemRow(this, rightSide, r.Uid, r.Gid, r.Qty, r.Price, rightAction)).ToList();
        RefreshCategories(leftAll.Concat(rightAll).Select(r => r.Category));
        Fill(LeftRows, leftAll);
        Fill(RightRows, rightAll);
        Fill(TheirRows, theirs.Select(r => new ExchangeItemRow(this, ExchangeSide.TheirTrade, r.Uid, r.Gid, r.Qty, 0, "")));
        LeftVisibleText = Loc.Fmt("L.Iw.Visible", LeftRows.Count, leftAll.Count);
        RightVisibleText = Loc.Fmt("L.Iw.Visible", RightRows.Count, rightAll.Count);
    }

    /// <summary>Catégories présentes (index 0 = toutes) ; la catégorie choisie reste tant qu'elle existe.</summary>
    private void RefreshCategories(IEnumerable<string> cats)
    {
        var list = new List<string> { Loc.T("L.Iw.AllCategories") };
        list.AddRange(cats.Where(c => c.Length > 0).Distinct().OrderBy(c => c, StringComparer.CurrentCultureIgnoreCase));
        if (list.SequenceEqual(Categories)) return;
        string? keep = Category;
        Categories.Clear();
        foreach (var c in list) Categories.Add(c);
        Category = keep is not null && Categories.Contains(keep) ? keep : Categories[0];
    }

    // Dialogue de téléportation : noms lisibles (sous-zone, zone, coordonnées), coût, favoris en tête, tri choisi.
    private void PollTeleport(uint pid)
    {
        var launch = Stores.Launch;
        if (launch.TeleportView(pid) is not { } t) return;
        TeleportTitle = t.Type == 4 ? "Anomalie temporelle"
            : Loc.T(t.Type switch { 1 => "L.Iw.Zaapi", 3 => "L.Iw.HavenBagZaap", 2 => "L.Iw.Prism", _ => "L.Iw.Zaap" });
        TeleportReadOnly = t.Type == 4 || t.Type < 0 || t.Type > 3;
        CanSaveZaap = t.Type == 0;
        long kamas = launch.Kamas(pid);
        BagKamasText = kamas < 0 ? "—" : Fmt(kamas);
        long respawn = launch.RespawnMap(pid);
        var favs = FavoriteZaaps(pid);
        long sig = HashCode.Combine(t.Destinations.Count, t.OpenedOnMap, Filter, t.Type, TeleportSortIndex, FavoritesOnly, kamas, respawn);
        sig = HashCode.Combine(sig, favs.Count, favs.Sum());
        if (sig == _sig) return;
        _sig = sig;
        var here = launch.CurrentMap(pid) is long cm ? launch.MapCoordOf(cm) : null;
        var db = launch.GameData;
        string f = PlaceTextNormalize(Filter);
        var rows = new List<(TeleportRow Row, int Dist)>();
        foreach (var d in t.Destinations)
        {
            int sub = d.SubAreaId > 0 ? d.SubAreaId : launch.SubAreaOf(d.MapId);
            GivreClone.App.Services.LotM.SubAreaInfo? info = null;
            try { info = sub > 0 ? db?.SubArea(sub) : null; } catch { info = null; }
            var pos = launch.MapCoordOf(d.MapId);
            string coords = pos is (int x, int y) ? $"{x},{y}" : "";
            string name = info?.Name is { Length: > 0 } n ? n : (coords.Length > 0 ? coords : d.MapId.ToString(CultureInfo.InvariantCulture));
            string region = info?.Area ?? "";
            if (f.Length > 0 && !PlaceTextNormalize(name + " " + region + " " + coords).Contains(f, StringComparison.Ordinal)) continue;
            bool fav = favs.Contains(d.MapId);
            if (FavoritesOnly && !fav) continue;
            int dist = here is (int hx, int hy) && pos is (int px, int py) ? Math.Abs(hx - px) + Math.Abs(hy - py) : int.MaxValue;
            rows.Add((new TeleportRow(this, d.MapId, name, region, coords, d.Cost,
                kamas >= 0 && d.Cost <= kamas, respawn > 0 && d.MapId == respawn, fav, kamas >= 0), dist));
        }
        var cmp = StringComparer.CurrentCultureIgnoreCase;
        var ordered = rows.OrderByDescending(r => r.Row.IsFavorite);
        ordered = TeleportSortIndex switch
        {
            1 => ordered.ThenBy(r => r.Row.Name, cmp),
            2 => ordered.ThenBy(r => r.Row.Cost).ThenBy(r => r.Row.Name, cmp),
            3 => ordered.ThenBy(r => r.Row.Region, cmp).ThenBy(r => r.Row.Name, cmp),
            _ => ordered.ThenBy(r => r.Dist).ThenBy(r => r.Row.Name, cmp),
        };
        long keepSel = SelectedTeleport?.MapId ?? 0;
        TeleportRows.Clear();
        foreach (var r in ordered) TeleportRows.Add(r.Row);
        SelectedTeleport = TeleportRows.FirstOrDefault(r => r.MapId == keepSel);
        RespawnText = respawn > 0 ? Loc.Fmt("L.Iw.RespawnAt", PlaceName(respawn)) : "";
    }

    private static string PlaceName(long mapId)
    {
        var l = Stores.Launch;
        int sub = l.SubAreaOf(mapId);
        try { if (sub > 0 && l.GameData?.SubArea(sub) is { Name.Length: > 0 } s) return s.Name; } catch { }
        return l.MapCoordOf(mapId) is (int x, int y) ? $"{x},{y}" : mapId.ToString(CultureInfo.InvariantCulture);
    }

    // Favoris de zaaps, par perso (id du personnage), gardés dans les réglages.
    private string FavoriteKey(uint pid) => Stores.Launch.CharacterId(pid).ToString(CultureInfo.InvariantCulture);
    private HashSet<long> FavoriteZaaps(uint pid)
        => AppConfig.Current.ZaapFavorites.TryGetValue(FavoriteKey(pid), out var l) ? l.ToHashSet() : new HashSet<long>();

    [RelayCommand]
    private void ToggleFavorite(TeleportRow? row)
    {
        if (row is null) return;
        uint pid = PidValue;
        if (Stores.Launch.CharacterId(pid) == 0) return;
        var map = AppConfig.Current.ZaapFavorites;
        string key = FavoriteKey(pid);
        if (!map.TryGetValue(key, out var list)) map[key] = list = new List<long>();
        if (!list.Remove(row.MapId)) list.Add(row.MapId);
        row.IsFavorite = list.Contains(row.MapId);
        AppConfig.Save();
        _sig = long.MinValue;
    }

    // Double-clic (deux clics sur la même ligne en moins de 450 ms) : téléportation immédiate, comme dans le jeu.
    private long _lastTeleportClickMap, _lastTeleportClickAt;
    [RelayCommand]
    private void SelectTeleport(TeleportRow? row)
    {
        if (row is null) return;
        long now = Environment.TickCount64;
        bool doubleClick = row.MapId == _lastTeleportClickMap && now - _lastTeleportClickAt < 450;
        _lastTeleportClickMap = doubleClick ? 0 : row.MapId;
        _lastTeleportClickAt = now;
        SelectedTeleport = row;
        if (doubleClick && CanExecuteTeleport) TeleportTo(row);
    }

    [RelayCommand] private void TeleportSelected() => TeleportTo(SelectedTeleport);

    /// <summary>« Sauvegarder le zaap » : point de retour sur le zaap ouvert (confirmé par le serveur, affiché en bas).</summary>
    [RelayCommand]
    private void SaveZaap() => Report(CanSaveZaap && Stores.Launch.ZaapRespawnSave(PidValue), Loc.T("L.Iw.SaveZaap"));

    private static string PlaceTextNormalize(string s) => GivreClone.App.Services.LotM.PlaceText.Normalize(s ?? "");

    /// <summary>Clic sur une destination : téléportation (le dialogue se ferme tout seul à l'arrivée).</summary>
    [RelayCommand]
    private void TeleportTo(TeleportRow? row)
    {
        if (row is null) return;
        if (TeleportReadOnly)
        {
            Status = TeleportReadOnlyHint;
            return;
        }
        uint pid = PidValue;
        if (Stores.Launch.Kamas(pid) is long k && k >= 0 && row.Cost > k) { Status = Loc.Fmt("L.Iw.Refused", Loc.T("L.Iw.Reason.Kamas")); return; }
        Report(Stores.Launch.TeleportTo(pid, row.MapId), Loc.Fmt("L.Iw.TeleportTo", row.Name));
    }

    private void Fill(ObservableCollection<ExchangeItemRow> target, IEnumerable<ExchangeItemRow> rows)
    {
        target.Clear();
        foreach (var r in Visible(rows)) target.Add(r);
    }

    /// <summary>Lignes visibles : recherche (nom ou gid), catégorie, puis tri choisi.</summary>
    private IEnumerable<ExchangeItemRow> Visible(IEnumerable<ExchangeItemRow> rows)
    {
        string f = Filter.Trim();
        bool allCats = string.IsNullOrEmpty(Category) || Categories.Count == 0 || Category == Categories[0];
        var shown = rows.Where(r => (f.Length == 0 || r.Name.Contains(f, StringComparison.CurrentCultureIgnoreCase)
                                     || r.Gid.ToString(CultureInfo.InvariantCulture).Contains(f, StringComparison.Ordinal))
                                    && (allCats || r.Category == Category));
        return ItemSortIndex switch
        {
            1 => shown.OrderBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase),
            2 => shown.OrderByDescending(r => r.Qty).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase),
            3 => shown.OrderBy(r => r.Category, StringComparer.CurrentCultureIgnoreCase).ThenBy(r => r.Name, StringComparer.CurrentCultureIgnoreCase),
            _ => shown,
        };
    }

    private string ActorName(long id)
        => ActorsProvider?.Invoke()?.FirstOrDefault(a => a.ActorId == id && a.Kind == MapActorKind.Player)?.Name is { Length: > 0 } n
            ? n : Loc.Fmt("L.Iw.PlayerN", id);

    private static string Fmt(long k) => k.ToString("#,0", CultureInfo.InvariantCulture).Replace(",", " ");

    private void Report(bool ok, string label)
    {
        Status = ok ? Loc.Fmt("L.Iw.Sent", label) : Loc.Fmt("L.Iw.Refused", label);
        _sig = long.MinValue;   // l'accusé serveur arrive au prochain tick
    }

    /// <summary>Bouton d'une ligne : déposer / retirer (banque), poser / retirer (échange), acheter / vendre (boutique).</summary>
    [RelayCommand]
    private async Task Act(ExchangeItemRow? row)
    {
        if (row is null || Busy) return;
        uint pid = PidValue;
        var l = Stores.Launch;
        int q = EffectiveQty;
        switch (Kind, row.Side)
        {
            case (ExchangeKind.Bank, ExchangeSide.Bag): Report(l.BankDepositUid(pid, row.Uid, q), $"{row.ActionText} {row.Name}"); break;
            case (ExchangeKind.Bank, ExchangeSide.Bank): Report(l.BankWithdrawUid(pid, row.Uid, q), $"{row.ActionText} {row.Name}"); break;
            case (ExchangeKind.Trade, ExchangeSide.Bag): Report(l.TradePutUid(pid, row.Uid, q), $"{row.ActionText} {row.Name}"); break;
            case (ExchangeKind.Trade, ExchangeSide.MyTrade): Report(l.TradeTakeBackUid(pid, row.Uid, q), $"{row.ActionText} {row.Name}"); break;
            case (ExchangeKind.Shop, ExchangeSide.Shop):
            case (ExchangeKind.Shop, ExchangeSide.Bag):
            {
                bool buy = row.Side == ExchangeSide.Shop;
                int want = buy ? Math.Max(1, Quantity) : InteractionLogic.ClampQty(q, l.InventoryCount(pid, row.Gid));
                if (want <= 0) return;
                Busy = true;
                Status = Loc.Fmt("L.Iw.InProgress", $"{row.ActionText} {want}× {row.Name}");
                try
                {
                    var r = buy ? await l.NpcBuyAsync(pid, (int)row.Gid, want, default).ConfigureAwait(true)
                                : await l.NpcSellAsync(pid, (int)row.Gid, want, default).ConfigureAwait(true);
                    Status = r.Ok ? Loc.Fmt("L.Iw.ShopDone", row.ActionText, r.Done, row.Name)
                                  : Loc.Fmt("L.Iw.ShopFailed", row.ActionText, r.Done, want, row.Name, ShopReason(r.Reason));
                }
                catch (Exception e) { Status = Loc.Fmt("L.Iw.Refused", e.Message); }
                finally { Busy = false; _sig = long.MinValue; }
                break;
            }
        }
    }

    private static string ShopReason(string code) => code switch
    {
        "kamas" => Loc.T("L.Iw.Reason.Kamas"),
        "noitem" => Loc.T("L.Iw.Reason.NoItem"),
        "noperm" => Loc.T("L.Iw.Reason.NoPerm"),
        "timeout" => Loc.T("L.Iw.Reason.Timeout"),
        _ => Loc.T("L.Iw.Reason.Refused"),
    };

    [RelayCommand] private void DepositAll() => Report(Stores.Launch.BankDepositConfigured(PidValue), Loc.T("L.Iw.DepositAll"));

    // ---- Transferts groupés de la banque, comme le menu du jeu : tous les objets, les objets visibles (recherche / catégorie),
    // les objets déjà présents de l'autre côté. Visibles du coffre : une pile après l'autre (pas de message de liste fiable).
    [RelayCommand] private void TransferBagAll() => Report(Stores.Launch.BankDepositAll(PidValue), Loc.T("L.Iw.TransferBagAll"));
    [RelayCommand] private void TransferBagExisting() => Report(Stores.Launch.BankDepositExisting(PidValue), Loc.T("L.Iw.TransferBagExisting"));
    [RelayCommand]
    private void TransferBagVisible()
        => Report(Stores.Launch.BankDepositList(PidValue, LeftRows.Select(r => r.Uid).Where(u => u != 0).ToList()), Loc.Fmt("L.Iw.TransferBagVisible", LeftRows.Count));
    [RelayCommand] private void TransferBankAll() => Report(Stores.Launch.BankWithdrawEverything(PidValue), Loc.T("L.Iw.TransferBankAll"));
    [RelayCommand] private void TransferBankExisting() => Report(Stores.Launch.BankWithdrawExisting(PidValue), Loc.T("L.Iw.TransferBankExisting"));
    [RelayCommand]
    private async Task TransferBankVisible()
    {
        if (Busy || !IsBank) return;
        var uids = RightRows.Select(r => r.Uid).Where(u => u != 0).ToList();
        if (uids.Count == 0) return;
        uint pid = PidValue;
        Busy = true;
        int done = 0;
        try
        {
            foreach (int uid in uids)
            {
                if (!Stores.Launch.StorageOpen(pid)) break;
                if (Stores.Launch.BankWithdrawUid(pid, uid, 0)) done++;
                Status = Loc.Fmt("L.Iw.InProgress", Loc.Fmt("L.Iw.TransferBankVisible", $"{done}/{uids.Count}"));
                await Task.Delay(Random.Shared.Next(140, 260)).ConfigureAwait(true);   // rythme humain, comme un clic par pile
            }
            Status = Loc.Fmt("L.Iw.Sent", Loc.Fmt("L.Iw.TransferBankVisible", $"{done}/{uids.Count}"));
        }
        finally { Busy = false; _sig = long.MinValue; }
    }
    [RelayCommand] private void WithdrawAll() => Report(Stores.Launch.BankWithdrawEverything(PidValue), Loc.T("L.Iw.WithdrawAll"));
    [RelayCommand] private void KamasIn() => Report(KamasAmount > 0 && Stores.Launch.BankMoveKamas(PidValue, KamasAmount), Loc.Fmt("L.Iw.KamasIn", KamasAmount));
    [RelayCommand] private void KamasOut() => Report(KamasAmount > 0 && Stores.Launch.BankMoveKamas(PidValue, -KamasAmount), Loc.Fmt("L.Iw.KamasOut", KamasAmount));
    [RelayCommand] private void TradeKamas() => Report(Stores.Launch.TradeSetKamas(PidValue, KamasAmount), Loc.Fmt("L.Iw.KamasOffer", KamasAmount));
    [RelayCommand] private void ToggleReady() => Report(Stores.Launch.TradeSetReady(PidValue, !MeReady), ReadyText);
    [RelayCommand] private void AcceptRequest() => Report(Stores.Launch.TradeAccept(PidValue), Loc.T("L.Iw.Accept"));
    [RelayCommand] private void RefuseRequest() { Report(Stores.Launch.TradeRefuse(PidValue), Loc.T("L.Iw.Refuse")); PendingText = ""; }
    [RelayCommand] private void Close() => Report(Stores.Launch.LeaveDialog(PidValue), Loc.T("L.Iw.Close"));

    /// <summary>Ouvre la banque depuis l'app (banquier de la carte). Le coffre s'affiche dès que le serveur l'a envoyé.</summary>
    [RelayCommand]
    private async Task OpenBank()
    {
        if (Busy) return;
        Busy = true;
        Status = Loc.Fmt("L.Iw.InProgress", Loc.T("L.Iw.OpenBank"));
        try
        {
            bool ok = await Stores.Launch.BankOpenHereAsync(PidValue, ActorsProvider?.Invoke()).ConfigureAwait(true);
            Status = ok ? "" : Loc.T("L.Iw.OpenBankFailed");
        }
        catch (Exception e) { Status = Loc.Fmt("L.Iw.Refused", e.Message); }
        finally { Busy = false; _sig = long.MinValue; Poll(); }
    }

    /// <summary>Propose un échange au joueur de la carte (id d'acteur). La fenêtre s'ouvre si le joueur accepte.</summary>
    public bool RequestTrade(long actorId)
    {
        bool ok = Stores.Launch.TradeWithPlayer(PidValue, actorId);
        Status = ok ? Loc.Fmt("L.Iw.TradeAsked", ActorName(actorId)) : Loc.Fmt("L.Iw.Refused", Loc.T("L.Iw.TradeRequest"));
        return ok;
    }
}
