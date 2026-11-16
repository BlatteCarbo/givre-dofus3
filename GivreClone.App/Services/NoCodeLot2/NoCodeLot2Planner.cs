using System;
using System.Collections.Generic;
using System.Linq;

namespace GivreClone.App.Services.NoCodeLot2;

/// <summary>Actions de conception uniquement : aucune commande de jeu ou d'HDV n'est exposée.</summary>
public enum RoutineKind { MineurGrotte, Donjon, HdvObservation, Combat, Banque }
public enum RoutineAction { Locate, Traverse, Harvest, CheckKey, InspectHdv, CompareQuote, CheckEncounter, SimulateCombat, Deposit }
public enum EvidenceVerdict { Verified, MissingEvidence, InvalidInput }

public sealed record RoutineField(string Key, string Caption, string EvidenceExpected);
public sealed record RoutineStep(string Label, RoutineAction Action,
    string? MapField = null, string? DestinationField = null, string? ObjectField = null);
public sealed record RoutineTemplate(string Id, string Title, RoutineKind Kind,
    IReadOnlyList<RoutineField> Fields, IReadOnlyList<RoutineStep> Steps);

/// <summary>Les valeurs ci-dessous sont déclarées par l'utilisateur : elles ne deviennent vérifiées
/// que si elles correspondent à une preuve fournie séparément.</summary>
public sealed record RoutineInputs(IReadOnlyDictionary<string, long> Values);

/// <summary>Snapshot explicitement sourcé par l'intégrateur. Listes vides = aucune preuve.
/// Un prix observé n'autorise PAS un achat. Aucun accès direct aux sources ou au client.</summary>
public sealed record RoutineEvidence(
    IReadOnlySet<long> KnownMapIds,
    IReadOnlySet<(long From, long To)> KnownTransitions,
    IReadOnlySet<long> KnownItemGids,
    IReadOnlySet<long> KnownResourceTypeIds,
    IReadOnlySet<long> KnownMonsterIds,
    IReadOnlyDictionary<long, decimal> ObservedUnitPrices);

public sealed record RoutineStepReport(int Number, string Label, RoutineAction Action,
    EvidenceVerdict Verdict, string Reason);

/// <summary>Plan consultatif. Ni code Lua, ni ACK d'une action, ni estimation de trajet/loot/dégâts.</summary>
public sealed record RoutinePreview(string TemplateId, IReadOnlyList<RoutineStepReport> Steps,
    EvidenceVerdict Verdict, bool Executed, bool Acknowledged, string Declaration)
{
    public bool HasUnverifiedSteps => Steps.Any(s => s.Verdict != EvidenceVerdict.Verified);
}

/// <summary>Bibliothèque de blocs présentables dans un assistant visuel.
/// Les références de carte/grotte/clé sont volontairement absentes et doivent venir d'une source prouvée.</summary>
public static class RoutineLibrary
{
    private static readonly RoutineTemplate[] Templates =
    {
        new("mineur-grotte", "Mineur · exploration de grotte", RoutineKind.MineurGrotte,
            new [] { Field("surface", "Carte de surface", "MapId vérifié"), Field("entree", "Entrée de grotte", "MapId et transition"),
                Field("interieur", "Intérieur de grotte", "MapId et transition"), Field("minerai", "Ressource", "ElementTypeId attesté"),
                Field("banque", "Banque", "MapId vérifié") },
            new [] { S("Trouver la surface", RoutineAction.Locate, "surface"),
                S("Atteindre l'entrée", RoutineAction.Traverse, "surface", "entree"),
                S("Traverser vers la grotte", RoutineAction.Traverse, "entree", "interieur"),
                S("Identifier le minerai", RoutineAction.Harvest, "interieur", obj: "minerai"),
                S("Retour extérieur", RoutineAction.Traverse, "interieur", "entree"),
                S("Planifier le dépôt", RoutineAction.Deposit, "banque") }),
        new("donjon", "Donjon · préparation d'un passage", RoutineKind.Donjon,
            new [] { Field("entree", "Entrée", "MapId vérifié"), Field("salle", "Salle/transition", "MapId réel"),
                Field("cle", "Clé", "GID attesté"), Field("monstre", "Boss", "Identifiant prouvé") },
            new [] { S("Entrée du donjon", RoutineAction.Locate, "entree"),
                S("Vérifier la clé", RoutineAction.CheckKey, obj: "cle"),
                S("Contrôler la transition de salle", RoutineAction.Traverse, "entree", "salle"),
                S("Évaluer la rencontre", RoutineAction.CheckEncounter, obj: "monstre") }),
        new("hdv", "HDV · observer sans acheter", RoutineKind.HdvObservation,
            new [] { Field("objet", "Objet", "GID attesté"), Field("quantite", "Quantité", "Quantité strictement positive") },
            new [] { S("Identifier l'objet", RoutineAction.InspectHdv, obj: "objet"),
                S("Comparer le prix observé", RoutineAction.CompareQuote, obj: "objet") }),
        new("combat", "Combat · simulation de préparation", RoutineKind.Combat,
            new [] { Field("carte", "Carte", "MapId prouvé"), Field("monstre", "Monstre", "Identifiant vérifié") },
            new [] { S("Repérer la carte", RoutineAction.Locate, "carte"),
                S("Vérifier le monstre", RoutineAction.CheckEncounter, obj: "monstre"),
                S("Préparer les règles de simulation", RoutineAction.SimulateCombat, obj: "monstre") }),
        new("banque", "Banque · tournée de dépôt", RoutineKind.Banque,
            new [] { Field("depart", "Départ", "MapId attesté"), Field("banque", "Banque", "MapId attesté") },
            new [] { S("Départ", RoutineAction.Locate, "depart"),
                S("Aller vers la banque", RoutineAction.Traverse, "depart", "banque"),
                S("Préparer le dépôt", RoutineAction.Deposit, "banque") })
    };
    public static IReadOnlyList<RoutineTemplate> All { get; } = Array.AsReadOnly(Templates);
    public static RoutineTemplate? Find(string? id) =>
        All.FirstOrDefault(t => string.Equals(t.Id, id, StringComparison.Ordinal));
    private static RoutineField Field(string key, string caption, string evidence) => new(key, caption, evidence);
    private static RoutineStep S(string title, RoutineAction action, string? map = null, string? to = null, string? obj = null)
        => new(title, action, map, to, obj);
}

/// <summary>Simulation offline déterministe : évalue les blocages, n'envoie jamais d'action.</summary>
public static class RoutinePlanner
{
    public static RoutinePreview Preview(RoutineTemplate template, RoutineInputs inputs, RoutineEvidence evidence)
    {
        ArgumentNullException.ThrowIfNull(template);
        ArgumentNullException.ThrowIfNull(inputs);
        ArgumentNullException.ThrowIfNull(inputs.Values);
        ArgumentNullException.ThrowIfNull(evidence);
        ArgumentNullException.ThrowIfNull(evidence.KnownMapIds);
        ArgumentNullException.ThrowIfNull(evidence.KnownTransitions);
        ArgumentNullException.ThrowIfNull(evidence.KnownItemGids);
        ArgumentNullException.ThrowIfNull(evidence.KnownResourceTypeIds);
        ArgumentNullException.ThrowIfNull(evidence.KnownMonsterIds);
        ArgumentNullException.ThrowIfNull(evidence.ObservedUnitPrices);
        if (!RoutineLibrary.All.Contains(template))
            throw new ArgumentException("Template non approuvé par la bibliothèque.", nameof(template));
        if (template.Steps.Count > 32) throw new ArgumentException("Routine trop longue.", nameof(template));

        (EvidenceVerdict Verdict, string Why) Need(string? field, IReadOnlySet<long>? authoritative, string evidenceName)
        {
            if (field is null) return (EvidenceVerdict.Verified, "Aucun identifiant requis.");
            if (!inputs.Values.TryGetValue(field, out long value))
                return (EvidenceVerdict.MissingEvidence, $"Valeur « {field} » absente.");
            if (value <= 0) return (EvidenceVerdict.InvalidInput, $"Valeur « {field} » invalide.");
            if (authoritative is not null && !authoritative.Contains(value))
                return (EvidenceVerdict.MissingEvidence, $"{evidenceName} non vérifié : « {field} ».");
            return (EvidenceVerdict.Verified, "Identifiant confirmé par le snapshot fourni.");
        }

        var output = new List<RoutineStepReport>();
        for (int i = 0; i < template.Steps.Count; i++)
        {
            var step = template.Steps[i];
            var checks = new List<(EvidenceVerdict Verdict, string Why)>();
            if (step.MapField is not null) checks.Add(Need(step.MapField, evidence.KnownMapIds, "Carte"));
            if (step.DestinationField is not null) checks.Add(Need(step.DestinationField, evidence.KnownMapIds, "Destination"));
            switch (step.Action)
            {
                case RoutineAction.Harvest:
                    checks.Add(Need(step.ObjectField, evidence.KnownResourceTypeIds, "Ressource"));
                    break;
                case RoutineAction.CheckKey:
                case RoutineAction.InspectHdv:
                case RoutineAction.CompareQuote:
                    checks.Add(Need(step.ObjectField, evidence.KnownItemGids, "Objet"));
                    if (step.Action == RoutineAction.CompareQuote)
                    {
                        checks.Add(Need("quantite", null, "Quantité"));
                        if (inputs.Values.TryGetValue(step.ObjectField ?? "", out long gid) &&
                            (!evidence.ObservedUnitPrices.TryGetValue(gid, out decimal quote) || quote < 0))
                            checks.Add((EvidenceVerdict.MissingEvidence, "Aucun prix HDV observé (ne jamais inventer un prix)."));
                    }
                    break;
                case RoutineAction.CheckEncounter:
                case RoutineAction.SimulateCombat:
                    checks.Add(Need(step.ObjectField, evidence.KnownMonsterIds, "Monstre"));
                    break;
            }
            if (step.Action == RoutineAction.Traverse && step.MapField is { } fromField &&
                step.DestinationField is { } toField &&
                inputs.Values.TryGetValue(fromField, out long from) &&
                inputs.Values.TryGetValue(toField, out long to) &&
                !evidence.KnownTransitions.Contains((from, to)))
                checks.Add((EvidenceVerdict.MissingEvidence, "Transition, porte ou entrée de grotte non vérifiée."));
            // Connaître un mapId ne prouve pas l'existence d'un PNJ de banque ni la réussite d'une interaction.
            if (step.Action == RoutineAction.Deposit)
                checks.Add((EvidenceVerdict.MissingEvidence, "Présence du banquier/accusé de dépôt non attestée."));
            if (step.Action == RoutineAction.SimulateCombat)
                checks.Add((EvidenceVerdict.MissingEvidence, "Dégâts, victoire, sorts et contraintes non simulables sans données attestées."));
            if (step.Action == RoutineAction.Harvest)
                checks.Add((EvidenceVerdict.MissingEvidence, "Présence, cellule et disponibilité de la ressource non attestées."));

            EvidenceVerdict verdict = checks.Any(c => c.Verdict == EvidenceVerdict.InvalidInput)
                ? EvidenceVerdict.InvalidInput : checks.Any(c => c.Verdict == EvidenceVerdict.MissingEvidence)
                    ? EvidenceVerdict.MissingEvidence : EvidenceVerdict.Verified;
            string reason = string.Join(" ", checks.Where(c => c.Verdict != EvidenceVerdict.Verified).Select(c => c.Why));
            output.Add(new(i + 1, step.Label, step.Action, verdict,
                reason.Length == 0 ? "Données d'entrée vérifiées ; aucune action exécutée." : reason));
        }
        var all = output.Any(s => s.Verdict == EvidenceVerdict.InvalidInput) ? EvidenceVerdict.InvalidInput
            : output.Any(s => s.Verdict == EvidenceVerdict.MissingEvidence) ? EvidenceVerdict.MissingEvidence
            : EvidenceVerdict.Verified;
        return new(template.Id, output.AsReadOnly(), all, false, false,
            "PLAN CONSULTATIF UNIQUEMENT : aucune simulation ne confirme une action, un déplacement, un achat ou un ACK serveur.");
    }
}
