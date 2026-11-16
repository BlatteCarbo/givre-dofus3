using CommunityToolkit.Mvvm.Input;

namespace GivreClone.App.ViewModels;

// Perso précédent / suivant dans la fiche (boutons de l'en-tête, Ctrl+PageHaut / Ctrl+PageBas). Boucle aux extrémités.
public partial class BotsViewModel
{
    [RelayCommand] private void PreviousBot() => StepBot(-1);
    [RelayCommand] private void NextBot() => StepBot(+1);

    private void StepBot(int delta)
    {
        if (Bots.Count == 0) return;
        int i = SelectedBot is null ? -1 : Bots.IndexOf(SelectedBot);
        int next = i < 0 ? 0 : ((i + delta) % Bots.Count + Bots.Count) % Bots.Count;
        SelectedBot = Bots[next];
    }
}
