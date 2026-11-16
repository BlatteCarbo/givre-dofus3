namespace GivreClone.App.Services.Forgemagie;

public static class FmSupportLines
{
    public static string Unmodeled(IReadOnlyList<string> lines)
        => lines.Count == 0 ? ""
            : $"{lines.Count} ligne(s) de l'objet sans rune modélisée, non prises en compte : {string.Join(", ", lines.Distinct())}.";
}
