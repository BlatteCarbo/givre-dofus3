namespace GivreClone.App.Services;

public sealed partial class LaunchService
{
    /// <summary>Charge (une fois, idempotent) les noms et icônes de monstres du client, sans attendre une connexion au jeu :
    /// l'onglet Expédition et d'autres vues hors ligne en ont besoin. À appeler hors thread UI.</summary>
    public void EnsureMonsterIcons()
    {
        _gameNames.EnsureLoaded();
        Stores.MonsterIcons.Init(_gameNames.MonsterGfx);
        Stores.MonsterIcons.EnsureLoaded();
    }
}
