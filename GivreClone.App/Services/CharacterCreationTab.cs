using System;
using System.Collections.Generic;
using System.Linq;
using GivreClone.Protocol;
using GivreCore.Protobuf;

namespace GivreClone.App.Services;

/// <summary>Présence des champs de création, lus par nom dans le catalogue. Aucun numéro n'est écrit ici.</summary>
public sealed record CreationFieldPresence(
    bool Request,
    bool Name,
    bool Gender,
    bool Breed,
    bool Cosmetic,
    bool Body,
    bool Colors,
    bool Result,
    bool SuggestionRequest,
    bool Suggestion)
{
    public bool Complete => Request && Name && Gender && Breed && Cosmetic && Body && Colors && Result;
    public bool CanSuggest => SuggestionRequest && Suggestion;
    public static CreationFieldPresence None { get; } = new(false, false, false, false, false, false, false, false, false, false);
}

/// <summary>Où la création part. MitmList seulement si le client officiel est encore sur la liste et que le catalogue résout la demande.</summary>
public enum CreationRoute { Socket, MitmList, StopClientFirst }

/// <summary>Contexte de la page, sans transport. Le nom vide demande une suggestion au serveur.</summary>
public readonly record struct CreationContext(
    bool SocketSession,
    bool RememberedSocket,
    bool Running,
    bool OnCharacterList,
    bool FieldsComplete,
    bool NameBlank,
    bool SuggestionReady);

/// <summary>Résultat d'un envoi MITM tenté pendant que le client est sur la liste des personnages.</summary>
public sealed record MitmCreationResult(bool UseSocketInstead, bool Created, string Status, string? Name = null);

/// <summary>
/// Règles de la page « Créer un personnage » : validation locale du nom tapé, texte français du refus serveur,
/// et choix Socket / MITM. L'encodage des octets reste <see cref="CharacterCreationEncoder"/>.
/// </summary>
public static class CharacterCreationTab
{
    /// <summary>Nom obfusqué du champ corps de CharacterRemodelingInformation, déjà utilisé par la création Socket. Pas un numéro.</summary>
    public const string BodyField = "gccm";

    public const string CreatedText = "Personnage créé.";
    public const string SocketBecauseNotOnList = "La création se fait par une connexion Socket : le client n'est pas sur la liste des personnages.";
    public const string SocketBecauseFields = "La création se fait par une connexion Socket : le catalogue ne résout pas tous les champs de la demande.";
    public const string SocketBecauseNoSuggestion = "La création se fait par une connexion Socket : le nom proposé par le serveur ne peut pas être demandé sur la liste des personnages.";
    public const string StopClient = "Arrête d'abord le client ouvert, puis relance la création par Socket.";
    public const string StopSession = "Arrête d'abord la session en cours, puis relance la création.";

    public static CreationFieldPresence ReadFields(Func<string, string?> codeForName, Func<string, string, int?> fieldNumber)
    {
        bool Has(string message, string field) => fieldNumber(message, field) is > 0;
        bool Code(string name) => !string.IsNullOrEmpty(codeForName(name));
        return new CreationFieldPresence(
            Code("CharacterCreationRequest") && Has("CharacterCreationRequest", "modeling_information"),
            Has("CharacterRemodelingInformation", "name"),
            Has("CharacterRemodelingInformation", "gender"),
            Has("CharacterRemodelingInformation", "breed_id"),
            Has("CharacterRemodelingInformation", "cosmetic_id"),
            Has("CharacterRemodelingInformation", BodyField),
            Has("CharacterRemodelingInformation", "colors"),
            Has("CharacterCreationResultEvent", "result"),
            Code("CharacterNameSuggestionRequest"),
            Code("CharacterNameSuggestionEvent") && Has("CharacterNameSuggestionEvent", "suggestion"));
    }

    /// <summary>Null si le nom est vide (le serveur le propose) ou s'il passe la règle locale.</summary>
    public static string? NameError(string? name)
        => string.IsNullOrWhiteSpace(name) ? null : AccountsLogic.ValidateName(name);

    public static string? FormError(int serverId, int breedIndex, int sex, string? name)
    {
        if (serverId <= 0) return "Choisis un serveur.";
        if (CharacterInfoReader.BreedName(breedIndex) is null) return "Choisis une classe.";
        if (sex is not (0 or 1)) return "Choisis le sexe.";
        return NameError(name) is { } why ? "Nom : " + why : null;
    }

    /// <summary>Six RVB. Une case vide prend la couleur de classe ; sans défaut, on ne met pas 0.</summary>
    public static int[]? ResolveColors(IReadOnlyList<int>? chosen, IReadOnlyList<int>? defaults)
    {
        var colors = new int[CharacterCreationEncoder.ColorCount];
        for (int i = 0; i < colors.Length; i++)
        {
            int pick = chosen is not null && i < chosen.Count ? chosen[i] : AccountsLogic.NoColor;
            int fallback = defaults is not null && i < defaults.Count ? defaults[i] : AccountsLogic.NoColor;
            int value = pick >= 0 ? pick : fallback;
            if (value is < 0 or > 0xFFFFFF) return null;
            colors[i] = value;
        }
        return colors;
    }

    public static CreationRoute Route(CreationContext c)
    {
        bool mitm = !c.SocketSession && c.Running && c.OnCharacterList && c.FieldsComplete && (!c.NameBlank || c.SuggestionReady);
        if (mitm) return CreationRoute.MitmList;
        return c.Running ? CreationRoute.StopClientFirst : CreationRoute.Socket;
    }

    public static string Notice(CreationContext c)
    {
        if (c.Running && c.SocketSession) return StopSession;
        if (Route(c) == CreationRoute.MitmList) return "";
        if (!c.Running) return c.RememberedSocket ? "" : SocketBecauseNotOnList;
        return Why(c) + " " + StopClient;
    }

    private static string Why(CreationContext c)
    {
        if (c.OnCharacterList && !c.FieldsComplete) return SocketBecauseFields;
        if (c.OnCharacterList && c.FieldsComplete && c.NameBlank && !c.SuggestionReady) return SocketBecauseNoSuggestion;
        return SocketBecauseNotOnList;
    }

    /// <summary>Null si le serveur a accepté (résultat absent ou 0). Le détail numérique n'est ajouté que pour un nom refusé.</summary>
    public static string? RefusalText(long result, long? reason)
    {
        if (result == 0) return null;
        string text = result switch
        {
            1 => "Le serveur a refusé la création, sans raison indiquée.",
            2 => "Le serveur a refusé ce nom.",
            3 => "Le serveur a refusé la création : trop de personnages sur ce compte.",
            4 => "Le serveur a refusé la création : elle est interdite.",
            _ => $"Le serveur a refusé la création (résultat {result}).",
        };
        if (result == 2 && reason is > 0) text += $" Détail du serveur : {reason}.";
        return text;
    }

    public static string? Refusal(IReadOnlyList<ProtoField>? payload, Func<string, string, int?> fieldNumber)
    {
        long result = 0;
        int? resultField = fieldNumber("CharacterCreationResultEvent", "result");
        if (resultField is > 0 && payload?.FirstOrDefault(f => f.Field == resultField && f.Wire == WireType.Varint) is { } hit)
            result = (long)hit.Varint;
        long? reason = null;
        int? reasonField = fieldNumber("CharacterCreationResultEvent", "reason");
        if (reasonField is > 0 && payload?.FirstOrDefault(f => f.Field == reasonField && f.Wire == WireType.Varint) is { } why)
            reason = (long)why.Varint;
        return RefusalText(result, reason);
    }

    public static string? SuggestionName(IReadOnlyList<ProtoField>? payload, Func<string, string, int?> fieldNumber)
    {
        if (payload is null) return null;
        int? field = fieldNumber("CharacterNameSuggestionEvent", "suggestion");
        if (field is not > 0) return null;
        string? text = PayloadReader.Str(payload, field.Value);
        return string.IsNullOrWhiteSpace(text) ? null : text.Trim();
    }

    public static long? SuggestionFailureReason(IReadOnlyList<ProtoField>? payload, Func<string, string, int?> fieldNumber)
    {
        if (payload is null) return null;
        int? field = fieldNumber("CharacterNameGenerationFailedEvent", "reason");
        if (field is not > 0) return null;
        var hit = payload.FirstOrDefault(f => f.Field == field && f.Wire == WireType.Varint);
        return hit is null ? null : (long)hit.Varint;
    }

    public static string SuggestionFailed(long? reason)
    {
        var text = "Le serveur n'a pas proposé de nom.";
        if (reason is > 0) text += $" Détail du serveur : {reason}.";
        return text;
    }
}

/// <summary>La session Socket publie ici le résultat, pour l'afficher sur le compte qui a demandé la création.</summary>
public static class CharacterCreationReports
{
    public static event Action<string, string, string?>? Reported;

    public static void Publish(string? login, string? text, string? name = null)
    {
        if (string.IsNullOrWhiteSpace(login) || string.IsNullOrWhiteSpace(text)) return;
        try { Reported?.Invoke(login, text, string.IsNullOrWhiteSpace(name) ? null : name); }
        catch { /* un abonné ne doit pas couper la session */ }
    }
}
