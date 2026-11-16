using System;

namespace GivreClone.App.Services;

/// <summary>Façade statique découplée du centre de notifications : n'importe quel service pousse une notification
/// (MP reçu/envoyé, événement...) sans référencer le ViewModel. Le <c>NotificationCenterViewModel</c> branche <see
/// cref="Sink"/> à sa création ; les appels avant l'UI sont ignorés (Sink null).</summary>
public static class Notifier
{
    public static Action<NotifKind, string, string, string?>? Sink;
    public static void Push(NotifKind kind, string title, string body = "", string? source = null)
        => Sink?.Invoke(kind, title, body ?? "", source);
}
