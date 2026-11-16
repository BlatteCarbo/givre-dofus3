namespace GivreClone.App.Services.Signals;

public static class IncidentSubject
{
    public static string Describe(string? character, string? label, string? server, int pid)
    {
        string who = string.IsNullOrWhiteSpace(character) ? "personnage non identifié" : character.Trim();
        string tag = string.IsNullOrWhiteSpace(label) ? "sans libellé" : label.Trim();
        string srv = string.IsNullOrWhiteSpace(server) ? "serveur inconnu" : server.Trim();
        return $"{who} ({tag}) [{srv}] · session {pid}";
    }
}
