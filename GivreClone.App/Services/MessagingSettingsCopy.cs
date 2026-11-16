using System.Text.Json;

namespace GivreClone.App.Services;

/// <summary>Recopie les réglages de messagerie privée d'un personnage vers un autre : réponse automatique, mots-clés, IA et sa clé (blob DPAPI du même
/// utilisateur Windows), délais, cooldown et relais Discord. Ne touche ni au webhook de notifications ni aux autres réglages du personnage cible.</summary>
public static class MessagingSettingsCopy
{
    public static void Apply(BotSettings src, BotSettings dst)
    {
        dst.AutoReplyEnabled = src.AutoReplyEnabled;
        dst.ReplyMode = src.ReplyMode;
        dst.Rules = JsonSerializer.Deserialize<System.Collections.Generic.List<KeywordRule>>(JsonSerializer.Serialize(src.Rules)) ?? new();
        dst.ReplyDelayMinMs = src.ReplyDelayMinMs; dst.ReplyDelayMaxMs = src.ReplyDelayMaxMs; dst.ReplyCooldownSec = src.ReplyCooldownSec;
        dst.LlmDirect = src.LlmDirect; dst.LlmModel = src.LlmModel; dst.LlmBaseUrl = src.LlmBaseUrl;
        dst.LlmApiKeyProtected = src.LlmApiKeyProtected; dst.LlmSystemPrompt = src.LlmSystemPrompt;
        dst.DiscordRelay = src.DiscordRelay; dst.ManualReplyWindowSec = src.ManualReplyWindowSec; dst.AutoReplyIfNoManual = src.AutoReplyIfNoManual;
    }
}
