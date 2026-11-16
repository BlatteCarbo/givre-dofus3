using System;
using System.IO;
using GivreClone.App.Services.Eco;

namespace GivreClone.App.Services;

/// <summary>Dernière fin d'abonnement vue par compte, persistée (%LOCALAPPDATA%\…\userdata\eco\subscriptions.json). Un seul exemplaire pour l'app.</summary>
public static class SubscriptionStore
{
    private static readonly object Gate = new();
    private static SubscriptionBook? _book;
    private static string PathFile => System.IO.Path.Combine(MZP19LocalClone.DataRoot, "eco", "subscriptions.json");

    public static SubscriptionBook Book { get { lock (Gate) return _book ??= EcoJson.Load<SubscriptionBook>(PathFile); } }

    public static void Save()
    {
        lock (Gate)
        {
            if (_book is null) return;
            try { EcoJson.Save(PathFile, _book); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* disque indisponible : mémoire seule */ }
        }
    }

    public static T Locked<T>(Func<SubscriptionBook, T> f) { lock (Gate) return f(Book); }
}
