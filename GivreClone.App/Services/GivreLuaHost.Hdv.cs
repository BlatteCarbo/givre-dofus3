using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.Engine.LuaTooling;
using GivreClone.Protocol;
using MoonSharp.Interpreter;

namespace GivreClone.App.Services;

// ===================== HDV : voyage jusqu'à l'hôtelier, ouverture et bascule vendeur / acheteur =====================
public sealed partial class GivreLuaHost
{
    // ============ HDV : voyage jusqu'au HDV cible -> ouverture de l'hôtelier (iwo) -> bascule mode VENTE ============
    // ElementTypeId des hôteliers HDV (résolus À CHAUD sur la carte comme une ressource, pas hardcodés en wire) : ressources
    // 313, équipements 314, consommables 315, runes 318, créatures 319, âmes 320, cosmétiques 381.
    private static readonly HashSet<int> HdvHotelierTypeIds = new() { 313, 314, 315, 318, 319, 320, 381 };

    /// <summary>Voyage auto jusqu'à la carte du HDV, ouvre l'hôtelier (iwo, comme une porte/récolte) puis bascule en mode
    /// VENDEUR (iov action=5). Renvoie true si le HDV vendeur est ouvert. BLOQUANT (polling) -> encapsulé en Task.Run :
    /// NE JAMAIS appeler le corps sur le thread UI. Réutilisable par l'onglet HDV (hôte transitoire) et une future API Lua sale:.</summary>
    public Task<bool> GoToHdvAndOpenSellerAsync(long targetMap, CancellationToken ct)
        => Task.Run(() => RunTravelAction(ct, token => GoToHdvAndOpenSeller(targetMap, token)), ct);

    /// <summary>Voyage jusqu'à la carte du HDV + ouverture de l'hôtelier (commun ACHAT/VENTE). Renvoie true si le HDV est
    /// ouvert (mode ACHETEUR par défaut). L'appelant bascule ensuite vendeur/acheteur selon le besoin.</summary>
    private bool OpenHotelier(long targetMap, CancellationToken ct)
    {
        ThrowIfStopping(ct);
        if (!LicenseService.HasPermission(LicenseService.Perm.Hdv)) { Warn("hdv", "permission HDV manquante"); return false; }
        if (targetMap == 0) { Warn("hdv", "aucune carte HDV cible"); return false; }

        // 1. Voyage jusqu'à la carte du HDV (BFS world-graph + raccourci zaap, comme travelTo).
        if ((_launch.CurrentMap(_pid) ?? 0) != targetMap)
        {
            Info("hdv", $"voyage vers le HDV (carte {targetMap})");
            if (!GoToMapId(targetMap, ct)) { Warn("hdv", "voyage vers le HDV échoué"); return false; }
            InterruptibleDelay(MapSettle(), ct);
        }
        WaitSelfReady(ct);

        // 2. Trouver l'hôtelier sur la carte (interactif de type HDV avec skill actif), comme le zaap du havre-sac.
        MapInteractive? h = null;
        // Carte présente dans le carnet : le serveur ne liste pas son hôtelier, inutile d'attendre le délai complet.
        int hotelierWait = HdvHotelierBook.Get(_launch.CurrentMap(_pid) ?? 0) is null ? HavenBagInteractiveTimeoutMs : 1500;
        if (!WaitUntil(() => (h = _world.Get(_pid).Interactives.FirstOrDefault(i => HdvHotelierTypeIds.Contains(i.ElementTypeId) && i.HarvestSkill is not null)) is not null, hotelierWait, ct) || h is null)
        {
            // Le serveur ne déclare pas toujours l'hôtelier dans les interactifs de la carte (vu à Astrub, patch 3.7.12.12) : on se rabat sur le carnet (relevés de capture + hôteliers appris
            // quand tu ouvres un HDV à la main en MITM) ; la case de l'élément vient des données de carte du client.
            long hm = _launch.CurrentMap(_pid) ?? 0;
            // Le serveur annonce l'hôtelier mais sans cellule exploitable (mapData du client muette) : le jeu l'ouvre quand même
            // à distance. On tente donc l'ouverture directe avant de déclarer l'hôtelier introuvable, et on l'apprend au carnet.
            if (_launch.PlacelessInteractives(_pid, hm).FirstOrDefault(i => HdvHotelierTypeIds.Contains(i.ElementTypeId) && i.Skills.Any(s => s.Enabled)) is { } ghost
                && ghost.Skills.First(s => s.Enabled) is { } gskill)
            {
                Info("hdv", $"hôtelier sans cellule connue : ouverture directe de l'élément {ghost.ElementId}…");
                var openedGhost = _launch.SendAndWaitForCodeAsync(_pid, () => _launch.SendHarvest(_pid, ghost),
                    new[] { "ExchangeBidBuyerStartedEvent", "ExchangeBidSellerStartedEvent" }, 8000, ct).GetAwaiter().GetResult();
                if (openedGhost is not null)
                {
                    HdvHotelierBook.Learn(hm, ghost.ElementId, gskill.SkillInstanceUid);
                    return true;
                }
                Warn("hdv", "ouverture directe de l'hôtelier sans cellule non confirmée par le serveur");
            }
            var known0 = HdvHotelierBook.Get(hm);
            if (known0 is not null && _launch.InteractionCell(hm, known0.ElementId) is null)
            {
                // Le client ouvre aussi ces hoteliers depuis la case d'arrivee.
                // L'absence de position dans le cache ne doit pas interdire une
                // interaction deja apprise. Seule l'ouverture serveur fait foi.
                var remote = new MapInteractive(-1, known0.ElementId, 313, 0,
                    new[] { new InteractiveSkill(known0.SkillInstanceUid, 355, 0, true) }, "Hôtel de vente");
                Info("hdv", "hôtelier du carnet sans position : ouverture directe…");
                var opened = _launch.SendAndWaitForCodeAsync(_pid, () => _launch.SendHarvest(_pid, remote),
                    new[] { "ExchangeBidBuyerStartedEvent", "ExchangeBidSellerStartedEvent" }, 8000, ct).GetAwaiter().GetResult();
                if (opened is null) Warn("hdv", "ouverture directe du HDV non confirmée par le serveur");
                return opened is not null;
            }
            if (HdvHotelierBook.Get(hm) is { } known && _launch.InteractionCell(hm, known.ElementId) is int hcell)
            {
                h = new MapInteractive(hcell, known.ElementId, 313, 0, new[] { new InteractiveSkill(known.SkillInstanceUid, 355, 0, true) }, "Hôtel de vente");
                Info("hdv", $"hôtelier absent de la liste du serveur : élément {known.ElementId} (carnet), case {hcell}");
            }
            else { Warn("hdv", "hôtelier introuvable sur la carte du HDV (ni dans la carte du serveur, ni dans le carnet : ouvre l'HDV une fois à la main en MITM pour l'apprendre)"); return false; }
        }

        // 3. Se placer à portée (marche adjacent + attente d'arrivée) puis ouvrir l'hôtelier (iwo) et attendre l'ouverture
        //    (kdw = acheteur par défaut, kby si déjà vendeur) — patron identique à l'ouverture du zaap (TravelViaZaap).
        long map = _launch.CurrentMap(_pid) ?? 0;
        var st = _world.Get(_pid);
        if (st.SelfCell is not int src) { Warn("hdv", "position du perso inconnue"); return false; }
        int? stand = _launch.HarvestStandCell(_pid, map, src, h, st.IsWalkable, st.StepAllowed, out var standWalk);
        if (stand is null) { Warn("hdv", "hôtelier inaccessible"); return false; }
        if (stand.Value != src)
        {
            var mv = _launch.SendAndWaitAsync(_pid, () => _launch.MoveTo(_pid, map, src, stand.Value, standWalk, true, st.StepAllowed), MoveTimeoutMs, ct).GetAwaiter().GetResult();
            if (mv != LaunchService.MoveOutcome.Arrived) { Warn("hdv", $"déplacement vers l'hôtelier échoué ({mv})"); return false; }
        }
        Info("hdv", "ouverture de l'hôtelier…");
        var ev = _launch.SendAndWaitForCodeAsync(_pid, () => _launch.SendHarvest(_pid, h),
            new[] { "ExchangeBidBuyerStartedEvent", "ExchangeBidSellerStartedEvent" }, 8000, ct).GetAwaiter().GetResult();
        if (ev is null) { Warn("hdv", "ouverture du HDV : pas de réponse serveur"); return false; }
        return true;
    }

    private bool GoToHdvAndOpenSeller(long targetMap, CancellationToken ct)
    {
        if (!OpenHotelier(targetMap, ct)) return false;
        // Bascule en mode VENDEUR (iov action=5) pour repricer/vendre.
        bool seller = _launch.HdvOpenSellerAsync(_pid, _launch.CurrentMap(_pid) ?? 0, ct).GetAwaiter().GetResult();
        if (seller) Ok("hdv", "HDV ouvert en mode vente"); else Warn("hdv", "bascule en mode vente échouée");
        return seller;
    }

    /// <summary>Voyage jusqu'au HDV de <paramref name="targetMap"/>, ouvre l'hôtelier et bascule en mode ACHETEUR. BLOQUANT -> Task.Run
    /// (jamais sur le thread UI). Sert aux pilotes non-Lua (achat des runes de l'onglet Forgemagie).</summary>
    public Task<bool> GoToHdvAndOpenBuyerAsync(long targetMap, CancellationToken ct)
        => RunUiTravelAsync(ct, token => GoToHdvAndOpenBuyer(targetMap, token));

    private bool GoToHdvAndOpenBuyer(long targetMap, CancellationToken ct)
    {
        if (_launch.CurrentMap(_pid) == targetMap && _launch.HdvBuyerOpen(_pid)) return true;
        if (!OpenHotelier(targetMap, ct)) return false;
        // Garantit le mode ACHETEUR (iov action=6) — requis pour rechercher (jzn) et acheter, même si le HDV était resté vendeur.
        bool buyer = _launch.HdvOpenBuyerAsync(_pid, _launch.CurrentMap(_pid) ?? 0, ct).GetAwaiter().GetResult();
        if (buyer) Ok("hdv", "HDV ouvert en mode achat"); else Warn("hdv", "bascule en mode achat échouée");
        return buyer;
    }
}
