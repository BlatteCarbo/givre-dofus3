using System.Text.Json;

namespace GivreClone.Protocol;

/// <summary>
/// Le "dictionnaire" du protocole : resout NOM CLAIR &lt;-&gt; CODE obfusque, et fournit les tables de champs
/// (numero -&gt; nom/type) lues du .proto clair. Charge depuis des CACHES regenerables (jamais du code en dur) :
///   - matching_current.json : { "type.ankama.com/&lt;code&gt;" : "MessageType.&lt;Nom&gt;" }  (tier fiable "struct")
///   - confirmed.json        : { "&lt;code&gt;" : "&lt;Nom&gt;" }  (overrides verifies utilisateur, priorite max)
///   - fields.json           : { "&lt;Nom&gt;" : { "&lt;champ&gt;" : &lt;numeroWire&gt; } }  (mapping de CHAMP confirme)
///   - clear_game_2025.proto : structures + numeros de champ (schema beta, tags parfois derives)
/// Les codes ET les tags derivent a chaque patch Dofus : ces fichiers sont a re-generer par le pipeline.
///
/// POURQUOI fields.json : Ankama SHUFFLE les numeros de champ a chaque patch, donc le numero du .proto clair
/// n'est PAS le numero sur le fil (ex : MapComplementaryInformationEvent.map_id = #5 au schema beta mais #2 sur
/// le client actuel). On ne lit donc PAS un champ "par le schema" : fields.json porte le VRAI numero wire confirme
/// (comme le workflow "Confirmer un champ" de GivreProxy) et la lecture reste data-driven, jamais codee en dur.
/// Une resolution absente n'est PAS une erreur fatale : l'action/lecture concernee se desactive proprement.
/// </summary>
public sealed class ProtocolCatalog
{
    private const string TypeUrlPrefix = "type.ankama.com/";
    private const string MessageTypePrefix = "MessageType.";

    private readonly Dictionary<string, string> _codeToName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, string> _nameToCode = new(StringComparer.Ordinal);
    private readonly IReadOnlyDictionary<string, ProtoMessageDef> _messages;

    // Mapping de champ CONFIRME (fields.json), par NOM CLAIR de message : champ -> numero wire, et l'inverse.
    // C'est la SEULE source fiable de numeros de champ (le .proto beta a des tags derives). Vide par defaut.
    private readonly Dictionary<string, Dictionary<string, int>> _fieldNumberByName = new(StringComparer.Ordinal);
    private readonly Dictionary<string, Dictionary<int, string>> _fieldNameByNumber = new(StringComparer.Ordinal);


    public int MappingCount => _codeToName.Count;
    public int MessageDefCount => _messages.Count;
    /// <summary>Nombre de messages dont fields.json fournit les numéros de champ (source fiable, contrairement au .proto).</summary>
    public int FieldMessageCount => _fieldNameByNumber.Count;

    private ProtocolCatalog(IReadOnlyDictionary<string, ProtoMessageDef> messages) => _messages = messages;

    /// <summary>Charge le catalogue depuis un dossier data/ (matching_current.json, confirmed.json, clear proto).</summary>
    public static ProtocolCatalog LoadFromDirectory(string dataDir, string clearProtoFile = "clear_game_2025.proto")
    {
        var protoPath = Path.Combine(dataDir, clearProtoFile);
        var matchingPath = Path.Combine(dataDir, "matching_current.json");
        var confirmedPath = Path.Combine(dataDir, "confirmed.json");
        var fieldsPath = Path.Combine(dataDir, "fields.json");
        return LoadFromStrings(
            File.Exists(protoPath) ? File.ReadAllText(protoPath) : null,
            File.Exists(matchingPath) ? File.ReadAllText(matchingPath) : null,
            File.Exists(confirmedPath) ? File.ReadAllText(confirmedPath) : null,
            File.Exists(fieldsPath) ? File.ReadAllText(fieldsPath) : null);
    }

    /// <summary>
    /// Charge le catalogue depuis les CONTENUS bruts (proto clair + matching/confirmed/fields JSON), SANS toucher
    /// au disque. Utilisé par le mapping STREAMÉ du serveur : les artefacts déchiffrés en mémoire ne sont jamais
    /// écrits sur disque (le mapping n'est jamais visible pour l'utilisateur). Chaque contenu est optionnel.
    /// </summary>
    public static ProtocolCatalog LoadFromStrings(string? protoText, string? matchingJson, string? confirmedJson, string? fieldsJson)
    {
        var messages = !string.IsNullOrEmpty(protoText)
            ? ProtoSchemaText.Parse(protoText)
            : new Dictionary<string, ProtoMessageDef>();

        var catalog = new ProtocolCatalog(messages);
        if (!string.IsNullOrEmpty(matchingJson)) catalog.LoadMatching(matchingJson);
        if (!string.IsNullOrEmpty(confirmedJson)) catalog.LoadConfirmed(confirmedJson);
        if (!string.IsNullOrEmpty(fieldsJson)) catalog.LoadFields(fieldsJson);
        // VÉRITÉ = matching_current.json + fields.json (04/10). Les sections "_fields" (pins de numéros) et "_aliases" (anciens
        // noms) de confirmed.json ne sont PLUS lues : la prod ne les appliquait pas, elles faisaient marcher en test ce qui était
        // mort en prod (ventes HDV hors ligne, décodage des actions de combat).
        return catalog;
    }

    // { "type.ankama.com/<code>": "MessageType.<Name>" }
    private void LoadMatching(string json)
    {
        using var doc = JsonDocument.Parse(json);
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            var key = prop.Name;
            var val = prop.Value.GetString();
            if (val is null) continue;
            var code = key.StartsWith(TypeUrlPrefix, StringComparison.Ordinal) ? key[TypeUrlPrefix.Length..] : key;
            var name = val.StartsWith(MessageTypePrefix, StringComparison.Ordinal) ? val[MessageTypePrefix.Length..] : val;
            Bind(code, name, overwrite: false);
        }
    }

    // { "<code>": "<Name>" } : COMBLE seulement les codes absents de matching_current.json (qui fait foi, réception comprise).
    // Jusqu'au 04/10 il écrasait 9 noms v5 (pont « GameFightShowFighter », « MapInformationRequest » sur kkg…) : selon le
    // confirmed.json servi, un handler écrit avec le nom v5 ne se déclenchait pas. Valeurs non-string ignorées.
    private void LoadConfirmed(string json)
    {
        using var doc = JsonDocument.Parse(json);
        foreach (var prop in doc.RootElement.EnumerateObject())
        {
            if (prop.Value.ValueKind != JsonValueKind.String) continue;   // "_fields" (objet) et toute valeur non-string
            var name = prop.Value.GetString();
            if (name is null) continue;
            Bind(prop.Name, name, overwrite: false);
        }
    }

    private void Bind(string code, string name, bool overwrite)
    {
        // code -> nom (RÉCEPTION / p.Name des handlers) : matching_current.json, chargé en premier, fait foi ; confirmed.json
        // (overwrite:false) ne fait que combler un code inconnu.
        if (overwrite || !_codeToName.ContainsKey(code)) _codeToName[code] = name;
        // nom -> code (ÉMISSION / CodeForName utilisé par les forges) : matching_current.json (v4) fait AUTORITÉ.
        // Chargé EN PREMIER (overwrite:false), il gagne ; confirmed.json ne peut donc PLUS réassigner un nom déjà lié
        // par v4 à un code DIFFÉRENT — il ne fait qu'AJOUTER les noms absents de v4. C'était EXACTEMENT la classe du
        // bug dépôt banque (confirmed inversait "...TransferAll" -> kdu au lieu du jzb de v4). "Son mapping = le nôtre"
        // appliqué à la forge : un confirmed.json périmé ne peut plus empoisonner un code émis en silence.
        if (!_nameToCode.ContainsKey(name)) _nameToCode[name] = code;
    }

    // { "<MessageName>": { "<fieldName>": <wireNumber> } }
    private void LoadFields(string json)
    {
        using var doc = JsonDocument.Parse(json);
        foreach (var msg in doc.RootElement.EnumerateObject())
        {
            var byName = new Dictionary<string, int>(StringComparer.Ordinal);
            var byNumber = new Dictionary<int, string>();
            foreach (var field in msg.Value.EnumerateObject())
            {
                if (!field.Value.TryGetInt32(out var num)) continue;
                byName[field.Name] = num;
                byNumber[num] = field.Name;
            }
            _fieldNumberByName[msg.Name] = byName;
            _fieldNameByNumber[msg.Name] = byNumber;
        }
    }

    /// <summary>Catalogue de REPLI (DEV uniquement) consulté quand CE catalogue n'a pas l'entrée. Sert à combler un mapping
    /// streamé INCOMPLET/périmé (ex : sous-types imbriqués absents) avec le mapping local complet, SANS rien coder en dur —
    /// on reste 100% piloté par des fichiers de mapping. Câblé par <c>MappingCatalog</c> en Debug seulement ; null en prod
    /// (le streamé DOIT y être complet). Priorité : ce catalogue d'abord, le repli seulement pour ce qu'il ignore.</summary>
    public ProtocolCatalog? Fallback { get; set; }

    /// <summary>Nom clair d'un code obfusque, ou null si non mappe.</summary>
    public string? NameForCode(string code) => _codeToName.TryGetValue(code, out var n) ? n : Fallback?.NameForCode(code);

    /// <summary>Code obfusque courant d'un nom clair, ou null si non mappe (mapping a regenerer).</summary>
    public string? CodeForName(string name) => _nameToCode.TryGetValue(name, out var c) ? c : Fallback?.CodeForName(name);

    /// <summary>Definition de message (champs) par nom clair, ou null si absente du .proto.</summary>
    public ProtoMessageDef? Message(string name) => _messages.TryGetValue(name, out var m) ? m : Fallback?.Message(name);

    /// <summary>
    /// Numero de champ WIRE confirme (fields.json) pour (message clair, champ clair), ou null si non confirme.
    /// A utiliser pour LIRE un champ de facon data-driven au lieu d'un numero code en dur. Ne retombe PAS sur le
    /// .proto beta (numeros derives -> faux) : tant que le champ n'est pas dans fields.json, la lecture se desactive.
    /// </summary>
    public int? FieldNumber(string messageName, string fieldName)
    {
        if (_fieldNumberByName.TryGetValue(messageName, out var m) && m.TryGetValue(fieldName, out var n))
            return n;
        return Fallback?.FieldNumber(messageName, fieldName);
    }

    /// <summary>Nom de champ clair confirme (fields.json) pour (message clair, numero wire), ou null. Pour l'inspecteur.</summary>
    public string? FieldName(string messageName, int number)
    {
        if (_fieldNameByNumber.TryGetValue(messageName, out var m) && m.TryGetValue(number, out var n))
            return n;
        return Fallback?.FieldName(messageName, number);
    }

    /// <summary>Vrai si le mapping de champs (fields.json) connaît ce message. Sert au décodage générique
    /// (PacketTree) pour choisir le type d'un sous-message quand le .proto clair est ambigu (noms imbriqués homonymes).</summary>
    public bool HasFields(string messageName)
        => _fieldNameByNumber.ContainsKey(messageName)
           || (Fallback?.HasFields(messageName) ?? false);

    /// <summary>Vrai si le nom clair est a la fois mappe (code connu) et structure (champs connus) : forgeable.</summary>
    public bool CanForge(string name) => CodeForName(name) is not null && Message(name) is not null;
}
