using System;
using System.Collections.Generic;

namespace GivreClone.App.Services.Transport.Socket;

// Résultat décodé d'un LoginMessage reçu du serveur de connexion (seuls les champs utiles à l'étape A).
internal sealed class ConnCharacter
{
    public string Name = "";
    public int Breed;             // enum Breed (0 = FECA … 18 = FORGELANCE) ; proto3 : 0 = champ OMIS -> Féca
    public int Gender;            // 0 = MALE, 1 = FEMALE
    public int Level;
    public string LastConnectionDate = "";
    public int ServerId;
}

internal sealed class ConnServer
{
    public bool Online => Status is 0 or 1;
    /// <summary>Statut lisible. Observé en 3.7.11.11 : champ absent (0) pour des serveurs que l'original affiche « en ligne » ; 2 = maintenance (3.6.12.16, non revérifié) ; tout autre : brut.</summary>
    public string StatusText => Status switch { 0 => "en ligne", 2 => "maintenance", _ => $"statut {Status}" };
    public int Id;
    public int Status;            // 0 = en ligne (3.7 observé), 1 = ONLINE (3.6), 2 = MAINTENANCE
    public bool MonoAccount;      // serveur mono-compte
    public int Accessibility;     // 0 ACCESSIBLE, 1 SUBSCRIBE_RESTRICTION, 2 MONO_ACCOUNT_RESTRICTION, 3 UNKNOWN
    public readonly List<ConnCharacter> Characters = new();
}

internal sealed class ConnDecoded
{
    public enum Kind { Unknown, IdentificationSuccess, IdentificationError, SelectServerSuccess, SelectServerError, CharacterList, Pong, Event }
    public Kind Type = Kind.Unknown;

    // identification success
    public string AccountNickname = "";
    public string SubscriptionEndRaw = "";          // IdentificationSuccess.subscription_end_date (chaîne de date, champ 5)
    public bool UnlimitedAccess, InfiniteSubscription;   // IdentificationSuccess.rights (champ 6 : 3 et 4)
    public readonly List<ConnServer> Servers = new();

    // identification error
    public int ErrorReason;               // enum IdentificationResponse.Error.Reason
    public string RequiredVersion = "";
    public string BanEndDate = "";

    // select server success
    public string GameToken = "";
    public string GameHost = "";
    public readonly List<int> GamePorts = new();
    public int SelectedServerId;          // serveur choisi à l'étape A (déterministe) -> renseigne row.Server sans WhoAmI

    public int SelectServerError = -1;    // enum SelectServerResponse.Error
    public bool CharactersEmbedded;       // 3.7.11.11 : personnages dans la réponse d'identification (sous chaque serveur)
}

/// <summary>
/// Encode/décode les messages du protocole de connexion Dofus 3
/// (com.ankama.dofus.server.connection.protocol), numéros de champ figés d'après le .proto de référence.
/// LoginMessage = oneof { request(1), response(2), event(3) }.
/// </summary>
internal static class ConnectionMessages
{
    // ---------- ENCODAGE (client -> serveur) ----------

    /// <summary>LoginMessage{ request{ uuid, identification{ device_identifier, client_version, tokenRequest{ token, shield } } } }.</summary>
    public static byte[] BuildIdentification(string uuid, string deviceId, string clientVersion,
                                             string token, long certificateId, string certificateHash)
    {
        // TokenRequest.Shield : certificateId(1 varint), certificateHash(2 string). Encodage CANONIQUE proto3 comme le vrai
        // client (Google.Protobuf) : valeurs par défaut OMISES -> sans cert, un Shield PRÉSENT mais VIDE (« 12 00 »), jamais
        // « 08 00 12 00 ». Vérifié sur la taille des trames réelles (127 o, 138 o avec la sélection serveur).
        var shield = new ProtoWire.Writer();
        if (certificateId != 0) shield.Int64(1, certificateId);
        if (!string.IsNullOrEmpty(certificateHash)) shield.String(2, certificateHash);

        // TokenRequest : token(1 string), shield(2 message)
        var tokenReq = new ProtoWire.Writer();
        tokenReq.String(1, token);
        tokenReq.Message(2, shield.ToArray());

        // IdentificationRequest : device_identifier(1), tokenRequest(3), client_version(5)
        var ident = new ProtoWire.Writer();
        ident.String(1, deviceId);
        ident.Message(3, tokenReq.ToArray());
        ident.String(5, clientVersion);

        // Request : uuid(1), identification(3)
        var request = new ProtoWire.Writer();
        request.String(1, uuid);
        request.Message(3, ident.ToArray());

        // LoginMessage : request(1)
        var login = new ProtoWire.Writer();
        login.Message(1, request.ToArray());
        return login.ToFramed();
    }

    /// <summary>LoginMessage{ request{ uuid, selectServer{ server } } }.</summary>
    public static byte[] BuildSelectServer(string uuid, int serverId)
    {
        var sel = new ProtoWire.Writer();
        sel.Int32(1, serverId);

        var request = new ProtoWire.Writer();
        request.String(1, uuid);
        request.Message(4, sel.ToArray());

        var login = new ProtoWire.Writer();
        login.Message(1, request.ToArray());
        return login.ToFramed();
    }

    // ---------- LECTURE PASSIVE (client -> serveur) ----------

    /// <summary>
    /// Extrait <c>device_identifier</c> d'un corps LoginMessage (déjà déframé) émis par le CLIENT, SANS rien modifier.
    /// Chemin : request(1) -> identification(3) -> { device_identifier(1 string), tokenRequest(3), client_version(5 string) }.
    /// Renvoie false si la trame n'est pas une IdentificationRequest. La présence CONJOINTE de device_identifier ET
    /// client_version sert de garde structurelle : aucune trame de JEU ne matche ce chemin par hasard. Ne lève jamais.
    /// </summary>
    public static bool TryReadDeviceIdentifier(ReadOnlySpan<byte> body, out string deviceId)
    {
        deviceId = "";
        try
        {
            var r = new ProtoWire.Reader(body);
            while (!r.End)
            {
                var (field, wire) = r.Tag();
                if (field == 1 && wire == 2) return TryReadRequestForDevice(r.Bytes(), out deviceId);   // request
                r.Skip(wire);
            }
        }
        catch { /* trame partielle / non-login : pas une identification */ }
        return false;
    }

    private static bool TryReadRequestForDevice(ReadOnlySpan<byte> request, out string deviceId)
    {
        deviceId = "";
        var r = new ProtoWire.Reader(request);
        while (!r.End)
        {
            var (field, wire) = r.Tag();
            if (field == 3 && wire == 2) return TryReadIdentificationForDevice(r.Bytes(), out deviceId);   // identification
            r.Skip(wire);
        }
        return false;
    }

    private static bool TryReadIdentificationForDevice(ReadOnlySpan<byte> ident, out string deviceId)
    {
        deviceId = "";
        bool hasDevice = false, hasVersion = false;
        var r = new ProtoWire.Reader(ident);
        while (!r.End)
        {
            var (field, wire) = r.Tag();
            if (field == 1 && wire == 2) { deviceId = r.String(); hasDevice = true; }        // device_identifier
            else if (field == 5 && wire == 2) { r.Bytes(); hasVersion = true; }              // client_version (présence -> garde)
            else r.Skip(wire);
        }
        // Une vraie IdentificationRequest porte les DEUX (device_identifier non vide + client_version).
        return hasDevice && hasVersion && deviceId.Length > 0;
    }

    // ---------- DÉCODAGE (serveur -> client) ----------

    /// <summary>Décode un corps LoginMessage (déjà déframé, sans le préfixe de longueur).</summary>
    public static ConnDecoded Decode(ReadOnlySpan<byte> body)
    {
        var d = new ConnDecoded();
        var r = new ProtoWire.Reader(body);
        while (!r.End)
        {
            var (field, wire) = r.Tag();
            if (field == 2 && wire == 2) { DecodeResponse(r.Bytes(), d); }
            else if (field == 3 && wire == 2) { d.Type = ConnDecoded.Kind.Event; r.Bytes(); }
            else r.Skip(wire);
        }
        return d;
    }

    private static void DecodeResponse(ReadOnlySpan<byte> resp, ConnDecoded d)
    {
        var r = new ProtoWire.Reader(resp);
        while (!r.End)
        {
            var (field, wire) = r.Tag();
            switch (field)
            {
                case 2 when wire == 2: d.Type = ConnDecoded.Kind.Pong; r.Bytes(); break;   // pong
                case 3 when wire == 2: DecodeIdentificationResponse(r.Bytes(), d); break;   // identification
                case 4 when wire == 2: DecodeSelectServerResponse(r.Bytes(), d); break;     // selectServer
                case 6 when wire == 2: DecodeCharacterListResponse(r.Bytes(), d); break;    // character_list_response
                default: r.Skip(wire); break;
            }
        }
    }

    // IdentificationResponse : success(1) | error(2)
    private static void DecodeIdentificationResponse(ReadOnlySpan<byte> body, ConnDecoded d)
    {
        var r = new ProtoWire.Reader(body);
        while (!r.End)
        {
            var (field, wire) = r.Tag();
            if (field == 1 && wire == 2) { d.Type = ConnDecoded.Kind.IdentificationSuccess; DecodeIdSuccess(r.Bytes(), d); }
            else if (field == 2 && wire == 2) { d.Type = ConnDecoded.Kind.IdentificationError; DecodeIdError(r.Bytes(), d); }
            else r.Skip(wire);
        }
    }

    // Success : account_nickname(2), server_list(4)
    private static void DecodeIdSuccess(ReadOnlySpan<byte> body, ConnDecoded d)
    {
        var r = new ProtoWire.Reader(body);
        while (!r.End)
        {
            var (field, wire) = r.Tag();
            if (field == 2 && wire == 2) d.AccountNickname = r.String();
            else if (field == 4 && wire == 2) DecodeServerList(r.Bytes(), d);
            else if (field == 5 && wire == 2) d.SubscriptionEndRaw = r.String();
            else if (field == 6 && wire == 2) DecodeRights(r.Bytes(), d);
            else r.Skip(wire);
        }
    }

    // Rights : show_force_account(1), show_console(2), unlimited_access(3), infinite_subscription(4), report(5)  (numérotation 3.7.4.4 de GivreActive)
    private static void DecodeRights(ReadOnlySpan<byte> body, ConnDecoded d)
    {
        var r = new ProtoWire.Reader(body);
        while (!r.End)
        {
            var (field, wire) = r.Tag();
            if (field == 3 && wire == 0) d.UnlimitedAccess = r.Varint() != 0;
            else if (field == 4 && wire == 0) d.InfiniteSubscription = r.Varint() != 0;
            else r.Skip(wire);
        }
    }

    // ServerList : servers(1 repeated)
    private static void DecodeServerList(ReadOnlySpan<byte> body, ConnDecoded d)
    {
        var r = new ProtoWire.Reader(body);
        while (!r.End)
        {
            var (field, wire) = r.Tag();
            if (field == 1 && wire == 2) d.Servers.Add(DecodeServerInformation(r.Bytes(), d));
            else r.Skip(wire);
        }
    }

    // ServerInformation 3.7.11.11 (trame réelle, 2026-10-17) : server(1), accessibility(2 enum, absent = 0), characters(3, répété).
    // En 3.6.12.16 les personnages arrivaient dans une CharacterListResponse séparée ; en 3.7.11.11 ils sont DANS la réponse d'identification,
    // sous chaque serveur (même disposition de personnage : nom 1, classe 2, sexe 3, niveau 4, dernière connexion 5 ISO).
    private static ConnServer DecodeServerInformation(ReadOnlySpan<byte> body, ConnDecoded d)
    {
        var s = new ConnServer();
        var r = new ProtoWire.Reader(body);
        while (!r.End)
        {
            var (field, wire) = r.Tag();
            if (field == 1 && wire == 2) DecodeServer(r.Bytes(), s);
            else if (field == 2 && wire == 0) s.Accessibility = (int)r.Varint();
            else if (field == 3 && wire == 2) { var c = DecodeCharacter(r.Bytes()); c.ServerId = s.Id; if (!string.IsNullOrEmpty(c.Name)) { s.Characters.Add(c); d.CharactersEmbedded = true; } }
            else r.Skip(wire);
        }
        // le serveur peut annoncer ses personnages AVANT son id : on rattache après coup
        foreach (var c in s.Characters) c.ServerId = s.Id;
        return s;
    }

    // Server : id(1), status(2 ; ABSENT = 0 = en ligne d'après l'original en 3.7.11.11), type(3 en 3.7.11.11 ; 3.6.12.16 : limited 3, type 4), mono_account(5 en 3.6.12.16)
    private static void DecodeServer(ReadOnlySpan<byte> body, ConnServer s)
    {
        var r = new ProtoWire.Reader(body);
        while (!r.End)
        {
            var (field, wire) = r.Tag();
            if (field == 1 && wire == 0) s.Id = (int)r.Varint();
            else if (field == 2 && wire == 0) s.Status = (int)r.Varint();
            else if (field == 5 && wire == 0) s.MonoAccount = r.Varint() != 0;
            else r.Skip(wire);
        }
    }

    /// <summary>
    /// Structure d'une trame, pour le diagnostic SANS secret : numéros de champ, entiers PETITS (&lt; 1 000 000 : niveaux, énumérations, identifiants de serveur) et
    /// longueurs des champs texte ; un entier plus grand est masqué (identifiants de compte ou de personnage) et un texte n'est jamais affiché.
    /// Un champ à longueur variable est lu comme sous-message seulement s'il se décode EXACTEMENT (heuristique) ; sinon il est rendu « &lt;N octets&gt; ».
    /// </summary>
    internal static string Shape(ReadOnlySpan<byte> body, int depth = 0)
    {
        var sb = new System.Text.StringBuilder("{");
        try
        {
            var r = new ProtoWire.Reader(body);
            int n = 0;
            while (!r.End)
            {
                var (field, wire) = r.Tag();
                if (field <= 0 || field > 200) return "<non protobuf>";
                if (n++ > 0) sb.Append(' ');
                switch (wire)
                {
                    case 0:
                    {
                        ulong v = r.Varint();
                        sb.Append('#').Append(field).Append('=').Append(v < 1_000_000 ? v.ToString() : "<grand>");
                        break;
                    }
                    case 2:
                    {
                        var inner = r.Bytes();
                        string sub = depth < 6 && inner.Length > 1 ? Shape(inner, depth + 1) : "<non protobuf>";
                        // Blocs PROFONDS et courts (entrées de la liste des serveurs : serveur, accessibilité, personnage) : octets en hexadécimal pour fixer la disposition ;
                        // les champs de tête (pseudo du compte...) restent masqués par la profondeur.
                        string raw = depth >= 4 && inner.Length <= 64 ? $"=<{inner.Length} octets: {Convert.ToHexString(inner)}>" : $"=<{inner.Length} octets>";
                        sb.Append('#').Append(field).Append(sub != "<non protobuf>" && sub.Length > 2 ? ":" + sub : raw);
                        break;
                    }
                    default:
                        r.Skip(wire);
                        sb.Append('#').Append(field).Append("=<wire ").Append(wire).Append('>');
                        break;
                }
            }
        }
        catch (Exception) { return "<non protobuf>"; }
        return sb.Append('}').ToString();
    }

    // CharacterListResponse : character_information(1 repeated)
    private static void DecodeCharacterListResponse(ReadOnlySpan<byte> body, ConnDecoded d)
    {
        d.Type = ConnDecoded.Kind.CharacterList;
        var byServer = new Dictionary<int, ConnServer>();
        var r = new ProtoWire.Reader(body);
        while (!r.End)
        {
            var (field, wire) = r.Tag();
            if (field == 1 && wire == 2)
            {
                var character = DecodeCharacter(r.Bytes());
                if (character.ServerId <= 0) continue;
                if (!byServer.TryGetValue(character.ServerId, out var server))
                {
                    server = new ConnServer { Id = character.ServerId };
                    byServer.Add(character.ServerId, server);
                    d.Servers.Add(server);
                }
                server.Characters.Add(character);
            }
            else r.Skip(wire);
        }
    }

    // CharacterInformation : name(1), breed(2 enum), gender(3 enum), level(4), last_connection_date(5), server_id(6)
    private static ConnCharacter DecodeCharacter(ReadOnlySpan<byte> body)
    {
        var c = new ConnCharacter();
        var r = new ProtoWire.Reader(body);
        while (!r.End)
        {
            var (field, wire) = r.Tag();
            if (field == 1 && wire == 2) c.Name = r.String();
            else if (field == 2 && wire == 0) c.Breed = (int)r.Varint();
            else if (field == 3 && wire == 0) c.Gender = (int)r.Varint();
            else if (field == 4 && wire == 0) c.Level = (int)r.Varint();
            else if (field == 5 && wire == 2) c.LastConnectionDate = r.String();
            else if (field == 6 && wire == 0) c.ServerId = (int)r.Varint();
            else r.Skip(wire);
        }
        return c;
    }

    /// <summary>
    /// Joint la liste serveur reçue à l'identification et la CharacterListResponse séparée de 3.6.12.16.
    /// Seuls les server_id déjà présents dans les métadonnées serveur autoritaires sont conservés : une entrée
    /// personnage orpheline ne doit jamais fabriquer un serveur sélectionnable.
    /// </summary>
    internal static List<ConnServer> MergeServerCharacters(IReadOnlyList<ConnServer> serverMetadata,
                                                           IReadOnlyList<ConnServer> characterBuckets)
    {
        var result = new List<ConnServer>(serverMetadata.Count);
        var byId = new Dictionary<int, ConnServer>();

        foreach (var source in serverMetadata)
        {
            if (source is null || source.Id <= 0 || byId.ContainsKey(source.Id)) continue;
            var copy = new ConnServer
            {
                Id = source.Id,
                Status = source.Status,
                MonoAccount = source.MonoAccount,
                Accessibility = source.Accessibility,
            };
            byId.Add(copy.Id, copy);
            result.Add(copy);
        }

        foreach (var bucket in characterBuckets)
        {
            if (bucket is null || bucket.Id <= 0 || !byId.TryGetValue(bucket.Id, out var target)) continue;
            foreach (var character in bucket.Characters)
            {
                if (character is null || character.ServerId != bucket.Id) continue;
                target.Characters.Add(character);
            }
        }

        return result;
    }

    // IdentificationResponse.Error : reason(1 enum), ban_end_date(2), required_version(3)
    private static void DecodeIdError(ReadOnlySpan<byte> body, ConnDecoded d)
    {
        var r = new ProtoWire.Reader(body);
        while (!r.End)
        {
            var (field, wire) = r.Tag();
            if (field == 1 && wire == 0) d.ErrorReason = (int)r.Varint();
            else if (field == 2 && wire == 2) d.BanEndDate = r.String();
            else if (field == 3 && wire == 2) d.RequiredVersion = r.String();
            else r.Skip(wire);
        }
    }

    // SelectServerResponse : success(1 message) | error(2 enum, DiscriminatedUnion32)
    private static void DecodeSelectServerResponse(ReadOnlySpan<byte> body, ConnDecoded d)
    {
        var r = new ProtoWire.Reader(body);
        while (!r.End)
        {
            var (field, wire) = r.Tag();
            if (field == 1 && wire == 2) { d.Type = ConnDecoded.Kind.SelectServerSuccess; DecodeSelectSuccess(r.Bytes(), d); }
            else if (field == 2 && wire == 0) { d.Type = ConnDecoded.Kind.SelectServerError; d.SelectServerError = (int)r.Varint(); }
            else r.Skip(wire);
        }
    }

    // SelectServerResponse.Success : token(1), host(2), ports(3 packed varint)
    private static void DecodeSelectSuccess(ReadOnlySpan<byte> body, ConnDecoded d)
    {
        var r = new ProtoWire.Reader(body);
        while (!r.End)
        {
            var (field, wire) = r.Tag();
            if (field == 1 && wire == 2) d.GameToken = r.String();
            else if (field == 2 && wire == 2) d.GameHost = r.String();
            else if (field == 3 && wire == 2)
            {
                var packed = new ProtoWire.Reader(r.Bytes());
                while (!packed.End) d.GamePorts.Add((int)packed.Varint());
            }
            else if (field == 3 && wire == 0) d.GamePorts.Add((int)r.Varint());
            else r.Skip(wire);
        }
    }

    // Libellés lisibles des énumérations d'erreur (pour les journaux).
    public static string IdErrorName(int reason) => reason switch
    {
        0 => "UNKNOWN_AUTH_ERROR", 1 => "ALREADY_CONNECTED", 2 => "OTP_TIMEOUT", 3 => "BANNED",
        4 => "INVALID_SHIELD_CERTIFICATE", 5 => "LOCKED", 6 => "CREDENTIALS_RESET", 7 => "WRONG_CREDENTIALS",
        8 => "EMAIL_UNVALIDATED", 9 => "ANONYMOUS_IP_FORBIDDEN", 10 => "NICKNAME_REGISTRATION",
        11 => "UNAUTHORIZED", 12 => "INVALID_CLIENT_VERSION", 13 => "OUTDATED_CLIENT_VERSION",
        _ => $"reason#{reason}",
    };

    public static string SelectErrorName(int err) => err switch
    {
        0 => "REFUSED", 1 => "SUBSCRIBER_ONLY", 2 => "SINGLE_ACCOUNT_VERIFIED_ONLY", 3 => "MAINTENANCE",
        _ => $"error#{err}",
    };

    /// <summary>
    /// Vrai si le corps (déjà déframé) suit le chemin écrit par <see cref="BuildSelectServer"/> :
    /// LoginMessage.request(1) puis selectServer(4) portant un server(1). Ne lève jamais.
    /// </summary>
    public static bool TryIsSelectServerRequest(ReadOnlySpan<byte> body)
    {
        try
        {
            var login = new ProtoWire.Reader(body);
            while (!login.End)
            {
                var (field, wire) = login.Tag();
                if (field == 1 && wire == 2) return RequestSelectsServer(login.Bytes());
                login.Skip(wire);
            }
        }
        catch { return false; }
        return false;
    }

    private static bool RequestSelectsServer(ReadOnlySpan<byte> request)
    {
        var reader = new ProtoWire.Reader(request);
        while (!reader.End)
        {
            var (field, wire) = reader.Tag();
            if (field == 4 && wire == 2) return SelectServerHasId(reader.Bytes());
            reader.Skip(wire);
        }
        return false;
    }

    private static bool SelectServerHasId(ReadOnlySpan<byte> selectServer)
    {
        var reader = new ProtoWire.Reader(selectServer);
        while (!reader.End)
        {
            var (field, wire) = reader.Tag();
            if (field == 1 && wire == 0)
            {
                reader.Varint();
                return true;
            }
            reader.Skip(wire);
        }
        return false;
    }
}
