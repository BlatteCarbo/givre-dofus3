using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.Linq;

namespace GivreClone.App.Services;

/// <summary>Échange ouvert côté serveur, tel que la fenêtre d'interaction l'affiche (un seul à la fois pour un perso).</summary>
public enum ExchangeKind { None, Bank, Trade, Shop, Teleport }

/// <summary>
/// Logique PURE de la fenêtre d'interaction (banque / échange joueur / boutique PNJ) et des actions d'inventaire de l'app :
/// sans Avalonia ni LaunchService -&gt; liée telle quelle dans les tests. Identique en MITM et en Socket : seul le transport change.
/// </summary>
public static class InteractionLogic
{
    /// <summary>Échange à afficher. Un échange joueur prime (il a pu s'ouvrir PAR-DESSUS une boutique restée marquée ouverte),
    /// puis la banque, la boutique et enfin un dialogue de téléportation (zaap, zaapi, havre-sac).</summary>
    public static ExchangeKind Resolve(bool storageOpen, bool tradeOpen, bool shopOpen, bool teleportOpen = false)
        => tradeOpen ? ExchangeKind.Trade : storageOpen ? ExchangeKind.Bank : shopOpen ? ExchangeKind.Shop
         : teleportOpen ? ExchangeKind.Teleport : ExchangeKind.None;

    /// <summary>Quantité effective d'une action : <paramref name="requested"/> &lt;= 0 = tout ; jamais plus que <paramref name="available"/>.</summary>
    public static int ClampQty(int requested, int available)
        => available <= 0 ? 0 : requested <= 0 ? available : Math.Min(requested, available);

    /// <summary>Répartit <paramref name="want"/> (&lt;= 0 = tout) sur des piles, plus grosses d'abord : (uid, quantité) par envoi.
    /// Un équipement = 1 uid par exemplaire, donc une pile peut ne pas suffire.</summary>
    public static IReadOnlyList<(int Uid, int Qty)> Spread(IEnumerable<(int Uid, int Qty)> stacks, int want)
    {
        var ordered = stacks.Where(s => s.Uid != 0 && s.Qty > 0).OrderByDescending(s => s.Qty).ToList();
        long total = ordered.Sum(s => (long)s.Qty);
        long left = want <= 0 ? total : Math.Min(want, total);
        var plan = new List<(int, int)>();
        foreach (var s in ordered)
        {
            if (left <= 0) break;
            int q = (int)Math.Min(left, s.Qty);
            plan.Add((s.Uid, q));
            left -= q;
        }
        return plan;
    }

    /// <summary>Clé de libellé d'un emplacement d'équipement (CharacterInventoryPositionEnum), null hors des 16 emplacements de base.</summary>
    public static string? SlotKey(int position) => position switch
    {
        0 => "L.Inv.Slot.Amulet",
        1 => "L.Inv.Slot.Weapon",
        2 => "L.Inv.Slot.RingLeft",
        3 => "L.Inv.Slot.Belt",
        4 => "L.Inv.Slot.RingRight",
        5 => "L.Inv.Slot.Boots",
        6 => "L.Inv.Slot.Hat",
        7 => "L.Inv.Slot.Cape",
        8 => "L.Inv.Slot.Pet",
        >= 9 and <= 14 => "L.Inv.Slot.Dofus",
        15 => "L.Inv.Slot.Shield",
        16 => "L.Inv.Slot.Mount",
        // Emplacements d'altérations (CharacterInventoryPositionEnum) : objets « N combats restants » posés par le jeu.
        20 => "L.Inv.Slot.Mutation",
        21 => "L.Inv.Slot.BoostFood",
        22 or 23 => "L.Inv.Slot.Bonus",
        24 or 25 => "L.Inv.Slot.Malus",
        26 => "L.Inv.Slot.RpBuff",
        27 => "L.Inv.Slot.Follower",
        28 => "L.Inv.Slot.Companion",
        _ => null,
    };

    /// <summary>Ordre d'affichage des emplacements (colonne gauche, colonne droite, rangée des dofus) — celui du jeu.</summary>
    public static readonly int[] LeftSlots = { 6, 7, 3, 5, 15 };
    public static readonly int[] RightSlots = { 0, 2, 4, 1, 8 };
    public static readonly int[] DofusSlots = { 9, 10, 11, 12, 13, 14 };
}

/// <summary>
/// Contenu d'un échange JOUEUR vu des deux côtés, reconstruit depuis les accusés serveur (Exchange*Event, champ remote) :
/// objets (uid -&gt; gid, quantité dans l'échange), kamas posés et état « prêt ». Neuf à l'ouverture, oublié à la fermeture.
/// Thread-safe : écrit par le thread réseau, lu par l'UI.
/// </summary>
public sealed class TradeContent
{
    private readonly ConcurrentDictionary<int, (long Gid, int Qty)> _mine = new(), _theirs = new();
    private long _myKamas, _theirKamas;
    private volatile bool _meReady, _theyReady;

    private ConcurrentDictionary<int, (long Gid, int Qty)> Side(bool remote) => remote ? _theirs : _mine;

    /// <summary>Objets ajoutés ou modifiés (quantité DANS l'échange ; 0 = retiré). Tout changement de contenu annule les « prêt ».</summary>
    public void Upsert(bool remote, IEnumerable<(int Uid, long Gid, int Qty)> objects)
    {
        var side = Side(remote);
        foreach (var o in objects)
            if (o.Qty > 0) side[o.Uid] = (o.Gid, o.Qty); else side.TryRemove(o.Uid, out _);
        ResetReady();
    }

    public void Remove(bool remote, IEnumerable<int> uids)
    {
        var side = Side(remote);
        foreach (int u in uids) side.TryRemove(u, out _);
        ResetReady();
    }

    public void SetKamas(bool remote, long amount)
    {
        if (remote) System.Threading.Interlocked.Exchange(ref _theirKamas, Math.Max(0, amount));
        else System.Threading.Interlocked.Exchange(ref _myKamas, Math.Max(0, amount));
        ResetReady();
    }

    public void SetReady(bool remote, bool ready) { if (remote) _theyReady = ready; else _meReady = ready; }

    private void ResetReady() { _meReady = false; _theyReady = false; }

    public IReadOnlyList<(int Uid, long Gid, int Qty)> Mine => Snapshot(_mine);
    public IReadOnlyList<(int Uid, long Gid, int Qty)> Theirs => Snapshot(_theirs);
    public long MyKamas => System.Threading.Interlocked.Read(ref _myKamas);
    public long TheirKamas => System.Threading.Interlocked.Read(ref _theirKamas);
    public bool MeReady => _meReady;
    public bool TheyReady => _theyReady;

    private static IReadOnlyList<(int Uid, long Gid, int Qty)> Snapshot(ConcurrentDictionary<int, (long Gid, int Qty)> side)
        => side.Select(kv => (kv.Key, kv.Value.Gid, kv.Value.Qty)).OrderBy(x => x.Gid).ThenBy(x => x.Key).ToList();
}

/// <summary>Panoplie enregistrée : un nom et l'objet (gid) porté à chaque emplacement.</summary>
public sealed record GearSet(string Name, IReadOnlyList<(int Position, long Gid)> Items);

/// <summary>Format texte d'une panoplie dans les réglages : « nom|position:gid,position:gid ». Le nom ne peut pas contenir « | ».</summary>
public static class GearSetCodec
{
    public static string Format(GearSet g)
        => (g.Name ?? "").Replace("|", "/").Trim() + "|" + string.Join(",", g.Items.Where(i => i.Gid > 0).Select(i => $"{i.Position}:{i.Gid}"));

    public static GearSet? Parse(string? s)
    {
        if (string.IsNullOrWhiteSpace(s)) return null;
        int bar = s.LastIndexOf('|');
        if (bar <= 0) return null;
        var items = new List<(int, long)>();
        foreach (var token in s[(bar + 1)..].Split(',', StringSplitOptions.RemoveEmptyEntries))
        {
            var kv = token.Split(':');
            if (kv.Length == 2 && int.TryParse(kv[0], out int pos) && long.TryParse(kv[1], out long gid) && gid > 0 && pos >= 0 && pos != 63)
                items.Add((pos, gid));
        }
        return items.Count == 0 ? null : new GearSet(s[..bar].Trim(), items);
    }
}
