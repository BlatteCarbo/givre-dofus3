using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.App.Services.LotM;
using GivreClone.Protocol;

namespace GivreClone.App.Services;

/// <summary>Adaptateur <see cref="ILotMGame"/> d'UN bot sur LaunchService + magasin du monde. Aucune valeur inventée : inconnu = -1 / null.</summary>
public sealed class LotMGame : ILotMGame
{
    private readonly LaunchService _l;
    private readonly uint _pid;
    private readonly BotWorldStore _world;

    public LotMGame(LaunchService launch, uint pid, BotWorldStore world) => (_l, _pid, _world) = (launch, pid, world);

    private LaunchService.LotMState St => _l.LotMStateOf(_pid);

    public GameDb? Db => _l.GameData;
    public DateTime UtcNow => DateTime.UtcNow;
    public string GameVersion => _l.LotMGameVersion;
    public bool CanAct => LicenseService.HasPermission(LicenseService.Perm.Gather);
    public bool HasHdv => LicenseService.HasPermission(LicenseService.Perm.Hdv);

    public long CharacterId => _l.CharacterId(_pid);
    public CharacterSheetData? Sheet()
    {
        if (_l.SelfCharacterInfo(_pid) is not { } c) return null;
        var life = _l.SelfLife(_pid); var en = _l.SelfEnergy(_pid);
        // Expérience déjà décodée de la fiche serveur ; -1 tant qu'inconnue.
        // La sélection garde le niveau d'entrée ; CharacterLevel suit aussi les gains de niveau en session.
        int level = _l.CharacterLevel(_pid);
        long xp = _l.CharacterXp(_pid), next = _l.CharacterXpNextLevel(_pid);
        return new CharacterSheetData(CharacterId, c.Name ?? "", c.Breed ?? "", level > 0 ? level : c.Level ?? -1, xp, xp >= 0 && next >= xp ? next - xp : -1,
            life?.Cur ?? -1, life?.Max ?? -1, en?.Cur ?? -1, en?.Max ?? -1, _l.Kamas(_pid));
    }
    public SheetExtras SheetMore()
    {
        var db = _l.GameData;
        var spells = _l.LearnedSpells(_pid).Select(id => (id, _l.GameNames.SpellName(id) ?? "", _l.LearnedSpellLevel(_pid, id))).ToList();
        var jobs = _l.JobLevels(_pid).OrderBy(k => k.Key).Select(k => (k.Key, db?.Jobs().FirstOrDefault(j => j.Id == k.Key)?.Name ?? "", k.Value)).ToList();
        var equipped = _l.Inventory.Items(_pid).Where(i => i.Position != 63 && i.Qty > 0).OrderBy(i => i.Position)
            .Select(i => (i.Position, (int)i.Gid, db?.ItemName((int)i.Gid) ?? _l.ItemDisplayName(i.Gid), _l.ItemLevel((int)i.Gid))).ToList();
        return new SheetExtras(spells, jobs, equipped, _l.StatPointsLeft(_pid));
    }
    public long CurrentMapId => _l.CurrentMap(_pid) ?? 0;
    public PlaceOrigin? PlaceOrigin => _l.MapCoordOf(CurrentMapId) is (int x, int y) ? new PlaceOrigin(x, y, _l.WorldMapOf(CurrentMapId) ?? 1) : null;
    public IReadOnlyList<PlaceDoor> PlaceDoors(long mapId) => _l.WorldPlaceDoors(mapId);
    public bool InFight => _l.IsInFight(_pid);
    public int ServerId => _l.ServerIdOf(_pid);
    public int JobLevel(int jobId) => _l.JobLevel(_pid, jobId);
    public int ItemCount(long gid) => _l.Inventory.CountByGid(_pid, gid) + Math.Max(0, _l.Bank.CountByGid(_pid, gid));

    public AchievementLedger Achievements => St.Ledger;
    public Task<bool> ClaimAchievementAsync(int id, CancellationToken ct) => _l.AchievementClaimAsync(_pid, id, ct);
    public bool AutoClaim { get => _l.AutoClaimOn(_pid); set => St.AutoClaim = value; }

    public IReadOnlyList<AnomalyState> Anomalies() => St.Anomalies.Values.OrderBy(a => a.SubareaId).ToList();

    public OfferState? CurrentOffer() => St.Offer;
    public Task<OfferState?> WaitOfferAsync(int ms, CancellationToken ct) => _l.TeleportWaitOfferAsync(_pid, ms, ct);
    public bool AnswerOffer(bool accept) => _l.TeleportAnswer(_pid, accept);

    public Task<(bool Ok, string Reason, long MapId)> ExitDungeonAsync(CancellationToken ct) => _l.DungeonExitAsync(_pid, ct);
    public int OpenDocumentId() => St.DocumentId;
    public bool CloseDocument() { if (St.DocumentId == 0) return false; bool ok = _l.LeaveDialog(_pid); if (ok) St.DocumentId = 0; return ok; }

    public int BankKnownCount(long gid) => Math.Max(0, _l.Bank.CountByGid(_pid, gid));
    public Task<IReadOnlyList<LotMReader.PricePoint>?> PriceHistoryAsync(long gid, CancellationToken ct) => _l.PriceHistoryAsync(_pid, gid, ct);

    public async Task<(int Placed, string? Reason)> KeepOnSaleAsync(long gid, IReadOnlyList<KeepSize> sizes, long undercut, long? defaultPrice, long? defaultMin, CancellationToken ct)
    {
        if (!HasHdv) return (0, "droit HDV absent");
        if (!_l.HdvSellerOpen(_pid)) return (0, "hôtel de vente (vendeur) non ouvert");
        var wanted = sizes.Select(s => s with { Price = s.Price ?? defaultPrice, MinPrice = s.MinPrice ?? defaultMin }).ToList();
        await _l.HdvGetMarketPriceAsync(_pid, gid, ct).ConfigureAwait(false);   // rafraîchit le marché (prix mini par lot) et la moyenne
        long avg = Math.Max(0, _l.Hdv.AverageFor(_pid, gid));
        IEnumerable<BidHouseStore.Listing> Mine(int size) => _l.Hdv.MyListings(_pid).Where(x => x.Gid == gid && x.Qty == size);
        var plan = KeepOnSalePlanner.Plan(wanted, undercut, new KeepMarket(avg,
            size => _l.Hdv.MarketFor(_pid, gid, BidHouseStore.SlotOf(size)),
            size => Mine(size).Select(x => x.Price).DefaultIfEmpty(0).Min(),
            size => Mine(size).Count()));
        int placed = 0;
        foreach (var step in plan.Steps)
        {
            ct.ThrowIfCancellationRequested();
            // LotM keep-on-sale is an actual sale: honor the same persistent
            // tax intent and profile cap as the classic HDV and Lua paths.
            if (!await _l.HdvScriptSellByGidAsync(_pid, gid, step.Size, step.Price, ct).ConfigureAwait(false))
                return (placed, placed == 0 ? "mise en vente refusée (objet introuvable, hôtel plein ou prix rejeté)" : "arrêt : une mise en vente a été refusée");
            placed++;
        }
        return (placed, plan.Reason);
    }

    public IReadOnlyCollection<int> FinishedQuests() => _l.FinishedQuests(_pid);
    public IReadOnlyCollection<int> ActiveQuests() => _l.ActiveQuests(_pid).Select(q => q.Id).ToList();
    public Task<(bool Ok, string Reason)> StartQuestAsync(int questId, CancellationToken ct) => _l.QuestStartAsync(_pid, questId, ct);

    public bool ShopOpen() => St.ShopOpen;
    public IReadOnlyList<LotMReader.ShopItem> OpenShopItems() => St.ShopOpen ? St.ShopItems : Array.Empty<LotMReader.ShopItem>();
    public ShopVisit? LastShopVisit(int npcId) => St.Shops.TryGetValue(npcId, out var v) ? v : null;
    public async Task<bool> OpenShopAsync(int npcId, CancellationToken ct)
    {
        var actor = _world.Get(_pid).Actors.FirstOrDefault(a => a.Kind == MapActorKind.Npc && a.NpcId == npcId);
        return actor is not null && await _l.ShopOpenAsync(_pid, actor.ActorId, ct).ConfigureAwait(false);
    }
    public bool CloseShop() { if (!St.ShopOpen) return false; bool ok = _l.LeaveDialog(_pid); if (ok) St.ShopOpen = false; return ok; }
    public Task<(bool Ok, int Done, string Reason)> NpcBuyAsync(int gid, int qty, CancellationToken ct) => _l.NpcBuyAsync(_pid, gid, qty, ct);
    public Task<(bool Ok, int Done, string Reason)> NpcSellAsync(int gid, int qty, CancellationToken ct) => _l.NpcSellAsync(_pid, gid, qty, ct);
    public NpcSightingStore Sightings => _l.NpcSightings;

    public string? ReadScriptFile(string simpleName) => GivreLuaHost.ReadScriptsFolderScript(simpleName);
}
