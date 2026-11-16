using System;
using System.Collections.ObjectModel;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;
using GivreClone.Engine.Secrets;

namespace GivreClone.App.ViewModels;

/// <summary>Une ligne d'édition de règle mot-clé (déclencheur -> réponse). Écrit dans son <see cref="KeywordRule"/> et
/// notifie le parent (persistance) à chaque changement.</summary>
public partial class KeywordRuleRow : ObservableObject
{
    public KeywordRule Model { get; }
    private readonly Action _onChange;
    private bool _loading;

    [ObservableProperty] public partial bool Enabled { get; set; }
    [ObservableProperty] public partial string Keyword { get; set; } = "";
    [ObservableProperty] public partial string Response { get; set; } = "";
    /// <summary>0=Contient, 1=Égal, 2=Commence par (index du ComboBox).</summary>
    [ObservableProperty] public partial int MatchIndex { get; set; }

    public KeywordRuleRow(KeywordRule model, Action onChange)
    {
        Model = model; _onChange = onChange;
        _loading = true;
        Enabled = model.Enabled; Keyword = model.Keyword; Response = model.Response;
        MatchIndex = (int)model.Match;
        _loading = false;
    }

    partial void OnEnabledChanged(bool v) => Apply();
    partial void OnKeywordChanged(string v) => Apply();
    partial void OnResponseChanged(string v) => Apply();
    partial void OnMatchIndexChanged(int v) => Apply();

    private void Apply()
    {
        if (_loading) return;
        Model.Enabled = Enabled; Model.Keyword = Keyword ?? ""; Model.Response = Response ?? "";
        Model.Match = (KeywordMatch)Math.Clamp(MatchIndex, 0, 2);
        _onChange();
    }
}

/// <summary>VM de l'onglet Paramètres (par perso sélectionné) : déplacement (Rapide/Legit) + messagerie privée (réponse
/// auto par mots-clés/LLM + relais Discord). Persistance immédiate dans <see cref="BotSettingsStore"/> à chaque édition.</summary>
public sealed partial class CopyTargetRow : ObservableObject
{
    public string Name { get; init; } = "";
    [ObservableProperty] public partial bool IsChecked { get; set; }
}

public partial class BotSettingsViewModel : ObservableObject
{
    // ---- Copier la messagerie vers d'autres personnages ----
    public ObservableCollection<CopyTargetRow> CopyTargets { get; } = new();
    [ObservableProperty] public partial string CopyStatus { get; set; } = "";
    private string? _currentName;

    [RelayCommand]
    private void RefreshCopyTargets()
    {
        CopyTargets.Clear(); CopyStatus = "";
        var names = MainViewModel.Instance?.BotsVM.Bots.Select(b => b.Row.CharacterName).Where(n => !string.IsNullOrWhiteSpace(n))
            .Distinct(StringComparer.OrdinalIgnoreCase).Where(n => !string.Equals(n, _currentName, StringComparison.OrdinalIgnoreCase)) ?? Enumerable.Empty<string?>();
        foreach (var n in names.OrderBy(n => n, StringComparer.OrdinalIgnoreCase)) CopyTargets.Add(new CopyTargetRow { Name = n! });
        if (CopyTargets.Count == 0) CopyStatus = "Aucun autre personnage connecté.";
        OnPropertyChanged(nameof(HasCopyTargets));
    }

    public bool HasCopyTargets => CopyTargets.Count > 0;

    [RelayCommand]
    private void CopyMessagingToSelected()
    {
        var chosen = CopyTargets.Where(t => t.IsChecked).ToList();
        if (chosen.Count == 0) { CopyStatus = "Coche au moins un personnage."; return; }
        foreach (var t in chosen) MessagingSettingsCopy.Apply(_cfg, BotSettingsStore.Get(t.Name));
        BotSettingsStore.Save();
        CopyStatus = $"Réglages de messagerie copiés vers {chosen.Count} personnage(s) (réponse auto, mots-clés, IA, délais, relais Discord).";
    }

    [RelayCommand]
    private void CopyAllToSelected()
    {
        var chosen = CopyTargets.Where(t => t.IsChecked).ToList();
        if (chosen.Count == 0) { CopyStatus = "Coche au moins un personnage."; return; }
        foreach (var t in chosen) BotSettingsCopy.ApplyAll(_cfg, BotSettingsStore.Get(t.Name));
        BotSettingsStore.Save();
        CopyStatus = $"Tous les réglages copiés vers {chosen.Count} personnage(s) (déplacement, anti-blocage, anti-inactivité, succès, messagerie, alertes ; webhook non copié).";
    }

    [RelayCommand]
    private void CheckAllCopyTargets()
    {
        bool all = CopyTargets.All(t => t.IsChecked);
        foreach (var t in CopyTargets) t.IsChecked = !all;
    }

    private BotSettings _cfg = new();
    private bool _loading;

    // ---- Déplacement ----
    /// <summary>0=Rapide (défaut), 1=Legit.</summary>
    [ObservableProperty] public partial int SpeedIndex { get; set; }
    [ObservableProperty] public partial int LegitSettleMinMs { get; set; }
    [ObservableProperty] public partial int LegitSettleMaxMs { get; set; }
    public bool IsLegit => SpeedIndex == 1;

    // ---- Watchdog anti-stuck ----
    [ObservableProperty] public partial bool AntiStuckEnabled { get; set; } = true;
    [ObservableProperty] public partial bool AntiStuckRestartScript { get; set; }
    [ObservableProperty] public partial bool AntiStuckReconnect { get; set; } = true;
    [ObservableProperty] public partial int AntiStuckTimeoutSeconds { get; set; } = 180;
    [ObservableProperty] public partial bool AchievementAutoClaim { get; set; } = true;
    [ObservableProperty] public partial bool AntiAfkEnabled { get; set; } = true;
    [ObservableProperty] public partial int AntiAfkIdleMinutes { get; set; } = 10;
    /// <summary>Trajets : 0 = zaap / havre-sac dès que plus court, 1 = marche de préférence.</summary>
    [ObservableProperty] public partial int TravelModeIndex { get; set; }
    [ObservableProperty] public partial int WalkMaxHops { get; set; }
    /// <summary>0 = automatique, 1 = zaap depuis la carte, 2 = zaap au sol, 3 = havre-sac, 4 = zaapi.</summary>
    [ObservableProperty] public partial int ZaapTravelIndex { get; set; }
    public bool PrefersWalk => TravelModeIndex == 1;

    // ---- Messagerie ----
    [ObservableProperty] public partial bool AutoReplyEnabled { get; set; }
    /// <summary>0=Off, 1=Mots-clés, 2=LLM, 3=Mots-clés puis LLM.</summary>
    [ObservableProperty] public partial int ReplyModeIndex { get; set; }
    [ObservableProperty] public partial int ReplyDelayMinMs { get; set; }
    [ObservableProperty] public partial int ReplyDelayMaxMs { get; set; }
    [ObservableProperty] public partial int ReplyCooldownSec { get; set; }
    public bool UsesLlm => ReplyModeIndex is 2 or 3;
    public bool UsesKeywords => ReplyModeIndex is 1 or 3;

    public ObservableCollection<KeywordRuleRow> Rules { get; } = new();
    public bool HasRules => Rules.Count > 0;

    // ---- LLM ----
    [ObservableProperty] public partial bool LlmDirect { get; set; } = true;
    [ObservableProperty] public partial string LlmModel { get; set; } = "";
    [ObservableProperty] public partial string LlmBaseUrl { get; set; } = "";
    [ObservableProperty] public partial string LlmSystemPrompt { get; set; } = "";
    /// <summary>Champ de SAISIE de la clé API : en écrivant dedans on chiffre+enregistre puis on vide (jamais réaffiché).</summary>
    [ObservableProperty] public partial string LlmApiKeyInput { get; set; } = "";
    /// <summary>true = une clé chiffrée est déjà enregistrée (affiche "clé enregistrée" sans jamais la montrer).</summary>
    public bool HasLlmKey => !string.IsNullOrEmpty(_cfg.LlmApiKeyProtected);

    // ---- Relais Discord ----
    [ObservableProperty] public partial bool DiscordRelay { get; set; }
    [ObservableProperty] public partial int ManualReplyWindowSec { get; set; }
    [ObservableProperty] public partial bool AutoReplyIfNoManual { get; set; }

    // ---- Notifications Discord (webhook) ----
    [ObservableProperty] public partial string DiscordWebhookUrl { get; set; } = "";
    [ObservableProperty] public partial bool NotifyAnomalyOpened { get; set; }
    [ObservableProperty] public partial bool NotifyAnomalyClosed { get; set; } = true;
    [ObservableProperty] public partial bool NotifyResale { get; set; }
    [ObservableProperty] public partial bool NotifyForge { get; set; } = true;

    public void Load(string? charName)
    {
        _loading = true;
        _cfg = BotSettingsStore.Get(charName);
        _currentName = charName;
        LoadCharacteristicTargets();
        LoadRegeneration();
        SpeedIndex = (int)_cfg.Speed;
        LegitSettleMinMs = _cfg.LegitSettleMinMs; LegitSettleMaxMs = _cfg.LegitSettleMaxMs;
        AntiStuckEnabled = _cfg.AntiStuckEnabled; AntiStuckRestartScript = _cfg.AntiStuckRestartScript; AntiStuckReconnect = _cfg.AntiStuckReconnect; AntiStuckTimeoutSeconds = _cfg.AntiStuckTimeoutSeconds;
        AchievementAutoClaim = _cfg.AchievementAutoClaim;
        AntiAfkEnabled = _cfg.AntiAfkEnabled; AntiAfkIdleMinutes = AntiAfkPolicy.ClampMinutes(_cfg.AntiAfkIdleMinutes);
        TravelModeIndex = _cfg.PreferWalk ? 1 : 0; WalkMaxHops = _cfg.WalkMaxHops;
        ZaapTravelIndex = (int)_cfg.ZaapTravel;
        AutoReplyEnabled = _cfg.AutoReplyEnabled;
        ReplyModeIndex = (int)_cfg.ReplyMode;
        ReplyDelayMinMs = _cfg.ReplyDelayMinMs; ReplyDelayMaxMs = _cfg.ReplyDelayMaxMs;
        ReplyCooldownSec = _cfg.ReplyCooldownSec;
        LlmDirect = _cfg.LlmDirect; LlmModel = _cfg.LlmModel; LlmBaseUrl = _cfg.LlmBaseUrl; LlmSystemPrompt = _cfg.LlmSystemPrompt;
        LlmApiKeyInput = "";
        DiscordRelay = _cfg.DiscordRelay; ManualReplyWindowSec = _cfg.ManualReplyWindowSec; AutoReplyIfNoManual = _cfg.AutoReplyIfNoManual;
        DiscordWebhookUrl = _cfg.DiscordWebhookUrl ?? ""; NotifyAnomalyOpened = _cfg.NotifyAnomalyOpened; NotifyAnomalyClosed = _cfg.NotifyAnomalyClosed; NotifyResale = _cfg.NotifyResale; NotifyForge = _cfg.NotifyForge;
        Rules.Clear();
        foreach (var r in _cfg.Rules) Rules.Add(new KeywordRuleRow(r, Save));
        OnPropertyChanged(nameof(HasRules));
        RaiseDerived();
        _loading = false;
    }

    private void RaiseDerived()
    {
        OnPropertyChanged(nameof(IsLegit));
        OnPropertyChanged(nameof(UsesLlm));
        OnPropertyChanged(nameof(UsesKeywords));
        OnPropertyChanged(nameof(HasLlmKey));
    }

    partial void OnSpeedIndexChanged(int v) { if (_loading) return; _cfg.Speed = (MovementSpeed)Math.Clamp(v, 0, 1); RaiseDerived(); Save(); }
    partial void OnLegitSettleMinMsChanged(int v) { if (_loading) return; _cfg.LegitSettleMinMs = Math.Max(100, v); Save(); }
    partial void OnLegitSettleMaxMsChanged(int v) { if (_loading) return; _cfg.LegitSettleMaxMs = Math.Max(100, v); Save(); }
    partial void OnAntiStuckEnabledChanged(bool v) { if (_loading) return; _cfg.AntiStuckEnabled = v; Save(); }
    partial void OnAntiStuckRestartScriptChanged(bool v) { if (_loading) return; _cfg.AntiStuckRestartScript = v; Save(); }
    partial void OnAntiStuckReconnectChanged(bool value) { if (_loading) return; _cfg.AntiStuckReconnect = value; Save(); }
    partial void OnAntiStuckTimeoutSecondsChanged(int v) { if (_loading) return; _cfg.AntiStuckTimeoutSeconds = Math.Max(30, v); Save(); }
    partial void OnAchievementAutoClaimChanged(bool v) { if (_loading) return; _cfg.AchievementAutoClaim = v; Save(); }
    partial void OnAntiAfkEnabledChanged(bool v) { if (_loading) return; _cfg.AntiAfkEnabled = v; Save(); }
    partial void OnAntiAfkIdleMinutesChanged(int v) { if (_loading) return; _cfg.AntiAfkIdleMinutes = AntiAfkPolicy.ClampMinutes(v); Save(); }
    partial void OnTravelModeIndexChanged(int v) { OnPropertyChanged(nameof(PrefersWalk)); if (_loading) return; _cfg.PreferWalk = v == 1; Save(); }
    partial void OnWalkMaxHopsChanged(int v) { if (_loading) return; _cfg.WalkMaxHops = Math.Clamp(v, 0, 500); Save(); }
    partial void OnZaapTravelIndexChanged(int v)
    {
        if (_loading) return;
        _cfg.ZaapTravel = (ZaapRoute)Math.Clamp(v, 0, (int)ZaapRoute.Zaapi);
        Save();
    }
    partial void OnDiscordWebhookUrlChanged(string v) { if (_loading) return; _cfg.DiscordWebhookUrl = (v ?? "").Trim(); Save(); }
    partial void OnNotifyAnomalyOpenedChanged(bool v) { if (_loading) return; _cfg.NotifyAnomalyOpened = v; Save(); }
    partial void OnNotifyAnomalyClosedChanged(bool v) { if (_loading) return; _cfg.NotifyAnomalyClosed = v; Save(); }
    partial void OnNotifyResaleChanged(bool value) { if (_loading) return; _cfg.NotifyResale = value; Save(); }
    partial void OnNotifyForgeChanged(bool value) { if (_loading) return; _cfg.NotifyForge = value; Save(); }

    /// <summary>Envoie un message d'essai sur le webhook saisi.</summary>
    [RelayCommand]
    private async Task TestWebhook()
    {
        if (!LicenseService.HasPermission(LicenseService.Perm.PmRelay)) { Toasts.Show(Loc.T("L.Bots.Settings.WebhookNoLicense"), success: false); return; }
        if (!(DiscordWebhookUrl ?? "").Trim().StartsWith("https://", StringComparison.OrdinalIgnoreCase)) { Toasts.Show(Loc.T("L.Bots.Settings.WebhookBadUrl"), success: false); return; }
        bool delivered = await WebhookService.PostAsync(DiscordWebhookUrl.Trim(), Loc.T("L.Bots.Settings.WebhookTestMessage"));
        Toasts.Show(delivered ? Loc.T("L.Bots.Settings.WebhookSent")
                              : "Le webhook Discord n'a pas accepté le message. Vérifiez l'adresse et la connexion.",
                    success: delivered);
    }
    partial void OnAutoReplyEnabledChanged(bool v) { if (_loading) return; _cfg.AutoReplyEnabled = v; Save(); }
    partial void OnReplyModeIndexChanged(int v) { if (_loading) return; _cfg.ReplyMode = (AutoReplyMode)Math.Clamp(v, 0, 3); RaiseDerived(); Save(); }
    partial void OnReplyDelayMinMsChanged(int v) { if (_loading) return; _cfg.ReplyDelayMinMs = Math.Max(0, v); Save(); }
    partial void OnReplyDelayMaxMsChanged(int v) { if (_loading) return; _cfg.ReplyDelayMaxMs = Math.Max(0, v); Save(); }
    partial void OnReplyCooldownSecChanged(int v) { if (_loading) return; _cfg.ReplyCooldownSec = Math.Max(0, v); Save(); }
    partial void OnLlmDirectChanged(bool v) { if (_loading) return; _cfg.LlmDirect = v; Save(); }
    partial void OnLlmModelChanged(string v) { if (_loading) return; _cfg.LlmModel = v ?? ""; Save(); }
    partial void OnLlmBaseUrlChanged(string v) { if (_loading) return; _cfg.LlmBaseUrl = v ?? ""; Save(); }
    partial void OnLlmSystemPromptChanged(string v) { if (_loading) return; _cfg.LlmSystemPrompt = v ?? ""; Save(); }
    partial void OnDiscordRelayChanged(bool v) { if (_loading) return; _cfg.DiscordRelay = v; Save(); }
    partial void OnManualReplyWindowSecChanged(int v) { if (_loading) return; _cfg.ManualReplyWindowSec = Math.Max(1, v); Save(); }
    partial void OnAutoReplyIfNoManualChanged(bool v) { if (_loading) return; _cfg.AutoReplyIfNoManual = v; Save(); }

    // Clé API : chiffrée DPAPI dès la saisie, puis le champ est vidé (on ne réaffiche jamais la clé).
    partial void OnLlmApiKeyInputChanged(string v)
    {
        if (_loading) return;
        if (string.IsNullOrWhiteSpace(v)) return;
        _cfg.LlmApiKeyProtected = Dpapi.Protect(v.Trim());
        BotSettingsStore.Save();
        _loading = true; LlmApiKeyInput = ""; _loading = false;   // vider sans re-déclencher
        OnPropertyChanged(nameof(HasLlmKey));
    }

    [RelayCommand]
    private void ClearLlmKey()
    {
        _cfg.LlmApiKeyProtected = "";
        BotSettingsStore.Save();
        OnPropertyChanged(nameof(HasLlmKey));
    }

    [RelayCommand]
    private void AddRule()
    {
        var r = new KeywordRule { Keyword = "", Response = "", Enabled = true };
        _cfg.Rules.Add(r);
        Rules.Add(new KeywordRuleRow(r, Save));
        OnPropertyChanged(nameof(HasRules));
        Save();
    }

    [RelayCommand]
    private void RemoveRule(KeywordRuleRow? row)
    {
        if (row is null) return;
        Rules.Remove(row);
        _cfg.Rules.Remove(row.Model);
        OnPropertyChanged(nameof(HasRules));
        Save();
    }

    private void Save()
    {
        if (_loading) return;
        BotSettingsStore.Save();
    }
}
