using System;
using CommunityToolkit.Mvvm.ComponentModel;
using GivreClone.App.Services;
using GivreClone.Engine;

namespace GivreClone.App.ViewModels;

public partial class BotSettingsViewModel
{
    [ObservableProperty] public partial bool RegenerationEnabled { get; set; }
    [ObservableProperty] public partial int RegenerationStartPercent { get; set; } = 60;
    [ObservableProperty] public partial int RegenerationStopPercent { get; set; } = 100;
    [ObservableProperty] public partial int RegenerationFoodGid { get; set; }
    [ObservableProperty] public partial bool RegenerationWaitForTeam { get; set; }
    [ObservableProperty] public partial string RegenerationStatus { get; set; } = "";

    private void LoadRegeneration()
    {
        RegenerationEnabled = _cfg.RegenerationEnabled;
        RegenerationStartPercent = Math.Clamp(_cfg.RegenerationStartPercent, 1, 100);
        RegenerationStopPercent = Math.Clamp(_cfg.RegenerationStopPercent, RegenerationStartPercent, 100);
        RegenerationFoodGid = Math.Max(0, _cfg.RegenerationFoodGid);
        RegenerationWaitForTeam = _cfg.RegenerationWaitForTeam;
        RegenerationStatus = "";
    }

    partial void OnRegenerationEnabledChanged(bool value) => SaveRegeneration();
    partial void OnRegenerationStartPercentChanged(int value) => SaveRegeneration();
    partial void OnRegenerationStopPercentChanged(int value) => SaveRegeneration();
    partial void OnRegenerationFoodGidChanged(int value) => SaveRegeneration();
    partial void OnRegenerationWaitForTeamChanged(bool value) => SaveRegeneration();

    private void SaveRegeneration()
    {
        if (_loading) return;
        _cfg.RegenerationEnabled = RegenerationEnabled;
        _cfg.RegenerationStartPercent = Math.Clamp(RegenerationStartPercent, 1, 100);
        _cfg.RegenerationStopPercent = Math.Clamp(RegenerationStopPercent, _cfg.RegenerationStartPercent, 100);
        _cfg.RegenerationFoodGid = Math.Max(0, RegenerationFoodGid);
        _cfg.RegenerationWaitForTeam = RegenerationWaitForTeam;
        Save();
    }

    public void RefreshRegeneration(uint pid)
    {
        var launch = Stores.Launch;
        RegenerationStatus = Loc.T(!RegenerationEnabled ? "L.Bots.Settings.RegenOff"
            : launch.AuthoritativeLifePercent(pid) is null ? "L.Bots.Settings.RegenUnknown"
            : !launch.RegenerationTeamReady(pid) ? "L.Bots.Settings.RegenWaiting"
            : "L.Bots.Settings.RegenReady");
    }
}
