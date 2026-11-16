using System;
using System.Collections.Generic;
using System.IO;
using System.Threading;
using System.Threading.Tasks;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;
using GivreClone.App.Views;

namespace GivreClone.App.ViewModels;

public partial class BotsViewModel
{
    private LocalNotificationPreferences? _localNotificationPreferences;
    private LocalBotNotifications? _localNotifications;
    private readonly ILocalToastDelivery _localToastDelivery = new WindowsLocalToastDelivery();
    public string LocalToastDeliveryStatus { get; private set; } = "Aucune soumission Windows";

    private void InitLocalNotifications()
    {
        try
        {
            _localNotificationPreferences = new(Path.Combine(MZP19LocalClone.DataRoot, "local-notification-preferences.json"));
            _localNotifications = new(_localNotificationPreferences.Get, PublishLocalNotification);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or System.Text.Json.JsonException)
        { LocalToastDeliveryStatus = "Préférences illisibles : notifications désactivées"; }

        Stores.Launch.PodsUpdated += (pid, current, maximum) => Dispatcher.UIThread.Post(() =>
        {
            if (RowOfPid(pid) is { } row) _localNotifications?.Pods(row.Model.Id, current, maximum);
        });
        Stores.Launch.FighterKilled += (pid, dead) =>
        {
            if (dead == 0 || dead != Stores.Launch.CharacterId(pid)) return;
            Dispatcher.UIThread.Post(() => { if (RowOfPid(pid) is { } row) _localNotifications?.Raise(row.Model.Id, LocalBotEvent.Death); });
        };
        Stores.Launch.FightOutcome += (pid, won, lost) => Dispatcher.UIThread.Post(() =>
        {
            if (won == lost || RowOfPid(pid) is not { } row) return;
            _localNotifications?.Raise(row.Model.Id, won ? LocalBotEvent.FightWon : LocalBotEvent.FightLost);
        });
        Stores.Launch.GameLinkLost += (pid, _, _) => Dispatcher.UIThread.Post(() =>
        {
            if (RowOfPid(pid) is { } row) _localNotifications?.Raise(row.Model.Id, LocalBotEvent.Disconnected);
        });
    }

    private void DriveLocalNotifications()
    {
        if (_supDir is null || _localNotifications is null) return;
        var ids = new HashSet<string>(StringComparer.Ordinal);
        foreach (var row in _accounts.Accounts)
        {
            ids.Add(row.Model.Id);
            if (_supDir.ByAccountId(row.Model.Id) is { } snapshot) _localNotifications.Observe(row.Model.Id, snapshot);
        }
        _localNotifications.Retain(ids);
    }

    private void PublishLocalNotification(LocalBotNotice notice)
    {
        Notifier.Push(NotifKind.Info, notice.Title, notice.Body);
        _ = DeliverLocalNotificationAsync(notice);
    }

    private async Task DeliverLocalNotificationAsync(LocalBotNotice notice)
    {
        LocalToastResult result;
        try { result = await _localToastDelivery.SendAsync(notice, CancellationToken.None); }
        catch { result = new(LocalToastStatus.Failed); }
        Dispatcher.UIThread.Post(() =>
        {
            LocalToastDeliveryStatus = result.Status == LocalToastStatus.Submitted
                ? "Soumis à Windows ; affichage non confirmé"
                : "Notification Windows : " + result.Status + (result.PlatformSetting is { } setting ? " (Setting=" + setting + ")" : "");
            OnPropertyChanged(nameof(LocalToastDeliveryStatus));
        });
    }

    [RelayCommand]
    private void ShowLocalNotificationSettings()
    {
        if (SelectedBot is not { } bot || _localNotificationPreferences is null) return;
        new LocalNotificationSettingsWindow(_localNotificationPreferences, bot.Row.Model.Id,
            () => LocalToastDeliveryStatus).Show();
    }
}
