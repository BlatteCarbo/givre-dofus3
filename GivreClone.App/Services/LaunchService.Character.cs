using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.App.Services.Transport;
using GivreClone.Engine.Accounts;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using GivreLauncher.Core.Ankama;
using GivreLauncher.Core.Launch;
using GivreLauncher.Core.Proxy;
using GivreLauncher.Core.Transport;
using GivreLauncher.Core.Zaap;
using CoreAccount = GivreLauncher.Core.Accounts.Account;

namespace GivreClone.App.Services;

// ===================== Personnage : identité, serveur, vie / Phénix, niveau, kamas, infos objets et ressources =====================
public sealed partial class LaunchService
{
    private readonly ConcurrentDictionary<uint, bool> _emptyStatBuffInventory = new();
    private readonly ConcurrentDictionary<uint, System.Collections.Immutable.ImmutableHashSet<int>> _observedStatBuffSources = new();
    public bool HasEmptyStatBuffInventory(uint pid) => _emptyStatBuffInventory.GetValueOrDefault(pid);
    public bool IsStatBuffSourceAbsent(uint pid, int spellId) =>
        _observedStatBuffSources.TryGetValue(pid, out var sources) && !sources.Contains(spellId);
    private readonly ConcurrentDictionary<uint, long> _charId = new();   // pid -> characterId (capté sur kvi)
    private readonly ConcurrentDictionary<uint, int> _charLevel = new(); // pid -> niveau du perso piloté (cap de niveau licence, per-perso)
    private readonly ConcurrentDictionary<uint, long> _kamas = new();    // pid -> kamas (capté sur ivx)
    private readonly ConcurrentDictionary<uint, IReadOnlyList<(long Id, CharacterInfo Info)>> _characters = new();  // pid -> tous les persos du compte (kvi)

    /// <summary>characterId du perso piloté (capté au login sur CharacterInformation), ou 0 si inconnu.</summary>
    public long CharacterId(uint pid) => _charId.TryGetValue(pid, out var id) ? id : 0;

    private readonly ConcurrentDictionary<uint, (long Id, CharacterInfo Info)> _selfInfo = new();                   // pid -> infos du perso JOUÉ (kvi 1 perso / kvw)
    private readonly ConcurrentDictionary<uint, (int Id, string Name)> _serverInfo = new();        // pid -> serveur de jeu (izs / whois)

    /// <summary>Infos (nom/niveau/classe/sexe) du perso JOUÉ, ou null. Repli : entrée de la liste kvi dont l'id == charId
    /// (couvre un charId adopté depuis le jss quand kvw n'a pas été capté).</summary>
    public CharacterInfo? SelfCharacterInfo(uint pid)
    {
        long id = CharacterId(pid);
        // L'entrée kvw/kvi n'est valable que pour le perso ENCORE joué (retour à la sélection + kvw manqué : charId adopté
        // du jss -> sinon on rendrait classe/sexe de l'ANCIEN perso).
        if (_selfInfo.TryGetValue(pid, out var self) && (id == 0 || self.Id == id)) return self.Info;
        return id != 0 && _characters.TryGetValue(pid, out var l) ? l.FirstOrDefault(c => c.Id == id).Info : null;
    }

    /// <summary>Nom du serveur de jeu de ce bot, "" si inconnu.</summary>
    public string ServerNameOf(uint pid) => _serverInfo.TryGetValue(pid, out var s) ? s.Name : "";
    /// <summary>Id du serveur de jeu de ce bot, 0 si inconnu.</summary>
    public int ServerIdOf(uint pid) => _serverInfo.TryGetValue(pid, out var s) ? s.Id : 0;

    /// <summary>Ids de TOUS les persos du compte (capté sur kvi/CharacterInformation), pour amorcer l'identité self quand la
    /// SÉLECTION (kvw) n'a pas été captée : le perso self dans le jss est celui dont l'actorId ∈ ces ids. Vide si kvi manqué.</summary>
    public IReadOnlyList<long> AccountCharacterIds(uint pid)
        => _characters.TryGetValue(pid, out var l) ? l.Select(x => x.Id).ToArray() : System.Array.Empty<long>();

    /// <summary>Adopte un characterId découvert autrement que par kvi/kvw (ex : perso self repéré dans le jss via AccountCharacterIds)
    /// UNIQUEMENT s'il est encore inconnu (0) — ne remplace jamais un id déjà capté. Débloque aussi l'identité de COMBAT.</summary>
    public void AdoptCharacterId(uint pid, long id, string source = "MapComplementaryInformationEvent", CharacterInfo? selectedInfo = null)
    {
        if (id <= 0) return;
        bool adopted = _charId.TryAdd(pid, id);
        if (!adopted && CharacterId(pid) != id) return;   // ne jamais adopter un autre combattant
        if (!adopted && selectedInfo is null) return;   // liste du compte potentiellement antérieure à une montée de niveau
        if (adopted)
            TransportLog.Line($"[perso] pid {pid} : characterId {id} adopté depuis {source} (sélection du perso non captée)");
        // La sélection serveur contient l'identité complète même si la liste du compte n'a pas été reçue.
        // Un id déjà connu peut donc encore recevoir son nom/classe lors de la confirmation.
        var info = selectedInfo ?? (_characters.TryGetValue(pid, out var list) ? list.FirstOrDefault(c => c.Id == id).Info : null);
        if (info is not null && (!_selfInfo.TryGetValue(pid, out var previous) || previous.Id != id || previous.Info != info))
        {
            _selfInfo[pid] = (id, info);
            if (!string.IsNullOrEmpty(info.Name)) CharacterIdentified?.Invoke(pid, info);
        }
        if (info?.Level is int lv and > 0 && CharacterLevel(pid) != lv)
            SetCharacterLevel(pid, lv);
    }

    /// <summary>Millisecondes écoulées depuis la fin du DERNIER combat (long.MaxValue si aucun). Sert à taire le bruit
    /// "hors du world-graph" pendant la fenêtre post-combat où la carte courante est encore l'instance de combat.</summary>
    public long MsSinceFightEnd(uint pid) => _fightEndAt.TryGetValue(pid, out var t) ? Environment.TickCount64 - t : long.MaxValue;
    private readonly ConcurrentDictionary<uint, long> _fightEndAt = new();

    // ===== État de vie / résurrection au Phénix (CharacterLifeStatusEvent) =====
    private readonly ConcurrentDictionary<uint, int> _lifeStatus = new();    // 0=vivant, 1=tombe, 2=fantôme
    private readonly ConcurrentDictionary<uint, long> _phoenixMap = new();   // carte du Phénix (donnée par le serveur à la mort)

    /// <summary>État de vie du perso : 0=vivant (AliveAndKicking), 1=tombe (Tombstone), 2=fantôme (Phantom).</summary>
    public int LifeStatusOf(uint pid) => _lifeStatus.TryGetValue(pid, out var s) ? s : 0;

    /// <summary>Carte du Phénix à rejoindre pour ressusciter (donnée par le serveur dans CharacterLifeStatusEvent), 0 si inconnue.</summary>
    public long PhoenixMapId(uint pid) => _phoenixMap.TryGetValue(pid, out var m) ? m : 0;

    /// <summary>Résurrection auto activée pour ce perso (profil combat AutoRevive). Non gaté par le droit "fight" (pas une action de combat).</summary>
    public bool AutoReviveEnabled(uint pid) => _combat.TryGetValue(pid, out var p) && p.AutoRevive;

    /// <summary>Envoie FreeSoulRequest (VIDE) : passe de TOMBE à FANTÔME pour pouvoir rejoindre le Phénix. Forge par nom.</summary>
    public bool SendFreeSoul(uint pid)
    {
        var relay = RelayForInject();
        if (relay is null) return false;
        byte[] pkt = MoveForge.BuildFreeSoulFramed();
        if (pkt.Length == 0) { TransportLog.Line($"[mort] pid {pid} : FreeSoulRequest NON forgé ({MoveForge.LastForgeError ?? "code absent du mapping"}) -> pas d'injection"); return false; }
        bool ok = relay.Inject(pid, pkt);
        ShowInjected(pid, pkt);
        TransportLog.Line($"[mort] pid {pid} : FreeSoulRequest (tombe->fantôme) inject={(ok ? "OK" : "ECHEC")}");
        return ok;
    }

    /// <summary>Niveau du perso piloté (0 si pas encore identifié) — capté à la sélection + maj sur CharacterLevelUpEvent.</summary>
    public int CharacterLevel(uint pid) => _charLevel.TryGetValue(pid, out var l) ? l : 0;

    /// <summary>true si le perso de `pid` DÉPASSE le cap de niveau de la licence (offre gratuite > 30). false si niveau encore
    /// inconnu (fail-open) : gate du point d'action (injection).</summary>
    public bool LevelBlocked(uint pid) => !LicenseService.LevelAllowed(CharacterLevel(pid));

    /// <summary>true si une automatisation NE PEUT PAS démarrer sur `pid` : sous cap, niveau inconnu OU au-delà (fail-closed).</summary>
    public bool LevelStartBlocked(uint pid) => !LicenseService.LevelAllowedToStart(CharacterLevel(pid));

    /// <summary>Niveau du perso piloté connu ou changé (liste des persos, sélection, montée de niveau). Thread de fond.</summary>
    public event Action<uint, int>? CharacterLevelKnown;

    private void SetCharacterLevel(uint pid, int level)
    {
        if (level <= 0) return;
        _charLevel[pid] = level;
        CharacterLevelKnown?.Invoke(pid, level);
    }

    /// <summary>Kamas du perso (capté sur le snapshot d'inventaire ivx). -1 si inconnu (pas encore de snapshot).</summary>
    public long Kamas(uint pid) => _kamas.TryGetValue(pid, out var k) ? k : -1;

    // Point d'écriture UNIQUE du total de kamas (snapshot d'inventaire, KamasUpdateEvent, caractéristiques) : un débit survenu
    // HDV vendeur ouvert est une taxe de mise en vente / de reprix, comptée par le HDV Tracker (HdvTrackKamas).
    private void SetKamas(uint pid, long kamas)
    {
        if (_smithReceiptObserver != 0 && _smithSpendGate is not null)
        {
            lock (_smithSpendGate)
            {
                long previous = Kamas(pid);
                _kamas[pid] = kamas;
                NoteSmithWallet(pid, previous, kamas);
                HdvTrackKamas(pid, previous, kamas);
            }
            return;
        }
        long before = Kamas(pid);
        _kamas[pid] = kamas;
        NoteSmithWallet(pid, before, kamas);
        HdvTrackKamas(pid, before, kamas);
    }

    private readonly ConcurrentDictionary<uint, long> _bankKamas = new();   // pid -> kamas DANS le coffre (capté sur iwb #2)

    /// <summary>Kamas dans le COFFRE/banque (capté à l'ouverture, iwb #2). -1 si la banque n'a pas encore été ouverte.</summary>
    public long BankKamas(uint pid) => _bankKamas.TryGetValue(pid, out var k) ? k : -1;

    /// <summary>Nom clair d'un objet (gid) pour l'affichage/log, jamais null.</summary>
    public int ItemLevel(int gid) => _gameNames.ItemLevel(gid);
    public IReadOnlyList<(int Gid, string Name, int Level)> SearchItems(string query, int limit) => _gameNames.SearchItems(query, limit);

    public string ItemDisplayName(long gid) => _gameNames.ItemName((int)gid) ?? Loc.Fmt("L.Bots.ItemFallback", gid);

    /// <summary>Icone d'un objet (gid) : iconId pour InteractiveIconProvider.GetByIcon. 0 si inconnu.</summary>
    public int ItemIconId(long gid) => _gameNames.ItemIconId((int)gid);

    /// <summary>Vrai si le gid est vendable au HDV — filtre « items éligibles » : tout objet CATALOGUÉ (ressources récoltées
    /// OU non, consommables, équipements, runes...), pas seulement les ressources de récolte. Exige les données d'items
    /// chargées : PAS de repli permissif (on ne propose pas un gid qu'on ne sait pas classer).</summary>
    public bool IsSellableItem(long gid) => _gameNames.ItemDataLoaded && _gameNames.IsCataloguedItem((int)gid);

    /// <summary>Ressource (récoltée ou de super-type « Ressources ») : périmètre de « tout mettre en vente » à l'HDV.</summary>
    public bool IsResourceItem(long gid) => _gameNames.IsResourceItem((int)gid);

    /// <summary>Categorie d'un objet (gid) = nom de son type (ex "Bois", "Minerai", "Epee", "Poudre"). Null si inconnu.</summary>
    public string? ItemCategory(long gid) => _gameNames.ItemCategory((int)gid);

    /// <summary>typeId (ItemType) d'un objet (gid) : cle stable pour trier/grouper par categorie. 0 si inconnu.</summary>
    public int ItemTypeId(long gid) => _gameNames.ItemTypeId((int)gid);

    /// <summary>Vrai si l'interactif est une VRAIE ressource recoltable : il a au moins un skill ACTIF qui produit un objet
    /// recolte (Couper/Collecter/Pecher...). FAUX pour un atelier/zaap/coffre/porte (skills Utiliser/Forger... sans objet).
    /// Utilise par le mode "recolte tout" (ELEMENTS_TO_GATHER vide) pour ne PLUS cliquer sur tout le mobilier interactif.
    /// Repli : si les donnees de skills du jeu ne sont pas chargees, on rend l'ancien comportement (tout recoltable).</summary>
    public bool IsGatherResource(MapInteractive it) =>
        !_gameNames.SkillDataLoaded
        || it.Skills.Any(s => s.Enabled && _gameNames.GatheredItemForSkill(s.SkillId) > 0);

    /// <summary>Les données de skills de récolte du jeu (gatheredRessourceItem) sont-elles chargées ? Si faux, on ne peut
    /// pas distinguer une ressource d'une action passive par le skill -> l'appelant doit utiliser d'autres signaux.</summary>
    public bool SkillDataLoaded => _gameNames.SkillDataLoaded;

    /// <summary>Vrai si le skill ENVOYÉ (HarvestSkill) produit un objet récolté (gatheredRessourceItem&gt;0) = VRAIE ressource.
    /// Faux pour une action passive ("Utiliser"/porte/zaap/mécanisme) OU si les données de skills ne sont pas chargées.</summary>
    public bool ProducesGatheredItem(MapInteractive it) =>
        _gameNames.SkillDataLoaded && it.HarvestSkill is { } hs && _gameNames.GatheredItemForSkill(hs.SkillId) > 0;

    public int GatheredItemGid(MapInteractive it) =>
        _gameNames.SkillDataLoaded && it.HarvestSkill is { } hs ? _gameNames.GatheredItemForSkill(hs.SkillId) : 0;

    /// <summary>Vrai si `cell` est une CELLULE de TRANSITION du world-graph sur `mapId` (porte/passage/bord qui change de
    /// carte). Sert à EXCLURE ces interactifs de « navigation » de la récolte automatique (le bot ne doit pas les « Utiliser »).</summary>
    public bool IsWorldTransitionCell(long mapId, int cell)
        => _worldGraph?.Transitions((int)mapId).Any(t => t.CellId == cell) ?? false;

    public ProxySettings? GetProxySettings(uint pid) =>
        _pidProxy.TryGetValue(pid, out var p) ? p : null;
    private readonly ConcurrentDictionary<(uint Pid, long FighterId), HashSet<int>> _activeFighterStates = new();
    private readonly ConcurrentDictionary<(uint Pid, long FighterId), HashSet<int>> _absentFighterStates = new();
    private readonly ConcurrentDictionary<(uint Pid, long FighterId, int Instance), int> _fighterStateInstances = new();
    private readonly ConcurrentDictionary<uint, Combat.DistributionTriggerTracker> _distributionTriggers = new();

    public bool FighterHasState(uint pid, long fighterId, int stateId) =>
        _activeFighterStates.TryGetValue((pid, fighterId), out var set) && set.Contains(stateId);

    public IReadOnlyCollection<int> FighterActiveStates(uint pid, long fighterId) =>
        _activeFighterStates.TryGetValue((pid, fighterId), out var set) ? ReadStates(set) : Array.Empty<int>();

    public IReadOnlyCollection<int> FighterAbsentStates(uint pid, long fighterId) =>
        _absentFighterStates.TryGetValue((pid, fighterId), out var set) ? ReadStates(set) : Array.Empty<int>();

    private static int[] ReadStates(HashSet<int> states)
    {
        lock (states) return states.ToArray();
    }

    public void SetFighterState(uint pid, long fighterId, int stateId, bool active)
    {
        var set = _activeFighterStates.GetOrAdd((pid, fighterId), _ => new HashSet<int>());
        lock (set)
        {
            if (active) set.Add(stateId);
            else if (!_fighterStateInstances.Any(kv => kv.Key.Pid == pid && kv.Key.FighterId == fighterId && kv.Value == stateId))
                set.Remove(stateId);
            var absent = _absentFighterStates.GetOrAdd((pid, fighterId), _ => new HashSet<int>());
            lock (absent)
            {
                if (set.Contains(stateId)) absent.Remove(stateId);
                else absent.Add(stateId);
            }
        }
    }

    public void ClearFighterStates(uint pid)
    {
        _distributionTriggers.TryRemove(pid, out _);
        _emptyStatBuffInventory.TryRemove(pid, out _);
        _observedStatBuffSources.TryRemove(pid, out _);
        var keys = _activeFighterStates.Keys.Where(k => k.Pid == pid).ToList();
        foreach (var k in keys) _activeFighterStates.TryRemove(k, out _);
        foreach (var k in _absentFighterStates.Keys.Where(k => k.Pid == pid)) _absentFighterStates.TryRemove(k, out _);
        var instKeys = _fighterStateInstances.Keys.Where(k => k.Pid == pid).ToList();
        foreach (var k in instKeys) _fighterStateInstances.TryRemove(k, out _);
    }
}
