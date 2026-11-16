namespace GivreClone.App.Services;

/// <summary>Formatage des logs qui touchent au protocole : la correspondance <b>code obfusqué → nom clair</b> est le cœur de
/// notre mapping. Un utilisateur normal ne doit JAMAIS la retrouver dans la console ni dans les fichiers de log. On n'affiche
/// donc que le <b>nom clair</b> (et les numéros de champ, non secrets) ; le code n'apparaît que pour une licence DÉVELOPPEUR
/// (<see cref="LicenseService.IsDeveloper"/>).</summary>
internal static class ProtoLog
{
    /// <summary>Nom clair + code entre parenthèses pour une licence développeur ; sinon le nom clair seul.</summary>
    public static string Named(string? name, string? code)
    {
        name = string.IsNullOrEmpty(name) ? "?" : name;
        return LicenseService.IsDeveloper && !string.IsNullOrEmpty(code) ? $"{name} ({code})" : name;
    }

    /// <summary>Un code brut seul (jamais de nom) : masqué hors licence développeur.</summary>
    public static string Code(string? code) => LicenseService.IsDeveloper ? (string.IsNullOrEmpty(code) ? "?" : code) : "•";
}
