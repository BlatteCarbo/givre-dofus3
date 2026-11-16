using System;
using System.Collections.Generic;
using System.Text;

namespace GivreClone.Protocol;

/// <summary>
/// Helpers PURS de la compatibilité tiers de l'API Lua (placés dans Protocol pour être testables): variantes de nom façon
/// MoonSharp userdata (sale:sellItem -&gt; SellItem, map:change_map -&gt; changeMap), conversion camelCase -&gt; snake_case
/// (msg.messageId -&gt; message_id), réduction d'un chemin de script tiers à son nom, et candidats Dofus 3 d'un nom de message
/// Dofus 2 (« NpcDialogQuestionMessage » -&gt; NpcDialogQuestionEvent).
/// </summary>
public static class LuaSnowCompat
{
    /// <summary>Variantes d'une clé absente, dans l'ordre essayé : 1re lettre en majuscule, en minuscule, puis snake_case -&gt;
    /// camelCase et sa forme PascalCase. La clé elle-même n'est pas renvoyée.</summary>
    public static IEnumerable<string> NameCandidates(string k)
    {
        if (string.IsNullOrEmpty(k)) yield break;
        string up = char.ToUpperInvariant(k[0]) + k[1..];
        string low = char.ToLowerInvariant(k[0]) + k[1..];
        if (up != k) yield return up;
        if (low != k) yield return low;
        if (k.IndexOf('_') < 0) yield break;
        var sb = new StringBuilder(k.Length);
        bool next = false;
        foreach (char c in k)
        {
            if (c == '_') { next = sb.Length > 0; continue; }
            sb.Append(next ? char.ToUpperInvariant(c) : c);
            next = false;
        }
        string camel = sb.ToString();
        if (camel.Length == 0) yield break;
        if (camel != k) yield return camel;
        string pascal = char.ToUpperInvariant(camel[0]) + camel[1..];
        if (pascal != k && pascal != camel) yield return pascal;
    }

    /// <summary>camelCase / PascalCase -&gt; snake_case (messageId -&gt; message_id, VisibleReplies -&gt; visible_replies).
    /// Une clé déjà en snake_case est rendue telle quelle.</summary>
    public static string SnakeCase(string k)
    {
        if (string.IsNullOrEmpty(k)) return k ?? "";
        var sb = new StringBuilder(k.Length + 4);
        for (int i = 0; i < k.Length; i++)
        {
            char c = k[i];
            if (char.IsUpper(c))
            {
                if (i > 0 && k[i - 1] != '_' && !char.IsUpper(k[i - 1])) sb.Append('_');
                else if (i > 0 && char.IsUpper(k[i - 1]) && i + 1 < k.Length && char.IsLower(k[i + 1])) sb.Append('_');
                sb.Append(char.ToLowerInvariant(c));
            }
            else sb.Append(c);
        }
        return sb.ToString();
    }

    /// <summary>Chemin un bot tiers (« C:\…\trajets\ble.lua », « lib/quest.lua ») -&gt; nom de script sans extension (« ble »).
    /// Aucun chemin disque n'est jamais lu : seul le nom sert à résoudre le script dans la bibliothèque.</summary>
    public static string ScriptNameFromPath(string? raw)
    {
        string t = (raw ?? "").Trim().Trim('"');
        if (t.Length == 0) return "";
        int i = Math.Max(t.LastIndexOf('\\'), t.LastIndexOf('/'));
        string file = i >= 0 ? t[(i + 1)..] : t;
        return file.EndsWith(".lua", StringComparison.OrdinalIgnoreCase) ? file[..^4] : file;
    }

    /// <summary>Candidats Dofus 3 d'un nom de message saisi par un script, dans l'ordre : identifiant PUBLIC Dofus 2 traduit
    /// en nom, nom tel quel, alias Dofus 2 -&gt; Dofus 3, puis règle « XxxMessage » -&gt; XxxEvent, XxxRequest,
    /// XxxResponse, Xxx.</summary>
    public static IEnumerable<string> MessageNameCandidates(string? name)
    {
        string n = (name ?? "").Trim();
        if (n.Length == 0) yield break;
        if (int.TryParse(n, System.Globalization.NumberStyles.None, System.Globalization.CultureInfo.InvariantCulture, out int id)
            && LuaMessageAliases.D2Ids.TryGetValue(id, out var d2)) n = d2;
        yield return n;
        if (LuaMessageAliases.D2ToD3.TryGetValue(n, out var d3)) yield return d3;
        if (LegacyNames.TryGetValue(n, out var v5)) yield return v5;
        if (n.EndsWith("Message", StringComparison.Ordinal) && n.Length > 7)
        {
            string b = n[..^7];
            yield return b + "Event";
            yield return b + "Request";
            yield return b + "Response";
            yield return b;
        }
    }

    // Anciens noms INTERNES de GivreClone (pont code->nom de confirmed.json, retiré le 04/10 : matching_current.json fait foi) ->
    // nom v5. Un script qui écoutait l'ancien nom (registerMessage, waitFor…) continue de marcher.
    private static readonly Dictionary<string, string> LegacyNames = new(StringComparer.OrdinalIgnoreCase)
    {
        ["GameFightShowFighter"] = "FightFighterShowEvent",
        ["GameRolePlayShowActor"] = "GameRolePlayShowActorsEvent",
        ["GameContextRemoveElement"] = "ContextRemoveElementEvent",
        ["CharacterInformation"] = "CharacterListEvent",
        ["ExchangeStartedBidSellerEvent"] = "ExchangeBidSellerStartedEvent",
        ["ExchangeStartedBidBuyerEvent"] = "ExchangeBidBuyerStartedEvent",
        ["ExchangeBidPriceForSellerEvent"] = "ExchangeBidPriceEvent",
        ["ExchangeObjectMoveKamaRequest"] = "ExchangeMoveKamaRequest",
    };

    /// <summary>« Pas d'opération » Dofus 2 (id 176): Un bot tiers l'attendait comme une pause d'environ 200 ms.</summary>
    public static bool IsNoOperation(string? name) => (name ?? "").Trim() is "BasicNoOperationMessage" or "176";

    // Messages porteurs de SECRETS de session ou de données de compte (inventaire de data/fields.json + clear proto) :
    // jetons (relogin, HAAPI, BAK, bascule vers le serveur de combat), ticket d'identification, cert Shield, question /
    // réponse secrète de suppression de personnage, codes de serrure (maison / coffre), e-mail du compte (BAK).
    private static readonly HashSet<string> SensitiveNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "IdentificationRequest", "LoginRequest", "TokenRequest", "Shield", "ReloginRequest", "ReloginTokenEvent",
        "ReconnectToGameServerEvent", "ArenaSwitchToFightServerEvent",
        "HaapiTokenEvent", "HaapiTokenRequest", "HaapiSessionEvent", "HaapiAuthenticationErrorEvent",
        "BakApiKeyEvent", "BakApiTokenRequest", "BakShopTokenEvent", "BakShopTokenRequest", "BakBuyValidationEvent",
        "CharacterDeletionPrepareEvent", "CharacterDeletionRequest",
        "LockableChangeCodeRequest", "LockableUseCodeRequest", "HouseLockFromInsideRequest",
        // Poignée de main du client : identifiant d'installation, clé et preuve du défi de connexion (empreinte du poste).
        "ClientIdRequest", "ClientChallengeInitRequest", "ClientChallengeProofRequest",
    };

    // Fragments de nom refusés quel que soit le message (renommage / nouveau message après un patch : on refuse par défaut).
    // Pas de « shield » ni « code » seuls : ShieldPoints (combat) et LockableCodeResult (résultat, sans code) restent lisibles.
    private static readonly string[] SensitiveParts =
    {
        "token", "apikey", "ticket", "identification", "authentication", "credential", "password", "passwd",
        "securitycode", "secret", "relogin", "certificate", "haapisession",
    };

    /// <summary>true si le message (nom CLAIR Dofus 3) transporte un secret de session ou une donnée de compte : jeton,
    /// clé d'API, ticket d'authentification, identification / identifiants, cert Shield, code de sécurité / de serrure,
    /// réponse secrète, e-mail. Ces messages ne sont JAMAIS exposés à Lua (hooks, attentes, historique, dernier reçu).</summary>
    public static bool IsSensitiveMessage(string? name)
    {
        string n = (name ?? "").Trim();
        if (n.Length == 0) return false;
        if (SensitiveNames.Contains(n)) return true;
        foreach (var part in SensitiveParts)
            if (n.Contains(part, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }
}
