using GivreClone.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services.Transport.Socket;

namespace GivreClone.App.ViewModels;

// Suppression d'un personnage (mode Socket) : étape 3 du sélecteur. Forme du message relevée sur 3 suppressions réelles ; seul le cas
// « compte avec code secret » est pris en charge (la session s'arrête avec un message clair sinon). Jamais de suppression sans la case cochée.
public sealed partial class AccountsViewModel
{
    public bool ServerPickOnDelete => ServerPickStep == 3;
    [ObservableProperty] public partial SocketCharCard? DeleteTarget { get; set; }
    [ObservableProperty] public partial string DeleteAnswer { get; set; } = "";
    [ObservableProperty] public partial string DeleteName { get; set; } = "";
    [ObservableProperty] public partial bool DeleteConfirmed { get; set; }

    [RelayCommand]
    private void AskDeleteSocketCharacter(SocketCharCard? c)
    {
        if (c is null || !ServerPickVisible) return;
        DeleteTarget = c; DeleteAnswer = ""; DeleteName = ""; DeleteConfirmed = false;
        ServerPickStep = 3;
        ServerPickTitle = "Supprimer un personnage";
        ServerPickSubtitle = _pickServerId > 0 ? PickServerLabel(_pickServerId) : "";
    }

    [RelayCommand] private void SocketDeleteBack() => ShowPickStep(_pickServerId);

    [RelayCommand]
    private void ConfirmSocketDeletion()
    {
        if (DeleteTarget is not { } t || string.IsNullOrEmpty(ServerPickLogin)) return;
        if (!ServerPickVisible || !ServerPickOnDelete) return;
        if (!string.Equals(DeleteName, t.Name, System.StringComparison.Ordinal))
        { Toasts.Show(Loc.T("L.Socket.Delete.NameMismatch"), success: false); return; }
        if (!DeleteConfirmed) { Toasts.Show("Coche la case pour confirmer la suppression définitive.", success: false); return; }
        if (string.IsNullOrEmpty(DeleteAnswer)) { Toasts.Show("Saisis la réponse à la question secrète (la casse compte).", success: false); return; }
        SocketDeletionRegistry.Set(ServerPickLogin, new CharacterDeletionSpec(t.Name, DeleteAnswer));
        DeleteAnswer = "";
        DeleteName = "";
        _serverPickTcs?.TrySetResult((_pickServerId, null));
    }
}
