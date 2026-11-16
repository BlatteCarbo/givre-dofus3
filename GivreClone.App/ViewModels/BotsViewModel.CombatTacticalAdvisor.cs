using System;
using System.Threading;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;

namespace GivreClone.App.ViewModels;

/// <summary>
/// Classic desktop opt-in to the observation-only multiclasse tactical
/// advisor. The control cannot dispatch a spell/move and cannot convert a
/// local decoder event or a Sent result into an ACK.
/// </summary>
public partial class BotsViewModel
{
    private int _tacticalAdvisorUiRefreshQueued;

    public bool CombatTacticalAdvisorEnabled => _profileBot is { Pid: > 0 } b &&
        Stores.Launch.ReadCombatTacticalAdvisoryStatus((uint)b.Pid).Enabled;

    public string CombatTacticalAdvisorToggleLabel => CombatTacticalAdvisorEnabled
        ? "Désactiver le conseil" : "Activer le conseil";

    public string CombatTacticalAdvisorStatusText
    {
        get
        {
            if (_profileBot is not { Pid: > 0 } bot || !CombatProfileReady)
                return "Personnage non identifié : aucun conseil tactique.";
            CombatTacticalAdvisoryStatus current =
                Stores.Launch.ReadCombatTacticalAdvisoryStatus((uint)bot.Pid);
            if (!current.Enabled)
                return "Désactivé. Activation manuelle uniquement ; aucun envoi de sort ou mouvement.";
            string kind = current.ClassTactic?.ToString() ?? "aucun combo attesté";
            return $"{current.Phase} · Combat {current.FightId} · Tour {current.Turn} " +
                   $"· {kind} · {current.Reason} · action automatique interdite (preuve serveur insuffisante).";
        }
    }

    [RelayCommand]
    private void ToggleCombatTacticalAdvisor()
    {
        // The selected row and the loaded profile must still denote the same
        // bot. Changing UI selection must never opt-in a new/unknown PID.
        if (_profileBot is not { Pid: > 0 } bot ||
            !ReferenceEquals(SelectedBot, bot) || !CombatProfileReady)
            return;
        uint pid = (uint)bot.Pid;
        bool wasEnabled = Stores.Launch.ReadCombatTacticalAdvisoryStatus(pid).Enabled;
        _profile.TacticalAdvisorDiagnostic = !wasEnabled;
        SaveAndPush();
        Stores.Launch.SetCombatTacticalAdvisoryEnabled(pid, _profile.TacticalAdvisorDiagnostic);
        RefreshCombatTacticalAdvisorUi();
    }

    private void RestoreCombatTacticalAdvisorForLoadedProfile()
    {
        if (_profileBot is not { Pid: > 0 } bot ||
            !ReferenceEquals(SelectedBot, bot))
        {
            RefreshCombatTacticalAdvisorUi();
            return;
        }
        // Reapply only after the selected character has a resolved profile.
        // An old PID may have been recycled; reloading with a new character
        // resets the observer from the persisted character-specific choice.
        Stores.Launch.SetCombatTacticalAdvisoryEnabled((uint)bot.Pid,
            CombatProfileReady && _profile.TacticalAdvisorDiagnostic);
        RefreshCombatTacticalAdvisorUi();
    }

    private void RefreshCombatTacticalAdvisorUi()
    {
        OnPropertyChanged(nameof(CombatTacticalAdvisorEnabled));
        OnPropertyChanged(nameof(CombatTacticalAdvisorToggleLabel));
        OnPropertyChanged(nameof(CombatTacticalAdvisorStatusText));
    }

    private void OnCombatTacticalAdvisoryUpdated(uint pid, CombatTacticalAdvisoryStatus snapshot)
    {
        // Only update the selected bot and coalesce network-rate notifications
        // into at most one UI callback at a time. Snapshot is never treated as
        // an instruction, authority or an acknowledged cast.
        if (!snapshot.Enabled || SelectedBot?.Pid != (int)pid ||
            Interlocked.Exchange(ref _tacticalAdvisorUiRefreshQueued, 1) != 0)
            return;
        Dispatcher.UIThread.Post(() =>
        {
            try
            {
                if (SelectedBot?.Pid == (int)pid)
                    RefreshCombatTacticalAdvisorUi();
            }
            finally { Interlocked.Exchange(ref _tacticalAdvisorUiRefreshQueued, 0); }
        }, DispatcherPriority.Background);
    }
}
