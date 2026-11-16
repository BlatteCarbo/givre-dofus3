namespace GivreClone.App.Services;

/// <summary>Recopie TOUS les réglages d'un personnage vers un autre (fenêtre « Copier les réglages » de MizanBot) : déplacement,
/// anti-blocage, anti-inactivité, succès, messagerie (via <see cref="MessagingSettingsCopy"/>) et alertes. Le webhook Discord reste propre à chaque
/// personnage (secret DPAPI, comme pour la copie de messagerie).</summary>
public static class BotSettingsCopy
{
    public static void ApplyAll(BotSettings src, BotSettings dst)
    {
        dst.RegenerationEnabled = src.RegenerationEnabled;
        dst.RegenerationStartPercent = src.RegenerationStartPercent;
        dst.RegenerationStopPercent = src.RegenerationStopPercent;
        dst.RegenerationFoodGid = src.RegenerationFoodGid;
        dst.RegenerationWaitForTeam = src.RegenerationWaitForTeam;
        dst.Speed = src.Speed; dst.LegitSettleMinMs = src.LegitSettleMinMs; dst.LegitSettleMaxMs = src.LegitSettleMaxMs;
        dst.PreferWalk = src.PreferWalk; dst.WalkMaxHops = src.WalkMaxHops; dst.ZaapTravel = src.ZaapTravel;
        dst.AntiStuckEnabled = src.AntiStuckEnabled; dst.AntiStuckRestartScript = src.AntiStuckRestartScript;
        dst.AntiStuckTimeoutSeconds = src.AntiStuckTimeoutSeconds; dst.AntiStuckReconnect = src.AntiStuckReconnect;
        dst.AchievementAutoClaim = src.AchievementAutoClaim;
        dst.AntiAfkEnabled = src.AntiAfkEnabled; dst.AntiAfkIdleMinutes = src.AntiAfkIdleMinutes;
        MessagingSettingsCopy.Apply(src, dst);
        dst.NotifyAnomalyOpened = src.NotifyAnomalyOpened; dst.NotifyAnomalyClosed = src.NotifyAnomalyClosed;
        dst.NotifyResale = src.NotifyResale; dst.NotifyForge = src.NotifyForge; dst.ResaleMinGainPercent = src.ResaleMinGainPercent; dst.ResaleMinTotal = src.ResaleMinTotal;
    }
}
