namespace GivreClone.App.Services;

/// <summary>
/// Édition LOCALE du bot (application autonome de l'utilisateur) : aucune session ni serveur distant. Les droits sont ceux de
/// l'exploitant local (tous), le mapping vient de <c>data/</c> et n'est lié au client installé que par une déclaration écrite
/// <c>data/bundle-build.json</c> (build + preuve) comparée à la version réellement lue sur l'installation (Cytrus). Ne parle à AUCUN
/// serveur Givre et ne se fait passer pour aucun. Activée avec <see cref="MZP19LocalClone.Enabled"/>.
/// </summary>
public static class LocalEdition
{
    public static bool Enabled => MZP19LocalClone.Enabled;
}
