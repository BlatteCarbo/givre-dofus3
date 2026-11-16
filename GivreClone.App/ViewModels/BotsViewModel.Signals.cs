using System.Collections.Generic;
using GivreClone.App.Services;
using GivreClone.App.Services.Signals;

namespace GivreClone.App.ViewModels;

// Pods pleins / PV bas hors combat : une notification à l'apparition du signal (pas à chaque tick), quelle que soit la page affichée.
public partial class BotsViewModel
{
    private readonly SignalEdges _signalEdges = new();
    private int _signalTick;

    private void CheckBotSignals()
    {
        if (++_signalTick % 10 != 0) return;   // ~3 s avec le tick de 300 ms
        RefreshStatPoints();
        var details = new Dictionary<string, (string Title, string Detail, string Name)>();
        foreach (var b in Bots)
        {
            if (b.Pid <= 0 || b.Row.Activity is BotActivity.Disconnected or BotActivity.Dead or BotActivity.Connecting) continue;
            string name = string.IsNullOrWhiteSpace(b.Row.CharacterName) ? "Bot" : b.Row.CharacterName!;
            // Pods pleins : déjà notifiés par LocalBotNotifications (préférences + délai) -> seulement les PV bas ici.
            foreach (var (category, detail) in BotSignals.For(new BotVitals(b.CharLife, b.CharMaxLife, 0, 0, Stores.Launch.IsInFight((uint)b.Pid))))
                details[b.Pid + ":" + category] = ($"{name} : {category.ToLowerInvariant()}", detail, name);
        }
        foreach (var key in _signalEdges.Rising(details.Keys))
        {
            var d = details[key];
            Notifier.Push(NotifKind.Warning, d.Title, d.Detail, d.Name);
        }
    }
}
