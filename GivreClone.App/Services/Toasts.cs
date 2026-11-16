using System;

namespace GivreClone.App.Services;

/// <summary>Toast transitoire (son + visuel) déclenché depuis les VMs, rendu au niveau fenêtre par MainWindow.</summary>
public static class Toasts
{
    /// <summary>(message, success, sound). success=false -> ton neutre/ambre (ex: retrait). sound=false -> visuel
    /// seul (quand le son a déjà été joué en amont, pour éviter un double son).</summary>
    public static event Action<string, bool, bool>? Requested;

    /// <summary>À appeler depuis le thread UI (les abonnés touchent des contrôles). Ailleurs : <see cref="Post"/>.</summary>
    public static void Show(string message, bool success = true, bool sound = true)
    {
        Requested?.Invoke(message, success, sound);
        // Le ton ambre est aussi utilisé pour les validations : ce n'est pas une gravité d'incident.
        // Les incidents sont publiés explicitement via Notifier avec Warning ou Error.
        Notifier.Push(success ? NotifKind.Success : NotifKind.Info, message);
    }

    /// <summary>Variante appelable depuis N'IMPORTE QUEL thread (ex. thread d'un script Lua : notify) : marshalée sur le
    /// thread UI.</summary>
    public static void Post(string message, bool success = true, bool sound = true)
        => Avalonia.Threading.Dispatcher.UIThread.Post(() => Show(message, success, sound));
}
