using System;
using System.Collections.Generic;
using System.Globalization;
using GivreClone.App.Services.LotM;

namespace GivreClone.App.Services;

/// <summary>Une ligne de zaap ou de zaapi affichée dans l'en-tête. Le coût vient des données déjà reçues.</summary>
public sealed record QuickTeleportLine(long MapId, string Title, string Detail, string CostText, bool CanSend, string Reason);

/// <summary>Zaapi retenu par le planificateur : carte et nombre de sauts à pied (0 = déjà dessus).</summary>
public readonly record struct ZaapiHost(long MapId, int Hops);

/// <summary>Décision d'un clic. <see cref="HostMap"/> est la carte du zaapi à rejoindre avant d'ouvrir, ou 0 s'il est déjà là.</summary>
public readonly record struct QuickAdmission(bool Fire, string Status, long HostMap = 0);

/// <summary>
/// Actions rapides de l'en-tête. Pur : aucune trame. L'énergie du personnage n'entre dans aucune décision.
/// Le combat, l'absence de donjon, de liste ou de zaapi joignable empêchent l'envoi.
/// </summary>
public static class QuickActions
{
    public const string NoCharacter = "Aucun personnage sélectionné.";
    public const string Fight = "Impossible en combat.";
    public const string NotInDungeon = "Pas dans un donjon.";
    public const string Busy = "Une action rapide est déjà en cours.";
    public const string ScriptOwns = "Un script dirige déjà ce personnage.";
    public const string AlreadyHere = "Déjà sur cette carte.";
    public const string Cancelled = "Action annulée.";
    public const string Interrupted = "Action rapide interrompue.";
    public const string ZaapiOnMap = "Zaapi sur cette carte.";
    public const string ZaapiUnreached = "Aucun zaapi joignable.";
    public const string PlannerMissed = "Le planificateur n'a pas atteint le zaapi.";
    public const string NoOtherZaapi = "Aucun autre zaapi à ouvrir pour cette destination.";
    public const string ZaapHint = "Un clic téléporte depuis la carte du monde, sans ouvrir de zaap.";
    public const string UnknownCostText = "coût inconnu";
    public const string CostLater = "Coût pas encore annoncé.";
    public const string ZaapiHint = "Destinations observées. Un clic utilise le zaapi. S'il n'est pas sur cette carte, le planificateur rejoint d'abord le plus proche.";
    public const string ZaapNoMatch = "Aucun zaap ne correspond.";
    public const string ZaapiNoMatch = "Aucune destination ne correspond.";
    public const string KnownCost = "Dernier coût annoncé";
    public const string AnnouncedCost = "Coût annoncé";

    public static string ZaapiReachable(int hops)
        => "Zaapi joignable à pied (" + hops.ToString(CultureInfo.InvariantCulture) + " carte(s)).";

    public static string ZaapKnown(int count)
        => count <= 0 ? WorldMapZaapTravel.UnknownListReason
        : count == 1 ? "1 zaap connu."
        : count.ToString(CultureInfo.InvariantCulture) + " zaaps connus.";

    public static string DungeonHere(string? name)
        => string.IsNullOrWhiteSpace(name) ? "Dans un donjon." : "Dans le donjon « " + name.Trim() + " ».";

    public static string LiveSummary(bool inFight, string dungeon, string zaap, string zaapi, int energy)
    {
        _ = energy;
        return inFight ? Fight : dungeon + " · " + zaap + " · " + zaapi;
    }

    public static QuickAdmission AdmitDungeon(bool hasCharacter, bool inFight, bool inDungeon, bool busy, int energy)
    {
        _ = energy;
        if (!hasCharacter) return new QuickAdmission(false, NoCharacter);
        if (inFight) return new QuickAdmission(false, Fight);
        if (busy) return new QuickAdmission(false, Busy);
        if (!inDungeon) return new QuickAdmission(false, NotInDungeon);
        return new QuickAdmission(true, DungeonHere(null));
    }

    public static string DungeonTip(bool hasCharacter, bool inFight, bool inDungeon, bool busy, string? name, int energy)
    {
        var admit = AdmitDungeon(hasCharacter, inFight, inDungeon, busy, energy);
        return admit.Fire ? DungeonHere(name) : admit.Status;
    }

    /// <summary>Texte du résultat serveur. Succès seulement si la sortie est confirmée, avec la carte d'arrivée quand elle est connue.</summary>
    public static string DungeonResult(bool ok, string? reason, long mapId)
    {
        if (ok)
        {
            string where = mapId > 0 ? " Carte " + mapId.ToString(CultureInfo.InvariantCulture) + "." : "";
            return "Donjon quitté." + where;
        }
        string why = string.IsNullOrWhiteSpace(reason) ? "résultat absent" : reason.Trim();
        if (string.Equals(why, "fight", StringComparison.OrdinalIgnoreCase)) why = Fight;
        string suffix = why.EndsWith('.') ? "" : ".";
        return "Sortie du donjon refusée : " + why + suffix;
    }

    public static bool CanBrowseZaap(bool hasCharacter, bool inFight, bool busy, int knownCount, int energy)
    {
        _ = energy;
        return hasCharacter && !inFight && !busy && knownCount > 0;
    }

    public static string ZaapTip(bool hasCharacter, bool inFight, bool busy, int knownCount, int energy)
    {
        _ = energy;
        if (!hasCharacter) return NoCharacter;
        if (inFight) return WorldMapZaapTravel.CombatReason;
        if (busy) return Busy;
        return ZaapKnown(knownCount);
    }

    public static QuickAdmission AdmitZaap(bool hasCharacter, bool inFight, bool scriptRunning, bool busy, QuickTeleportLine? line, int energy)
    {
        _ = energy;
        if (!hasCharacter) return new QuickAdmission(false, NoCharacter);
        if (inFight) return new QuickAdmission(false, WorldMapZaapTravel.CombatReason);
        if (busy) return new QuickAdmission(false, Busy);
        if (scriptRunning) return new QuickAdmission(false, ScriptOwns);
        if (line is null || line.MapId <= 0) return new QuickAdmission(false, WorldMapZaapTravel.UnknownReason);
        if (!line.CanSend) return new QuickAdmission(false, string.IsNullOrWhiteSpace(line.Reason) ? WorldMapZaapTravel.UnknownReason : line.Reason);
        return new QuickAdmission(true, line.Reason);
    }

    public static bool CanBrowseZaapi(bool hasCharacter, bool inFight, bool busy, bool onMap, ZaapiHost? nearest, int energy)
    {
        _ = energy;
        return hasCharacter && !inFight && !busy && (onMap || nearest is not null);
    }

    public static string ZaapiTip(bool hasCharacter, bool inFight, bool busy, bool onMap, ZaapiHost? nearest, int energy)
    {
        _ = energy;
        if (!hasCharacter) return NoCharacter;
        if (inFight) return ZaapiTravel.CombatReason;
        if (busy) return Busy;
        if (onMap || nearest is { Hops: 0 }) return ZaapiOnMap;
        if (nearest is { } host) return ZaapiReachable(host.Hops);
        return ZaapiUnreached;
    }

    public static QuickAdmission AdmitZaapi(bool hasCharacter, bool inFight, bool scriptRunning, bool busy,
        bool onMap, ZaapiHost? nearest, long here, QuickTeleportLine? line, int energy)
    {
        _ = energy;
        if (!hasCharacter) return new QuickAdmission(false, NoCharacter);
        if (inFight) return new QuickAdmission(false, ZaapiTravel.CombatReason);
        if (busy) return new QuickAdmission(false, Busy);
        if (scriptRunning) return new QuickAdmission(false, ScriptOwns);
        if (line is null || line.MapId <= 0) return new QuickAdmission(false, ZaapiTravel.UnknownListReason);
        if (here > 0 && line.MapId == here) return new QuickAdmission(false, AlreadyHere);
        if (!line.CanSend) return new QuickAdmission(false, string.IsNullOrWhiteSpace(line.Reason) ? ZaapiTravel.UnknownReason : line.Reason);
        if (onMap || nearest is { Hops: 0 }) return new QuickAdmission(true, line.Reason);
        if (nearest is not { } host) return new QuickAdmission(false, ZaapiUnreached);
        if (host.MapId == line.MapId) return new QuickAdmission(false, NoOtherZaapi);
        return new QuickAdmission(true, line.Reason, host.MapId);
    }

    /// <summary>
    /// Zaaps déjà connus, filtrés par le texte. La carte courante ne part pas.
    /// Un coût absent s'affiche « coût inconnu » et le clic reste possible : le serveur tranche.
    /// </summary>
    public static IReadOnlyList<QuickTeleportLine> ZaapChoices(
        IReadOnlyList<(long MapId, long? CostKamas, string Label)> known, long here, long kamas, string? query, int energy)
    {
        _ = energy;
        string q = PlaceText.Normalize(query ?? "");
        var rows = new List<QuickTeleportLine>();
        var seen = new HashSet<long>();
        if (known is null) return rows;
        foreach (var row in known)
        {
            if (row.MapId <= 0 || !seen.Add(row.MapId)) continue;
            bool hereAlready = here > 0 && row.MapId == here;
            bool priced = row.CostKamas is long cost && cost >= 0;
            var offer = hereAlready
                ? new WorldMapZaapOffer(true, priced ? row.CostKamas : null, false, AlreadyHere)
                : priced
                    ? WorldMapZaapTravel.Evaluate(row.MapId, true, row.CostKamas, kamas)
                    : new WorldMapZaapOffer(true, null, true, CostLater);
            string costText = priced ? WorldMapZaapTravel.FormatCost(row.CostKamas) : UnknownCostText;
            string detail = offer.CanSend ? (priced ? KnownCost : CostLater) : offer.Reason;
            string title = string.IsNullOrWhiteSpace(row.Label)
                ? row.MapId.ToString(CultureInfo.InvariantCulture) : row.Label.Trim();
            if (!Matches(q, title, detail, costText, row.MapId)) continue;
            rows.Add(new QuickTeleportLine(row.MapId, title, detail, costText, offer.CanSend, offer.Reason));
        }
        rows.Sort(CompareLines);
        return rows;
    }

    /// <summary>Destinations zaapi déjà observées. Aucune carte n'est ajoutée à la liste reçue.</summary>
    public static IReadOnlyList<QuickTeleportLine> ZaapiChoices(
        IReadOnlyList<ZaapiStop> stops, long here, long kamas, string? query, int energy)
    {
        _ = energy;
        string q = PlaceText.Normalize(query ?? "");
        var rows = new List<QuickTeleportLine>();
        if (stops is null) return rows;
        foreach (var stop in stops)
        {
            if (stop.MapId <= 0) continue;
            bool hereAlready = here > 0 && stop.MapId == here;
            var offer = hereAlready
                ? new ZaapiOffer(true, stop.CostKamas, false, AlreadyHere)
                : ZaapiTravel.Evaluate(stop.MapId, true, stop.CostKamas, kamas);
            string cost = WorldMapZaapTravel.FormatCost(offer.CostKamas ?? stop.CostKamas);
            string coords = ZaapiTravel.Coordinates(stop.X, stop.Y);
            string title = !string.IsNullOrWhiteSpace(stop.Name) ? stop.Name.Trim()
                : coords.Length > 0 ? coords
                : stop.MapId.ToString(CultureInfo.InvariantCulture);
            string place = JoinPlace(stop.District, coords, stop.Category);
            string detail = offer.CanSend
                ? (place.Length > 0 ? place : AnnouncedCost)
                : offer.Reason + (place.Length > 0 ? " · " + place : "");
            if (!Matches(q, title, detail, cost, stop.MapId, stop.Name, stop.District, stop.Category, coords)) continue;
            rows.Add(new QuickTeleportLine(stop.MapId, title, detail, cost, offer.CanSend, offer.Reason));
        }
        rows.Sort(CompareLines);
        return rows;
    }

    /// <summary>Parmi les cartes données, la plus proche selon <paramref name="hopsFromHere"/> (négatif = injoignable).</summary>
    public static ZaapiHost? NearestZaapi(long here, IEnumerable<long>? hosts, Func<long, int>? hopsFromHere, int energy)
    {
        _ = energy;
        if (here <= 0 || hosts is null || hopsFromHere is null) return null;
        ZaapiHost? best = null;
        var seen = new HashSet<long>();
        foreach (var host in hosts)
        {
            if (host <= 0 || !seen.Add(host)) continue;
            int hops;
            try { hops = host == here ? 0 : hopsFromHere(host); }
            catch (Exception ex) when (ex is InvalidOperationException or ArgumentException) { continue; }
            if (hops < 0) continue;
            if (best is null || hops < best.Value.Hops || (hops == best.Value.Hops && host < best.Value.MapId))
                best = new ZaapiHost(host, hops);
        }
        return best;
    }

    private static int CompareLines(QuickTeleportLine a, QuickTeleportLine b)
    {
        int name = string.Compare(a.Title, b.Title, StringComparison.OrdinalIgnoreCase);
        if (name != 0) return name;
        int map = a.MapId.CompareTo(b.MapId);
        return map != 0 ? map : string.Compare(a.Detail, b.Detail, StringComparison.Ordinal);
    }

    private static string JoinPlace(string? district, string coords, string? category)
    {
        var parts = new List<string>(3);
        if (!string.IsNullOrWhiteSpace(district)) parts.Add(district.Trim());
        if (!string.IsNullOrWhiteSpace(coords)) parts.Add(coords);
        if (!string.IsNullOrWhiteSpace(category)) parts.Add(category.Trim());
        return string.Join(" · ", parts);
    }

    private static bool Matches(string query, params object?[] parts)
    {
        if (query.Length == 0) return true;
        var text = new System.Text.StringBuilder();
        foreach (var part in parts)
        {
            if (part is null) continue;
            if (text.Length > 0) text.Append(' ');
            text.Append(part);
        }
        return PlaceText.Normalize(text.ToString()).Contains(query, StringComparison.Ordinal);
    }
}
