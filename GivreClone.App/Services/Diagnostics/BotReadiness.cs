using System;
using System.Collections.Generic;

namespace GivreClone.App.Services.Diagnostics;

/// <summary>
/// Operator-facing diagnosis from explicitly observed data. A nullable proof
/// is unknown, never an implicit success. No game traffic is sent here.
/// </summary>
public static class BotReadiness
{
    public sealed record Input(
        string Status,
        int Pid,
        long MapId,
        bool InFight,
        bool ScriptRunning,
        bool LinkSuspended,
        bool? CellConfirmed,
        bool? ProtocolVerified,
        bool? ServerAcknowledgementVerified,
        bool HdvSellReview,
        bool HdvRemoveReview,
        bool HdvRepriceReview,
        string? DisconnectReason,
        bool HdvBuyReview = false);

    public sealed record Check(string Id, string State, string Detail, string NextAction);
    public sealed record Report(string Verdict, IReadOnlyList<Check> Checks, bool MayAssumeGameplayConfirmed);

    public static Report Evaluate(Input input)
    {
        ArgumentNullException.ThrowIfNull(input);
        var checks = new List<Check>();
        void Add(string id, string state, string detail, string nextAction)
            => checks.Add(new(id, state, detail, nextAction));

        if (input.Pid <= 0 || input.Status == "offline")
            Add("session", "blocked", "Aucune session de personnage en ligne n'est observée.", "Connecter un profil puis recontrôler.");
        else if (input.LinkSuspended || input.Status == "disconnected")
            Add("session", "blocked", "Lien de jeu suspendu ou déconnecté" +
                (string.IsNullOrWhiteSpace(input.DisconnectReason) ? "." : " : " + input.DisconnectReason),
                "Consulter le journal et attendre une reconnexion vérifiée.");
        else if (input.Status == "connecting")
            Add("session", "pending", "Connexion en cours.", "Attendre l'état de session fourni par le serveur.");
        else
            Add("session", "observed", "État de session observé : " + input.Status + ".", "Confirmer la carte et l'identité de session.");

        if (input.MapId <= 0 && input.Pid > 0)
            Add("map", "unknown", "Identifiant de carte non disponible.", "Attendre un instantané serveur de la carte.");
        else if (input.MapId > 0 && input.CellConfirmed is false)
            Add("map", "unknown", "Carte connue, cellule du personnage non confirmée.", "Attendre l'information de position correspondante.");
        else if (input.MapId > 0 && input.CellConfirmed is null)
            Add("map", "unknown", "Carte connue, aucune preuve de cellule fournie au diagnostic.", "Lire l'état de la carte.");
        else if (input.MapId > 0)
            Add("map", "observed", "Carte et cellule reconnues par le modèle courant.", "Vérifier leur fraîcheur avant déplacement.");

        if (input.ProtocolVerified is not true)
            Add("protocol", "unknown", "Compatibilité protocole non certifiée pour cette session.",
                "Vérifier version, mapping et provenance du catalogue.");
        if (input.ServerAcknowledgementVerified is not true)
            Add("server-ack", "unknown", "Aucun accusé serveur positif n'a été fourni au diagnostic.",
                "Contrôler une réponse corrélée à l'action visée.");

        if (input.HdvSellReview || input.HdvRemoveReview ||
            input.HdvRepriceReview || input.HdvBuyReview)
            Add("market", "review", "Une transaction HDV présente un résultat incertain.",
                "Vérifier annonces, inventaire ET kamas dans le jeu avant le rapprochement manuel.");
        if (input.ScriptRunning && input.MapId <= 0)
            Add("script", "review", "Script actif sans carte observée.",
                "Contrôler l'état réel de la session et le journal du script.");

        bool blocked = checks.Exists(x => x.State == "blocked");
        bool review = checks.Exists(x => x.State == "review");
        bool unknown = checks.Exists(x => x.State == "unknown" || x.State == "pending");
        return new Report(blocked ? "blocked" : review ? "needs-review" : unknown ? "not-verified" : "observed",
            checks, false);
    }
}
