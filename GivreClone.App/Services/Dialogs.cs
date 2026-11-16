using System;
using System.Threading.Tasks;

namespace GivreClone.App.Services;

public sealed record ConfirmRequest(string Title, string Body, string OkText, Action OnConfirm, bool Danger);

/// <summary>Demande de sélection d'un fichier (sélecteur natif de l'OS). Patterns façon "*.exe" ; Dofus.exe en tête.</summary>
public sealed record FilePickRequest(string Title, string FilterName, string[] Patterns);

// demandes de confirmation / sélection, affichées par MainWindow (qui détient le TopLevel/StorageProvider)
public static class Dialogs
{
    public static event Action<ConfirmRequest>? ConfirmRequested;

    /// <summary>Résolu par MainWindow : ouvre le sélecteur natif et renvoie le chemin local choisi (null si annulé).</summary>
    public static Func<FilePickRequest, Task<string?>>? FilePickHandler;

    public static void Confirm(string title, string body, Action onConfirm, string? okText = null, bool danger = false)
        => ConfirmRequested?.Invoke(new ConfirmRequest(title, body, okText ?? Loc.T("L.Common.Confirm"), onConfirm, danger));

    /// <summary>Ouvre un sélecteur de fichier natif (via MainWindow). Renvoie le chemin LOCAL, ou null si annulé / indisponible.</summary>
    public static Task<string?> PickFileAsync(string title, string filterName, params string[] patterns)
        => FilePickHandler?.Invoke(new FilePickRequest(title, filterName, patterns)) ?? Task.FromResult<string?>(null);
}
