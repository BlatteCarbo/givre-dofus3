using System.Collections.Generic;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;

namespace GivreClone.App.ViewModels;

// Cap de niveau de la licence (offre gratuite : bot interdit au-delà du niveau 30, sur CHAQUE personnage).
//  - Démarrer une automatisation (script, console, équipe, pilotage web, planning, reprise) exige un niveau CONNU et ≤ cap.
//  - Dès que le niveau d'un perso est connu au-delà du cap (liste des persos, sélection, montée de niveau, licence changée),
//    tout ce qui tourne sur lui est arrêté et une fenêtre l'explique (une fois par perso et par session).
//  - Filet final : LaunchService refuse toute action forgée pour un perso au-delà du cap (point de passage unique).
public partial class BotsViewModel
{
    [ObservableProperty] public partial bool LevelCapModalVisible { get; set; }
    [ObservableProperty] public partial string LevelCapModalBody { get; set; } = "";

    private readonly HashSet<int> _levelCapNotified = new();   // persos déjà signalés (fenêtre automatique une seule fois)

    /// <summary>true = automatisation REFUSÉE sur ce perso (cap de niveau de la licence). <paramref name="interactive"/> : action
    /// explicite de l'utilisateur -> fenêtre (niveau trop élevé) ou message (niveau pas encore connu).</summary>
    private bool RefuseLevelCapped(int pid, bool interactive)
    {
        if (pid <= 0 || !Stores.Launch.LevelStartBlocked((uint)pid)) return false;
        int level = Stores.Launch.CharacterLevel((uint)pid);
        if (level <= 0)
        {
            AppendLuaLine("licence", Loc.T("L.Lua.LevelUnknownFree"), LuaLogLevel.Warning, pid);
            if (interactive) Toasts.Show(Loc.T("L.Lua.LevelUnknownFree"), success: false);
            return true;
        }
        AppendLuaLine("licence", Loc.T("L.Lua.LevelCapReached"), LuaLogLevel.Warning, pid);
        if (interactive) ShowLevelCapModal(pid);
        return true;
    }

    /// <summary>Motif court (accusé du pilotage web) si l'automatisation est refusée sur ce perso, sinon null.</summary>
    private static string? LevelCapRefusalReason(int pid) =>
        pid > 0 && Stores.Launch.LevelStartBlocked((uint)pid)
            ? (Stores.Launch.CharacterLevel((uint)pid) <= 0 ? "niveau du personnage inconnu (offre gratuite)" : "niveau du personnage au-delà de l'offre gratuite")
            : null;

    private void ShowLevelCapModal(int pid)
    {
        // Identité du perso d'abord (LaunchService la pose AVANT de signaler le niveau) : la ligne du bot n'a pas encore son nom
        // quand la fenêtre part à la sélection du perso (on affichait « ? est niveau 142 »).
        string? who = Stores.Launch.SelfCharacterInfo((uint)pid)?.Name;
        if (string.IsNullOrWhiteSpace(who) && _byPid.TryGetValue(pid, out var b)) who = b.Row.CharacterName;
        if (string.IsNullOrWhiteSpace(who)) who = Loc.T("L.LevelCap.ThisCharacter");
        LevelCapModalBody = Loc.Fmt("L.LevelCap.BodyFmt", who, Stores.Launch.CharacterLevel((uint)pid), LicenseService.LevelCap);
        if (!LevelCapModalVisible) { LevelCapModalVisible = true; AudioService.PlayUi(AudioService.ModalIn); }
    }

    [RelayCommand]
    private void CloseLevelCapModal() => LevelCapModalVisible = false;

    [RelayCommand]
    private void OpenLevelCapOffers()
    {
        Links.OpenPlans();
        LevelCapModalVisible = false;
    }

    // Niveau connu / changé (thread de fond) : un perso au-delà du cap ne doit plus rien automatiser.
    private void OnCharacterLevelKnownCap(uint pid, int level) => Dispatcher.UIThread.Post(() =>
    {
        if (Stores.Launch.LevelBlocked(pid)) EnforceLevelCap((int)pid);
        if (SelectedBot?.Pid == (int)pid) { OnPropertyChanged(nameof(LevelCapBlocked)); OnPropertyChanged(nameof(CanRunLua)); }
    });

    /// <summary>Arrête TOUT ce qui automatise ce perso (script, commande console, résurrection, suivi d'équipe) et prévient
    /// (fenêtre une fois par perso et par session).</summary>
    private void EnforceLevelCap(int pid)
    {
        bool stopped = false;
        if (_luaHosts.TryGetValue(pid, out var h) && h.Running) { try { h.Stop("levelcap"); } catch { /* best-effort */ } stopped = true; }
        if (_cmdHosts.Remove(pid, out var cmd)) { try { cmd.Dispose(); } catch { /* best-effort */ } stopped = true; }
        if (_reviveHosts.Remove(pid, out var rev)) { try { rev.Dispose(); } catch { /* best-effort */ } stopped = true; }
        CancelFollow(pid);
        if (stopped)
        {
            BotScripts.SetRunning(pid, false);
            if (SelectedBot?.Pid == pid) LuaRunning = false;
            AppendLuaLine("licence", Loc.T("L.Lua.LevelCapReached"), LuaLogLevel.Warning, pid);
            LicenseService.PulseBotTelemetry();
        }
        if (_levelCapNotified.Add(pid)) ShowLevelCapModal(pid);
    }

    // Licence changée (upgrade / downgrade / cap serveur modifié) : réévalue tous les persos connectés.
    private void EnforceLevelCapAll()
    {
        OnPropertyChanged(nameof(LevelCapBlocked));
        OnPropertyChanged(nameof(CanRunLua));
        OnPropertyChanged(nameof(LicenseLevelCap));
        foreach (var b in Bots)
            if (b.Pid > 0 && Stores.Launch.LevelBlocked((uint)b.Pid)) EnforceLevelCap(b.Pid);
    }
}
