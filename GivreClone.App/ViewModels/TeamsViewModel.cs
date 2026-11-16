namespace GivreClone.App.ViewModels;

/// <summary>
/// Page « Équipe » (barre de nav de gauche) : l'orchestration CROSS-bots (grouper des comptes en jeu, désigner un
/// chef, former le groupe ; suivi/combat coordonné à venir). VM-hôte MINCE : l'état d'équipe vit dans
/// <see cref="BotsViewModel"/> (il est intriqué au roster des bots, au Tick et à Resync), donc cette page s'y branche
/// simplement via <see cref="Bots"/> au lieu de dupliquer la logique. Un type distinct suffit pour que le ViewLocator
/// route vers <c>TeamsView</c> (et non BotsView).
/// </summary>
public partial class TeamsViewModel : ViewModelBase
{
    public BotsViewModel Bots { get; }
    public TeamsViewModel(BotsViewModel bots) => Bots = bots;
}
