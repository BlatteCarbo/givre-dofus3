using System.Collections.Generic;
using System.Text.Json.Serialization;
using GivreClone.Engine.Secrets;

namespace GivreClone.App.Services;

/// <summary>Vitesse de déplacement post-changement de carte. Rapide = comportement par défaut (settle jitté 400-800 ms) ;
/// Legit = settle plus long/configurable pour paraître plus humain.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<MovementSpeed>))]
public enum MovementSpeed { Rapide, Legit }

/// <summary>Mode de réponse automatique aux MP. Off = rien ; Keyword = règles mots-clés ; Llm = LLM ; KeywordThenLlm =
/// on tente les règles, sinon le LLM.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<AutoReplyMode>))]
public enum AutoReplyMode { Off, Keyword, Llm, KeywordThenLlm }

/// <summary>Sémantique de correspondance d'une règle mot-clé sur le contenu du MP.</summary>
[JsonConverter(typeof(JsonStringEnumConverter<KeywordMatch>))]
public enum KeywordMatch { Contains, Equals, StartsWith }

/// <summary>Une règle « si le MP contient/est/commence par &lt;Keyword&gt; -> répondre &lt;Response&gt; ». Insensible à la
/// casse par défaut. Première règle activée qui matche gagne (court-circuit).</summary>
public sealed class KeywordRule
{
    public string Keyword { get; set; } = "";
    public string Response { get; set; } = "";
    public bool Enabled { get; set; } = true;
    public KeywordMatch Match { get; set; } = KeywordMatch.Contains;
}

/// <summary>Réglages PAR PERSONNAGE de l'onglet Paramètres : déplacement (Rapide/Legit) + messagerie privée (réponse
/// auto par mots-clés / LLM + relais Discord). Les secrets sont protégés par DPAPI (CurrentUser).</summary>
public sealed class BotSettings
{
    public bool RegenerationEnabled { get; set; }
    public int RegenerationStartPercent { get; set; } = 60;
    public int RegenerationStopPercent { get; set; } = 100;
    public int RegenerationFoodGid { get; set; }
    public bool RegenerationWaitForTeam { get; set; }

    // ---- Caractéristiques : plan ordonné, activation explicite par personnage ----
    public bool OrderedCharacteristicsEnabled { get; set; }
    public List<GivreClone.Protocol.CharacteristicTarget> OrderedCharacteristicTargets { get; set; } = new();

    // ---- Déplacement ----
    public MovementSpeed Speed { get; set; } = MovementSpeed.Rapide;
    /// <summary>Bornes (ms) du settle post-carte en mode Legit (utilisées seulement si Speed==Legit). Défaut 1500-3000.</summary>
    public int LegitSettleMinMs { get; set; } = 1500;
    public int LegitSettleMaxMs { get; set; } = 3000;
    /// <summary>Trajets : marcher de préférence (zaap / havre-sac seulement pour changer d'île ou au-delà de WalkMaxHops cartes).
    /// Faux = comportement d'origine (zaap / havre-sac dès qu'ils raccourcissent nettement le trajet).</summary>
    public bool PreferWalk { get; set; }
    /// <summary>Marche de préférence : distance max à pied (cartes) avant d'accepter zaap / havre-sac ; 0 = sans limite.</summary>
    public int WalkMaxHops { get; set; }
    /// <summary>Raccourci zaap : auto (zaapi si la carte en a un, puis carte du monde, zaap au sol, havre-sac),
    /// ou un seul de ces choix. Défaut auto.</summary>
    [JsonConverter(typeof(JsonStringEnumConverter<ZaapRoute>))]
    public ZaapRoute ZaapTravel { get; set; } = ZaapRoute.Auto;

    // ---- Watchdog anti-stuck (bot vivant mais qui n'avance plus) ----
    /// <summary>Active la surveillance anti-blocage : détecte un script qui tourne mais n'agit plus (boucle sans progresser,
    /// coincé dans un dialogue, bloqué après une carte) et tente une récupération DOUCE. Combat/récolte/déplacement/mort ne
    /// sont jamais flaggés. Défaut ON (crans doux seulement).</summary>
    public bool AntiStuckEnabled { get; set; } = true;
    /// <summary>2e cran (OPT-IN) : si toujours bloqué après le 1er cran (fermeture de dialogue), REDÉMARRE le script. Défaut
    /// OFF (plus intrusif : coupe/relance le script en cours). JAMAIS de reconnexion de compte quoi qu'il arrive.</summary>
    public bool AntiStuckRestartScript { get; set; }
    /// <summary>Délai (s) sans aucune action forgée, activité « en jeu » (ni combat/récolte/déplacement), avant de juger le bot
    /// figé. Généreux par défaut (180 s) pour ne jamais couper une pause légitime (delay long, attente serveur).</summary>
    public int AntiStuckTimeoutSeconds { get; set; } = 180;
    /// <summary>Dernier cran : reconnexion de secours (script repris), 3 fois par heure et par compte au plus, jamais en combat,
    /// après un message de modérateur ni pour un perso surchargé. Défaut ON.</summary>
    public bool AntiStuckReconnect { get; set; } = true;

    // ---- Succès ----
    /// <summary>Récompense d'un succès terminé récupérée seule quelques secondes après, hors combat, même sans script.
    /// Défaut ON (comportement d'origine). Un script peut encore la couper (achievement:setAutoClaim(false)).</summary>
    public bool AchievementAutoClaim { get; set; } = true;

    // ---- Anti-inactivité ----
    /// <summary>Hors combat, échange, dialogue et déplacement : si aucune action client ou bot n'a été envoyée au serveur
    /// pendant <see cref="AntiAfkIdleMinutes"/> (plus un jitter), envoie un changement d'orientation du personnage.
    /// Défaut ON. Le ping réseau ne compte pas : il ne retarde pas la coupure d'inactivité du serveur.</summary>
    public bool AntiAfkEnabled { get; set; } = true;
    /// <summary>Minutes sans action avant le keepalive d'orientation. Défaut 10. Borné à l'usage (1-30).</summary>
    public int AntiAfkIdleMinutes { get; set; } = 10;

    // ---- Messagerie : réponse automatique ----
    public bool AutoReplyEnabled { get; set; }
    public AutoReplyMode ReplyMode { get; set; } = AutoReplyMode.Keyword;
    public List<KeywordRule> Rules { get; set; } = new();
    /// <summary>Délai (jitter) avant d'envoyer la réponse auto — plus humain qu'une réponse instantanée.</summary>
    public int ReplyDelayMinMs { get; set; } = 1500;
    public int ReplyDelayMaxMs { get; set; } = 4000;
    /// <summary>Cooldown par expéditeur (s) : on ne répond pas plus d'une fois par N s au même joueur (anti-ping-pong).</summary>
    public int ReplyCooldownSec { get; set; } = 8;

    // ---- Messagerie : LLM (mode Llm / KeywordThenLlm) ----
    /// <summary>true = appeler OpenAI (ou compatible) DIRECTEMENT avec la clé de l'utilisateur ; false = passer par le
    /// backend (proxy, clé côté serveur — nécessite l'endpoint backend, cf spec).</summary>
    public bool LlmDirect { get; set; } = true;
    public string LlmModel { get; set; } = "gpt-4o-mini";
    /// <summary>Endpoint compatible OpenAI (vide = https://api.openai.com/v1). Permet DeepSeek/Groq/local...</summary>
    public string LlmBaseUrl { get; set; } = "";
    /// <summary>Clé API chiffrée DPAPI (base64), JAMAIS en clair. Déchiffrée seulement au moment de l'appel.</summary>
    public string LlmApiKeyProtected { get; set; } = "";
    public string LlmSystemPrompt { get; set; } = "Tu es un joueur de Dofus. Réponds brièvement, en français, de façon naturelle et polie.";

    // ---- Messagerie : relais Discord (backend, cf docs/private-message-relay-spec.md) ----
    public bool DiscordRelay { get; set; }
    /// <summary>Secondes laissées à une réponse MANUELLE via Discord avant le repli automatique.</summary>
    public int ManualReplyWindowSec { get; set; } = 60;
    /// <summary>À l'expiration de la fenêtre manuelle sans réponse : répondre automatiquement (true) ou rester muet (false).</summary>
    public bool AutoReplyIfNoManual { get; set; } = true;

    // ---- Notifications Discord (webhook du perso) ----
    private string? _discordWebhookUrl;
    private string? _legacyDiscordWebhookUrl;
    private bool _webhookEdited;

    /// <summary>Seule forme persistée du webhook : blob DPAPI CurrentUser, jamais l'URL.</summary>
    public string DiscordWebhookProtected { get; set; } = "";

    /// <summary>URL disponible en mémoire pour l'interface existante. Exclue de la sérialisation.</summary>
    [JsonIgnore]
    public string DiscordWebhookUrl
    {
        get
        {
            if (_discordWebhookUrl is not null) return _discordWebhookUrl;
            return _discordWebhookUrl = !string.IsNullOrEmpty(DiscordWebhookProtected)
                ? Dpapi.Unprotect(DiscordWebhookProtected) ?? ""
                : _legacyDiscordWebhookUrl ?? "";
        }
        set { _discordWebhookUrl = (value ?? "").Trim(); _webhookEdited = true; }
    }

    // Lecture rétrocompatible des anciens bot-settings.json. Le getter nul empêche toute réécriture en clair.
    [JsonPropertyName("DiscordWebhookUrl")]
    [JsonIgnore(Condition = JsonIgnoreCondition.WhenWritingNull)]
    public string? LegacyDiscordWebhookUrl
    {
        get => null;
        set => _legacyDiscordWebhookUrl = value;
    }

    [JsonIgnore]
    public bool NeedsWebhookMigration => _legacyDiscordWebhookUrl is not null;

    /// <summary>Prépare une écriture sans jamais autoriser de repli en clair si DPAPI échoue.</summary>
    public void ProtectWebhookForSave()
    {
        if (!_webhookEdited && (_legacyDiscordWebhookUrl is null || !string.IsNullOrEmpty(DiscordWebhookProtected)))
        {
            _legacyDiscordWebhookUrl = null;
            return;
        }

        string plain = DiscordWebhookUrl;
        string encrypted = plain.Length == 0 ? "" : Dpapi.Protect(plain);
        DiscordWebhookProtected = encrypted;
        _legacyDiscordWebhookUrl = null;
        _webhookEdited = false;
    }
    public bool NotifyAnomalyOpened { get; set; }
    public bool NotifyAnomalyClosed { get; set; } = true;
    /// <summary>Alerte Discord : un objet du sac se vend nettement au-dessus de la moyenne du marché.</summary>
    public bool NotifyResale { get; set; }
    /// <summary>Forgemagie : exo / over réussi et objet terminé (bilan) envoyés sur le webhook du perso. Défaut ON.</summary>
    public bool NotifyForge { get; set; } = true;
    /// <summary>Derniers kamas VUS dans la banque du perso (coffre ouvert) et l'instant (UTC) : affichés tant que la banque n'est pas
    /// rouverte dans la session. -1 = jamais vus.</summary>
    public long LastBankKamas { get; set; } = -1;
    public DateTime? LastBankKamasUtc { get; set; }
    public int ResaleMinGainPercent { get; set; } = 30;
    public long ResaleMinTotal { get; set; } = 100_000;
}

/// <summary>Racine persistée : réglages indexés par nom de personnage. Enregistrée dans %APPDATA%/GivreClone/bot-settings.json.</summary>
public sealed class BotSettingsFile
{
    public Dictionary<string, BotSettings> Settings { get; set; } = new(System.StringComparer.Ordinal);
}
