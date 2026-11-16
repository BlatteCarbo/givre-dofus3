using System;
using System.Collections.Concurrent;
using System.Collections.Generic;

namespace GivreClone.App.Services;

// Réglage Déplacement de chaque perso (onglet Paramètres), poussé par l'UI et lu par TOUS les hôtes de script de ce pid
// (script principal, suivi d'équipe, HDV…) au moment de choisir entre marche, zaap et havre-sac.
public sealed partial class LaunchService
{
    private readonly ConcurrentDictionary<uint, (bool PreferWalk, int WalkMaxHops)> _travelPref = new();
    private readonly ConcurrentDictionary<uint, ZaapRoute> _zaapRoute = new();

    public void SetTravelPreference(uint pid, bool preferWalk, int walkMaxHops) => _travelPref[pid] = (preferWalk, Math.Max(0, walkMaxHops));

    /// <summary>(marche de préférence, distance max à pied en cartes, 0 = sans limite). Défaut : zaap / havre-sac comme avant.</summary>
    public (bool PreferWalk, int WalkMaxHops) TravelPreference(uint pid) => _travelPref.GetValueOrDefault(pid, (false, 0));

    /// <summary>Choix de raccourci : auto, zaap depuis la carte, zaap au sol, havre-sac, zaapi. Défaut auto.</summary>
    public void SetZaapRoute(uint pid, ZaapRoute route) => _zaapRoute[pid] = route;

    public ZaapRoute ZaapRouteOf(uint pid) => _zaapRoute.TryGetValue(pid, out var route) ? route : ZaapRoute.Auto;
}
