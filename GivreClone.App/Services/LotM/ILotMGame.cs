using System;
using System.Collections.Generic;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.Protocol;

namespace GivreClone.App.Services.LotM;

/// <summary>Fiche du personnage réellement connue du client (ce que l'application décode ; pas toute la fiche du jeu).</summary>
public sealed record CharacterSheetData(long Id, string Name, string Breed, int Level, long XpTotal, long XpToNextLevel,
                                        int Life, int MaxLife, int Energy, int MaxEnergy, long Kamas);

/// <summary>Ce que la fiche du personnage tire d'autres décodeurs : sorts (id, nom, niveau), métiers (id, nom, niveau), objets ÉQUIPÉS (position, gid, nom, niveau),
/// points de caractéristiques non dépensés (-1 = inconnu). Une liste vide = rien de décodé, jamais « aucun ».</summary>
public sealed record SheetExtras(IReadOnlyList<(int Id, string Name, int Level)> Spells, IReadOnlyList<(int Id, string Name, int Level)> Jobs,
                                 IReadOnlyList<(int Position, int Gid, string Name, int Level)> Equipment, int UnspentPoints);

/// <summary>Une anomalie connue pour la sous-zone d'un serveur (valeurs brutes du message, voir <see cref="AnomalyClock"/>).</summary>
public sealed record AnomalyState(int SubareaId, bool Open, long ClosingRaw, int RewardRate, DateTime ReceivedUtc);

/// <summary>Altération active (Alteration du protocole) : id (50/51/52 = élixirs uchroniques), type et valeur d'expiration
/// (nombre de combats restants pour les élixirs, d'après les données du jeu).</summary>
public sealed record AlterationInfo(int Id, int ExpirationType, long ExpirationValue);

/// <summary>Proposition de téléportation en attente et instant de réception.</summary>
public sealed record OfferState(TeleportOfferInfo Offer, DateTime AtUtc);

/// <summary>Contenu de la dernière visite d'une boutique de PNJ.</summary>
public sealed record ShopVisit(IReadOnlyList<LotMReader.ShopItem> Items, DateTime SeenUtc, string GameVersion);

/// <summary>
/// Tout ce dont les modules Lua du lot M ont besoin du jeu en cours (état décodé + actions). Implémentée par l'application sur LaunchService ;
/// par un faux dans les tests. Les actions renvoient leur résultat, jamais d'exception pour un refus normal.
/// </summary>
public interface ILotMGame
{
    GameDb? Db { get; }
    DateTime UtcNow { get; }
    string GameVersion { get; }

    // ---- droits ----
    /// <summary>Droit « Récolte auto » (Perm.Gather) : actions d'interactifs, téléportation acceptée, sortie de donjon, récompenses.</summary>
    bool CanAct { get; }
    bool HasHdv { get; }

    // ---- personnage ----
    long CharacterId { get; }
    CharacterSheetData? Sheet();
    SheetExtras SheetMore();
    long CurrentMapId { get; }
    PlaceOrigin? PlaceOrigin { get; }
    IReadOnlyList<PlaceDoor> PlaceDoors(long mapId);
    bool InFight { get; }
    int ServerId { get; }
    int JobLevel(int jobId);
    int ItemCount(long gid);   // sac + banque connue

    // ---- succès ----
    AchievementLedger Achievements { get; }
    Task<bool> ClaimAchievementAsync(int id, CancellationToken ct);
    bool AutoClaim { get; set; }

    // ---- anomalies ----
    IReadOnlyList<AnomalyState> Anomalies();

    // ---- téléportation ----
    OfferState? CurrentOffer();
    Task<OfferState?> WaitOfferAsync(int ms, CancellationToken ct);
    bool AnswerOffer(bool accept);

    // ---- donjon / document ----
    Task<(bool Ok, string Reason, long MapId)> ExitDungeonAsync(CancellationToken ct);
    int OpenDocumentId();
    bool CloseDocument();

    // ---- banque / HDV ----
    int BankKnownCount(long gid);
    Task<IReadOnlyList<LotMReader.PricePoint>?> PriceHistoryAsync(long gid, CancellationToken ct);
    Task<(int Placed, string? Reason)> KeepOnSaleAsync(long gid, IReadOnlyList<KeepSize> sizes, long undercut, long? defaultPrice, long? defaultMin, CancellationToken ct);

    // ---- quêtes ----
    IReadOnlyCollection<int> FinishedQuests();
    IReadOnlyCollection<int> ActiveQuests();
    Task<(bool Ok, string Reason)> StartQuestAsync(int questId, CancellationToken ct);

    // ---- PNJ ----
    bool ShopOpen();
    IReadOnlyList<LotMReader.ShopItem> OpenShopItems();
    ShopVisit? LastShopVisit(int npcId);
    Task<bool> OpenShopAsync(int npcId, CancellationToken ct);
    bool CloseShop();
    /// <summary>Achète <paramref name="qty"/> × gid dans la boutique OUVERTE (vérifications de présence, de prix maximal et de permission déjà faites par le module).
    /// Renvoie (ok, quantité obtenue, raison) ; raisons : kamas, refused, timeout.</summary>
    Task<(bool Ok, int Done, string Reason)> NpcBuyAsync(int gid, int qty, CancellationToken ct);
    /// <summary>Vend <paramref name="qty"/> × gid du sac (jamais l'exemplaire équipé) dans la boutique OUVERTE. Raisons : noitem, refused, timeout.</summary>
    Task<(bool Ok, int Done, string Reason)> NpcSellAsync(int gid, int qty, CancellationToken ct);
    NpcSightingStore Sightings { get; }

    // ---- scripts ----
    string? ReadScriptFile(string simpleName);
}
