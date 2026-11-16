using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;

namespace GivreClone.App.ViewModels;

/// <summary>Une ligne de l'historique de notifications (enveloppe un <see cref="NotifItem"/> persisté).</summary>
public sealed partial class NotifRow : ObservableObject
{
    private static readonly IBrush Blue = new SolidColorBrush(Color.Parse("#6EA8FE"));
    private static readonly IBrush Green = new SolidColorBrush(Color.Parse("#63D3A6"));
    private static readonly IBrush Amber = new SolidColorBrush(Color.Parse("#E0A53F"));
    private static readonly IBrush Red = new SolidColorBrush(Color.Parse("#F0616D"));
    private static readonly IBrush Grey = new SolidColorBrush(Color.Parse("#A7B0C0"));

    public NotifItem Model { get; }
    public string Title => Model.Title;
    public string Body => Model.Body;
    public bool HasSource => !string.IsNullOrWhiteSpace(Model.Source);
    public string Source => Model.Source ?? "";
    /// <summary>Le corps est-il non vide ? -> le modal masque la zone corps pour une notif « titre seul » (ex. un toast).</summary>
    public bool HasBody => !string.IsNullOrWhiteSpace(Model.Body);
    public string TimeText { get; }
    public string FullTimeText { get; }

    /// <summary>Corps long -> on tronque dans la liste et on propose d'ouvrir le détail complet.</summary>
    public bool IsLong => Model.Body.Length > 140;
    public string Preview => IsLong ? Model.Body.Substring(0, 140).TrimEnd() + "…" : Model.Body;

    [ObservableProperty] public partial bool Read { get; set; }

    /// <summary>Passe à true juste avant le retrait -> RowOpacity tombe à 0 (transition) = fade-out doux avant suppression.</summary>
    [ObservableProperty] public partial bool Removing { get; set; }
    public double RowOpacity => Removing ? 0 : 1;
    partial void OnRemovingChanged(bool v) => OnPropertyChanged(nameof(RowOpacity));

    public string IconKey => Model.Kind switch
    {
        NotifKind.PmIn => "IconChat",
        NotifKind.PmOut => "IconSend",
        NotifKind.Success => "IconCheckCircle",
        _ => "IconInfo",
    };
    /// <summary>Géométrie de l'icône résolue depuis les ressources de l'app (bindée dans le template, pas de converter).</summary>
    public Geometry? IconGeo =>
        Avalonia.Application.Current is { } app && app.TryGetResource(IconKey, null, out var g) ? g as Geometry : null;
    public IBrush Accent => Model.Kind switch
    {
        NotifKind.PmIn => Blue,
        NotifKind.PmOut => Green,
        NotifKind.Success => Green,
        NotifKind.Warning => Amber,
        NotifKind.Error => Red,
        _ => Grey,
    };

    /// <summary>Étiquette de gravité lisible sans la couleur (vide hors incident).</summary>
    public string SeverityText => Model.Kind switch { NotifKind.Error => "Erreur", NotifKind.Warning => "Alerte", _ => "" };
    public bool IsIncident => SeverityText.Length > 0;
    /// <summary>Rang de tri « gravité d'abord » : erreur, alerte, puis le reste.</summary>
    public int SeverityRank => Model.Kind switch { NotifKind.Error => 0, NotifKind.Warning => 1, _ => 2 };

    /// <summary>Texte complet copié depuis le détail : le corps n'est jamais tronqué.</summary>
    public string CopyText => string.Join("\n", new[] { Title, HasSource ? Source : null, FullTimeText, HasBody ? Body : null }
        .Where(part => !string.IsNullOrEmpty(part)));

    public NotifRow(NotifItem model)
    {
        Model = model;
        Read = model.Read;
        var local = DateTimeOffset.FromUnixTimeMilliseconds(model.Ts).LocalDateTime;
        // Format NUMÉRIQUE (InvariantGlobalization=true dans le csproj -> pas de noms de mois/jour localisés ; CurrentUICulture
        // tombait sur l'anglais). dd/MM = neutre et sans ambiguïté pour un utilisateur FR.
        TimeText = local.ToString("dd/MM HH:mm", CultureInfo.InvariantCulture);
        var zoned = DateTimeOffset.FromUnixTimeMilliseconds(model.Ts).ToLocalTime();
        FullTimeText = zoned.ToString("dd/MM/yyyy · HH:mm:ss", CultureInfo.InvariantCulture)
            + " (UTC" + zoned.ToString("zzz", CultureInfo.InvariantCulture) + ")";
    }
}

/// <summary>Centre de notifications : cloche (barre de titre) + historique persistant + modal de détail + son. Alimenté
/// via <see cref="Notifier.Push"/> (MP reçus/envoyés, toasts, événements). Persisté dans notifications.json.</summary>
public partial class NotificationCenterViewModel : ObservableObject
{
    /// <summary>Historique, le + récent en tête.</summary>
    public ObservableCollection<NotifRow> Items { get; } = new();

    [ObservableProperty] public partial int UnreadCount { get; set; }
    /// <summary>Compteur de la pastille : non-lus VISIBLES quand un filtre est actif, sinon tous les non-lus.</summary>
    public int BadgeCount => HasFilters ? VisibleItems.Count(r => !r.Read) : UnreadCount;
    public bool HasUnread => BadgeCount > 0;
    public string UnreadBadge => BadgeCount > 9 ? "9+" : BadgeCount.ToString(CultureInfo.InvariantCulture);
    partial void OnUnreadCountChanged(int value) => RaiseBadge();
    private void RaiseBadge()
    {
        OnPropertyChanged(nameof(BadgeCount));
        OnPropertyChanged(nameof(HasUnread));
        OnPropertyChanged(nameof(UnreadBadge));
    }
    public bool IsEmpty => Items.Count == 0;
    [ObservableProperty] public partial bool UnreadOnly { get; set; }
    [ObservableProperty] public partial bool IncidentsOnly { get; set; }
    [ObservableProperty] public partial string NotificationQuery { get; set; } = "";
    [ObservableProperty] public partial string SourceQuery { get; set; } = "";
    /// <summary>Tri par gravité (erreurs, alertes, reste) ; l'ordre récent -> ancien est conservé dans chaque groupe.</summary>
    [ObservableProperty] public partial bool SeverityFirst { get; set; }
    public IReadOnlyList<NotifRow> VisibleItems => Items
        .OrderBy(row => SeverityFirst ? row.SeverityRank : 0)
        .Where(row => (!UnreadOnly || !row.Read)
            && (!IncidentsOnly || row.Model.Kind is NotifKind.Warning or NotifKind.Error)
            && (string.IsNullOrWhiteSpace(NotificationQuery)
                || row.Title.Contains(NotificationQuery.Trim(), StringComparison.OrdinalIgnoreCase)
                || row.Body.Contains(NotificationQuery.Trim(), StringComparison.OrdinalIgnoreCase))
            && (string.IsNullOrWhiteSpace(SourceQuery)
                || row.Source.Contains(SourceQuery.Trim(), StringComparison.OrdinalIgnoreCase)))
        .ToArray();
    public bool NoVisibleItems => VisibleItems.Count == 0;
    public string FilterCount => $"{VisibleItems.Count} / {Items.Count}";
    public bool HasFilters => UnreadOnly || IncidentsOnly
        || !string.IsNullOrWhiteSpace(NotificationQuery) || !string.IsNullOrWhiteSpace(SourceQuery);
    public string MarkReadLabel => HasFilters ? "Marquer les visibles lus" : "Tout marquer lu";
    partial void OnUnreadOnlyChanged(bool value) => RefreshFilters();
    partial void OnIncidentsOnlyChanged(bool value) => RefreshFilters();
    partial void OnSeverityFirstChanged(bool value) => RefreshFilters();
    partial void OnNotificationQueryChanged(string value) => RefreshFilters();
    partial void OnSourceQueryChanged(string value) => RefreshFilters();
    private void RefreshFilters()
    {
        OnPropertyChanged(nameof(VisibleItems));
        OnPropertyChanged(nameof(NoVisibleItems));
        OnPropertyChanged(nameof(FilterCount));
        OnPropertyChanged(nameof(HasFilters));
        OnPropertyChanged(nameof(MarkReadLabel));
        RaiseBadge();
    }

    [ObservableProperty] public partial bool IsOpen { get; set; }        // panneau (flyout) ouvert
    partial void OnIsOpenChanged(bool value) { if (value) RefreshFilters(); }
    [ObservableProperty] public partial bool DetailVisible { get; set; } // modal de détail
    [ObservableProperty] public partial NotifRow? Detail { get; set; }

    private DateTime _lastSound = DateTime.MinValue;

    public NotificationCenterViewModel()
    {
        foreach (var it in NotificationStore.Load()) Items.Insert(0, new NotifRow(it));   // fichier = ancien->récent ; on met le récent en tête
        RecountUnread();
        Notifier.Sink = (kind, title, body, source) => Push(kind, title, body, source);
    }

    // Appelé depuis n'importe quel thread -> on marshale sur l'UI (mutation de collection + son).
    private void Push(NotifKind kind, string title, string body, string? source)
    {
        if (string.IsNullOrWhiteSpace(title)) return;
        void Do()
        {
            var item = new NotifItem
            {
                Ts = DateTimeOffset.UtcNow.ToUnixTimeMilliseconds(),
                Title = title.Trim(),
                Body = (body ?? "").Trim(),
                Kind = kind,
                Source = string.IsNullOrWhiteSpace(source) ? null : source!.Trim(),
                Read = false,
            };
            Items.Insert(0, new NotifRow(item));
            while (Items.Count > 200) Items.RemoveAt(Items.Count - 1);
            RecountUnread();
            OnPropertyChanged(nameof(IsEmpty));
            Persist();
            // Son : throttlé (>= 1,5 s entre deux) pour ne pas spammer l'audio sur une rafale de MP.
            var now = DateTime.UtcNow;
            if ((now - _lastSound).TotalMilliseconds >= 1500) { _lastSound = now; AudioService.PlayUi(AudioService.Notify); }
        }
        if (Dispatcher.UIThread.CheckAccess()) Do();
        else Dispatcher.UIThread.Post(Do);
    }

    private void RecountUnread()
    {
        UnreadCount = Items.Count(i => !i.Read);
        RefreshFilters();
    }

    private void Persist()
    {
        var models = new List<NotifItem>(Items.Count);
        for (int i = Items.Count - 1; i >= 0; i--) models.Add(Items[i].Model);
        NotificationStore.SaveInBackground(models);
    }   // ré-ordonne récent->ancien -> ancien->récent ; écriture hors UI

    // Consulter la liste conserve les incidents non lus ; seuls le détail et
    // la commande explicite « tout marquer lu » acquittent les notifications.
    [RelayCommand] private void ToggleOpen() => IsOpen = !IsOpen;
    [RelayCommand] private void CloseOpen() => IsOpen = false;

    [RelayCommand]
    private void MarkAllRead()
    {
        bool any = false;
        // Avec un filtre actif, seules les lignes affichées sont acquittées : rien de caché n'est lu en silence.
        foreach (var r in HasFilters ? VisibleItems : Items) if (!r.Read) { r.Read = true; r.Model.Read = true; any = true; }
        if (any) { RecountUnread(); Persist(); }
    }

    /// <summary>« Acquitter les visibles » : acquitte UNIQUEMENT les lignes affichées par les filtres courants.</summary>
    [RelayCommand]
    private void AcknowledgeVisible()
    {
        bool any = false;
        foreach (var r in VisibleItems) if (!r.Read) { r.Read = true; r.Model.Read = true; any = true; }
        if (any) { RecountUnread(); Persist(); }
    }

    /// <summary>Copie le détail complet (titre, source, horodatage, corps) dans le presse-papier.</summary>
    [RelayCommand]
    private async System.Threading.Tasks.Task CopyDetails(NotifRow? row)
    {
        if (row is null) return;
        await ClipboardService.CopyAsync(row.CopyText).ConfigureAwait(true);
        Toasts.Show(Loc.T("L.Notif.Copied"), success: true, sound: false);
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task ClearAll()
    {
        if (Items.Count == 0) return;
        AudioService.PlayUi(AudioService.Dismiss);    // son distinct de retrait (whoosh), pas le clic générique
        foreach (var r in Items) r.Removing = true;   // fade-out de toutes les lignes
        await System.Threading.Tasks.Task.Delay(190);
        Items.Clear();
        RecountUnread();
        OnPropertyChanged(nameof(IsEmpty));
        Persist();
    }

    [RelayCommand]
    private async System.Threading.Tasks.Task Dismiss(NotifRow? row)
    {
        if (row is null) return;
        AudioService.PlayUi(AudioService.Dismiss);    // petit son distinct de retrait (whoosh), pas le clic générique
        row.Removing = true;                          // fade-out doux avant le retrait effectif
        await System.Threading.Tasks.Task.Delay(190);
        Items.Remove(row);
        RecountUnread();
        OnPropertyChanged(nameof(IsEmpty));
        Persist();
    }

    // Clic sur une notification : marque lu, puis ouvre TOUJOURS le modal de détail complet (le titre/corps est souvent
    // tronqué dans la liste étroite — les toasts mettent tout dans le titre, corps vide -> le modal montre le texte entier).
    [RelayCommand]
    private void Open(NotifRow? row)
    {
        if (row is null) return;
        AudioService.PlayUi(AudioService.ModalIn);   // retour sonore : ouverture du modal de détail
        if (!row.Read) { row.Read = true; row.Model.Read = true; RecountUnread(); Persist(); }
        Detail = row;
        DetailVisible = true;
        IsOpen = false;
    }

    [RelayCommand] private void CloseDetail() => DetailVisible = false;
}
