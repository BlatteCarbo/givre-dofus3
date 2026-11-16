using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace GivreClone.App.Services;

/// <summary>
/// Équipes persistées dans %APPDATA%/GivreClone/teams.json (même patron AOT-safe que PlanningStore : sérialisation via
/// le contexte source-généré <see cref="AppJsonContext"/>). Les membres sont mémorisés PAR NOM+SERVEUR (les pids sont
/// volatils entre sessions) ; l'orchestrateur (BotsViewModel) recompose les équipes vivantes à mesure que les bots se
/// connectent. L'état « groupe formé » (Active) n'est PAS persisté : on reforme le groupe en jeu à chaque session.
/// </summary>
public static class TeamStore
{
    private static readonly string Dir =
        MZP19LocalClone.DataRoot;
    private static readonly string FilePath = Path.Combine(Dir, "teams.json");

    public static TeamsFile Load()
    {
        try
        {
            if (File.Exists(FilePath))
                return (TeamsFile?)JsonSerializer.Deserialize(File.ReadAllText(FilePath), typeof(TeamsFile), AppJsonContext.Default) ?? new();
        }
        catch { /* fichier corrompu / absent -> équipes vides */ }
        return new();
    }

    public static void Save(TeamsFile data)
    {
        try
        {
            AtomicJson.Write(FilePath, JsonSerializer.Serialize(data, typeof(TeamsFile), AppJsonContext.Default));
        }
        catch { /* best-effort */ }
    }
}

public sealed class TeamsFile
{
    public List<TeamRecord> Teams { get; set; } = new();
}

public sealed class TeamRecord
{
    public string Name { get; set; } = "";
    public string Color { get; set; } = "";           // #AARRGGBB (Color.ToString)
    public bool Follow { get; set; } = true;
    public bool CombatJoin { get; set; } = true;
    // (retirés : WaitForTeam/WaitForMap/Simultaneous/Lockstep — comportement figé côté moteur, plus de toggles. STJ ignore
    //  les clés inconnues d'anciens teams.json au chargement -> pas de migration.)
    public int SyncMin { get; set; } = 200;
    public int SyncMax { get; set; } = 800;
    public List<TeamMemberRecord> Members { get; set; } = new();
}

/// <summary>Membre d'une équipe, identité STABLE entre sessions = nom de perso + serveur (jamais le pid).</summary>
public sealed class TeamMemberRecord
{
    public string Name { get; set; } = "";
    public string Server { get; set; } = "";
    public bool Leader { get; set; }
    /// <summary>ID de classe Dofus (1-20) mémorisé la dernière fois que le membre était EN JEU -> permet d'afficher son
    /// icône de classe même HORS LIGNE dans la carte d'équipe. 0 = inconnu (icône générique).</summary>
    public int BreedId { get; set; }
    /// <summary>ID du COMPTE (Account.Id) qui pilote ce membre, mémorisé quand il était EN JEU. Permet à « Connecter en
    /// jeu » de relancer le bon compte (via SON proxy défini dans « Mon compte ») sans le nom de perso. Vide = jamais vu
    /// en jeu depuis cette machine (l'utilisateur doit le lancer une fois pour l'associer).</summary>
    public string AccountId { get; set; } = "";
}
