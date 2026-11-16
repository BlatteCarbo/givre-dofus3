namespace GivreClone.App.Services.Transport.Socket;

/// <summary>
/// Version client Dofus 3 pour le premier login. Inutile de la deviner finement : si elle ne convient pas, le serveur
/// de connexion renvoie IdentificationResponse.Error.required_version, et on retente AUTOMATIQUEMENT avec cette
/// valeur exacte (cf SocketProbe). On part donc d'une version récente connue et on laisse le serveur corriger.
/// (globalgamemanagers = Application.version = "3.0.0.0" de base, PAS la version du patch -> inutilisable ; cytrus
/// renvoyait une autre déclinaison -> écarté. Le required_version du serveur est la seule source vraiment fiable.)
/// </summary>
internal static class DofusClientVersion
{
    // Historical static fallback is a candidate only, not server validation.
    // Normal path reads the exact 6.0_3.7.x.x releaseVersion from the local
    // Cytrus manifest. Socket admission remains distinct from this metadata.
    public const string Fallback = "3.7.11.11";

    public static string Resolve()
    {
        var exe = GivreLauncher.Core.Launch.GameLauncher.ResolveDofusPath(
            GivreClone.App.Services.AppConfig.Current.DofusExePath);
        return GivreClone.App.Services.MZP11CytrusVersionProbe.InspectInstalledDofus(exe)
                   .ProtocolVersion ?? Fallback;
    }
}
