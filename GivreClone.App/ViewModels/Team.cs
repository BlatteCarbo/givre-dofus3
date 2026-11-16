using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;

namespace GivreClone.App.ViewModels;

/// <summary>
/// Une ÉQUIPE (multi-comptes). Plusieurs équipes coexistent, indépendantes ; un bot appartient à AU PLUS UNE
/// équipe. L'objet vit dans <see cref="BotsViewModel"/> (orchestrateur cross-perso), jamais par-pid.
///
/// Source de vérité de l'appartenance = <see cref="Members"/> (des BotRow VIVANTS) ; <c>BotRow.TeamId</c> n'en est
/// qu'un MIROIR pour le badge « en groupe » et la couleur. On ne mute JAMAIS l'un sans l'autre (helpers
/// AssignBot/UnassignBot de BotsViewModel).
///
/// La <see cref="Color"/> est la démarcation VISUELLE de l'équipe : un brush INTRINSÈQUE (jamais un accent de thème
/// -> pas de DynamicResource, cf. la règle theme-accent-dynamicresource : un StaticResource sur un accent crashe au
/// démarrage), exactement comme les pastilles de présence de BotRowViewModel.
/// </summary>
public partial class Team : ObservableObject
{
    /// <summary>Identité stable de l'équipe (clé non-PII portée par BotRow.TeamId).</summary>
    public string Id { get; } = Guid.NewGuid().ToString("N");

    [ObservableProperty] public partial string Name { get; set; } = "";
    [ObservableProperty] public partial Color Color { get; set; }
    /// <summary>Groupe FORMÉ (invitations envoyées) vs simple composition. Verrouille l'édition des membres.</summary>
    [ObservableProperty] public partial bool Active { get; set; }
    /// <summary>Carte dépliée.
    /// Non persisté : dépliée par défaut à chaque session.</summary>
    [ObservableProperty] public partial bool IsExpanded { get; set; } = true;

    /// <summary>Plie/déplie le groupe.</summary>
    [RelayCommand] private void ToggleExpanded() => IsExpanded = !IsExpanded;
    /// <summary>Lancement des comptes du roster EN COURS (« Connecter en jeu ») : désactive le bouton + affiche « Connexion… ».
    /// Non persisté (transitoire). L'auto-invitation, elle, reste armée dans BotsViewML tant que les membres arrivent.</summary>
    [ObservableProperty] public partial bool Connecting { get; set; }
    /// <summary>Délai inter-bots ALÉATOIRE (ms) : TeamPilot tire une valeur dans [SyncMinMs, SyncMaxMs] avant de faire
    /// partir chaque follower. Coercition min≤max ci-dessous.</summary>
    [ObservableProperty] public partial int SyncMinMs { get; set; } = 200;
    [ObservableProperty] public partial int SyncMaxMs { get; set; } = 800;
    /// <summary>Suivi automatique actif : les followers rejoignent la carte du chef (TeamPilot). Désactivable sans dissoudre.
    /// Le suivi est TOUJOURS simultané+collé (comportement figé côté moteur : cf BotsViewModel `const bool tight = true`) ;
    /// l'attente de carte avant de quitter est IMPLICITE dès que le suivi est actif — plus de toggles séparés.</summary>
    [ObservableProperty] public partial bool FollowEnabled { get; set; } = true;
    /// <summary>Auto-join du combat : quand le chef entre en combat, les followers présents sur sa carte le rejoignent.
    /// L'attente des followers avant le combat est IMPLICITE dès que l'auto-join est actif — plus de toggle séparé.</summary>
    [ObservableProperty] public partial bool CombatJoinEnabled { get; set; } = true;

    partial void OnSyncMinMsChanged(int value) { if (value > SyncMaxMs) SyncMaxMs = value; }   // garde min ≤ max
    partial void OnSyncMaxMsChanged(int value) { if (value < SyncMinMs) SyncMinMs = value; }

    /// <summary>ROSTER persisté = appartenance PAR NOM+SERVEUR (source de vérité, survit aux déconnexions). BotsViewModel
    /// le sérialise (teams.json) et en dérive <see cref="Members"/> (les membres actuellement EN JEU) par réconciliation.</summary>
    public List<TeamMemberRecord> Roster { get; } = new();
    /// <summary>Nombre de membres ENREGISTRÉS (roster), en ligne ou non — c'est le compteur affiché sur la carte.</summary>
    public int RosterCount => Roster.Count;

    /// <summary>Membres actuellement EN JEU (BotRow), dérivés du roster par réconciliation. Pilote Leader/CanForm/la formation.</summary>
    public ObservableCollection<BotRowViewModel> Members { get; } = new();

    /// <summary>Liste d'AFFICHAGE de la carte (dans l'ordre du roster) : un membre EN JEU = son <see cref="BotRowViewModel"/>
    /// vivant (présence live) ; un membre HORS LIGNE = un <see cref="OfflineMemberViewModel"/> (nom + icône de classe grisés).
    /// Reconstruite par <c>BotsViewModel.ReconcileTeams</c> -> affiche AUSSI les membres déconnectés (avant : ils disparaissaient).</summary>
    public ObservableCollection<object> DisplayMembers { get; } = new();

    /// <summary>Chef VIVANT de CETTE équipe (unique), ou null si pas encore désigné / hors ligne.</summary>
    public BotRowViewModel? Leader => Members.FirstOrDefault(m => m.TeamRole == TeamRoleKind.Leader);
    public int FollowerCount => Members.Count(m => m.TeamRole == TeamRoleKind.Follower);
    /// <summary>Formable : un chef + au moins un follower EN JEU.</summary>
    public bool CanForm => Leader is not null && FollowerCount >= 1;
    /// <summary>« Connecter en jeu » possible : au moins un membre enregistré et pas de lancement déjà en cours.</summary>
    public bool CanConnect => RosterCount > 0 && !Connecting;

    partial void OnConnectingChanged(bool value) => OnPropertyChanged(nameof(CanConnect));

    /// <summary>Brush plein de la couleur d'équipe (bordure de carte, badge chef, pastille dans « Bots en jeu »).</summary>
    public IBrush ColorBrush => new ImmutableSolidColorBrush(Color);
    /// <summary>Variante douce (~20 % d'alpha) pour les fonds de badge.</summary>
    public IBrush ColorSoftBrush => new ImmutableSolidColorBrush(new Color(0x33, Color.R, Color.G, Color.B));

    partial void OnColorChanged(Color value)
    { OnPropertyChanged(nameof(ColorBrush)); OnPropertyChanged(nameof(ColorSoftBrush)); }

    /// <summary>À appeler après toute mutation de <see cref="Members"/>/<see cref="Roster"/> ou de rôle : re-notifie les dérivés.</summary>
    public void NotifyMembers()
    { OnPropertyChanged(nameof(Leader)); OnPropertyChanged(nameof(FollowerCount)); OnPropertyChanged(nameof(CanForm)); OnPropertyChanged(nameof(RosterCount)); OnPropertyChanged(nameof(CanConnect)); }

    public Team(string name, Color color) { Name = name; Color = color; }
}

/// <summary>Fiche d'AFFICHAGE d'un membre d'équipe actuellement HORS LIGNE (pas de BotRow vivant) : identité persistée
/// (nom + serveur + rôle) et icône de classe reconstruite depuis le BreedId mémorisé. Grisée dans la carte ; les actions
/// (définir chef / retirer) passent par nom+serveur (cf. BotsViewModel.SetOfflineLeader / RemoveOfflineMember).</summary>
public sealed class OfflineMemberViewModel
{
    public string TeamId { get; }
    public string Name { get; }
    public string Server { get; }
    public bool IsLeader { get; }
    /// <summary>Éditable (définir chef / retirer) uniquement tant que le groupe n'est PAS formé.</summary>
    public bool CanEdit { get; }
    /// <summary>Symbole de classe (persisté via BreedId), ou null si inconnu -> l'UI affiche l'icône générique.</summary>
    public Avalonia.Media.Imaging.Bitmap? ClassIcon { get; }

    public OfflineMemberViewModel(Team team, TeamMemberRecord rec, Avalonia.Media.Imaging.Bitmap? icon)
    { TeamId = team.Id; Name = rec.Name; Server = rec.Server ?? ""; IsLeader = rec.Leader; CanEdit = !team.Active; ClassIcon = icon; }
}
