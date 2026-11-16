using System;
using System.IO;
using GivreClone.App.Services.Eco;

namespace GivreClone.App.Services;

/// <summary>Alertes de prix persistées (%LOCALAPPDATA%\...\userdata\eco\alerts.json). Un seul exemplaire pour l'app.</summary>
public static class PriceAlertStore
{
    private static readonly object Gate = new();
    private static PriceAlertBook? _book;
    private static string PathFile => System.IO.Path.Combine(MZP19LocalClone.DataRoot, "eco", "alerts.json");

    public static T Locked<T>(Func<PriceAlertBook, T> f) { lock (Gate) return f(_book ??= EcoJson.Load<PriceAlertBook>(PathFile)); }

    public static void Save()
    {
        lock (Gate)
        {
            if (_book is null) return;
            try { EcoJson.Save(PathFile, _book); } catch (Exception e) when (e is IOException or UnauthorizedAccessException) { /* disque indisponible : mémoire seule */ }
        }
    }
}
