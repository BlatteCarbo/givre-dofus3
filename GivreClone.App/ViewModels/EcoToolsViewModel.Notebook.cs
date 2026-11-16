using System;
using System.Collections.ObjectModel;
using System.Linq;
using Avalonia.Media;
using Avalonia.Media.Immutable;
using GivreClone.App.Services;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services.Eco;

namespace GivreClone.App.ViewModels;

public sealed record EcoSubRow(string Login, bool Manual, string Who, string EndText, string RemainingText, string LevelText, IBrush LevelBrush, string SeenText);

public sealed partial class EcoToolsViewModel
{
    private Notebook _notebook = new();

    public ObservableCollection<Notebook.Position> Positions { get; } = new();
    public ObservableCollection<Notebook.Note> Notes { get; } = new();
    public ObservableCollection<Notebook.Message> Messages { get; } = new();
    public ObservableCollection<Notebook.Position> PositionsView { get; } = new();
    public ObservableCollection<Notebook.Note> NotesView { get; } = new();
    public ObservableCollection<Notebook.Message> MessagesView { get; } = new();
    [ObservableProperty] public partial string NotebookFilter { get; set; } = "";
    [ObservableProperty] public partial bool NotebookCompact { get; set; }
    public bool NotebookEmpty => PositionsView.Count + NotesView.Count + MessagesView.Count == 0;
    partial void OnNotebookFilterChanged(string value) => FilterNotebook();

    private void FilterNotebook()
    {
        PositionsView.Clear(); NotesView.Clear(); MessagesView.Clear();
        foreach (var p in Positions.Where(p => NotebookSearch.Matches(NotebookFilter, p.Name, p.Note, Notebook.Coordinates(p)))) PositionsView.Add(p);
        foreach (var n in Notes.Where(n => NotebookSearch.Matches(NotebookFilter, n.Title, n.Text))) NotesView.Add(n);
        foreach (var m in Messages.Where(m => NotebookSearch.Matches(NotebookFilter, m.Title, m.Text))) MessagesView.Add(m);
        OnPropertyChanged(nameof(NotebookEmpty));
    }

    [ObservableProperty] public partial string NewPosName { get; set; } = "";
    [ObservableProperty] public partial string NewPosCoords { get; set; } = "";
    [ObservableProperty] public partial string NewPosNote { get; set; } = "";
    [ObservableProperty] public partial string NewNoteTitle { get; set; } = "";
    [ObservableProperty] public partial string NewNoteText { get; set; } = "";
    [ObservableProperty] public partial string NewMsgTitle { get; set; } = "";
    [ObservableProperty] public partial string NewMsgText { get; set; } = "";
    [ObservableProperty] public partial string NotebookStatus { get; set; } = "";

    /// <summary>Demande de copie dans le presse-papiers (gérée par la vue). Rien n'est jamais envoyé au chat du jeu.</summary>
    public event Action<string>? CopyRequested;

    private void ReloadNotebook()
    {
        Positions.Clear(); foreach (var p in _notebook.Positions) Positions.Add(p);
        Notes.Clear(); foreach (var n in _notebook.Notes) Notes.Add(n);
        Messages.Clear(); foreach (var m in _notebook.Messages) Messages.Add(m);
        FilterNotebook();
    }

    private void SaveNotebook()
    {
        try { EcoJson.Save(_nbPath, _notebook); NotebookStatus = ""; }
        catch (Exception e) { NotebookStatus = "Carnet gardé en mémoire seulement : " + e.Message; }
    }

    [RelayCommand]
    private void AddPosition()
    {
        if (!Notebook.TryParseCoordinates(NewPosCoords, out int x, out int y)) { NotebookStatus = "Coordonnées attendues : « x,y » (ex. 4,-18)."; return; }
        _notebook.Positions.Add(new Notebook.Position { Name = string.IsNullOrWhiteSpace(NewPosName) ? $"[{x},{y}]" : NewPosName.Trim(), X = x, Y = y, Note = NewPosNote.Trim() });
        NewPosName = NewPosCoords = NewPosNote = ""; SaveNotebook(); ReloadNotebook();
    }

    [RelayCommand] private void RemovePosition(Notebook.Position? p) { if (p is not null && _notebook.Positions.Remove(p)) { SaveNotebook(); ReloadNotebook(); } }
    [RelayCommand] private void CopyPosition(Notebook.Position? p) { if (p is not null) { CopyRequested?.Invoke(Notebook.Coordinates(p)); NotebookStatus = "Coordonnées copiées : " + Notebook.Coordinates(p); } }

    [RelayCommand]
    private void AddNote()
    {
        if (string.IsNullOrWhiteSpace(NewNoteText) && string.IsNullOrWhiteSpace(NewNoteTitle)) return;
        _notebook.Notes.Add(new Notebook.Note { Title = NewNoteTitle.Trim(), Text = NewNoteText.Trim() });
        NewNoteTitle = NewNoteText = ""; SaveNotebook(); ReloadNotebook();
    }

    [RelayCommand] private void RemoveNote(Notebook.Note? n) { if (n is not null && _notebook.Notes.Remove(n)) { SaveNotebook(); ReloadNotebook(); } }

    [RelayCommand]
    private void AddMessage()
    {
        if (string.IsNullOrWhiteSpace(NewMsgText)) return;
        _notebook.Messages.Add(new Notebook.Message { Title = NewMsgTitle.Trim(), Text = NewMsgText.Trim() });
        NewMsgTitle = NewMsgText = ""; SaveNotebook(); ReloadNotebook();
    }

    [RelayCommand] private void RemoveMessage(Notebook.Message? m) { if (m is not null && _notebook.Messages.Remove(m)) { SaveNotebook(); ReloadNotebook(); } }
    [RelayCommand] private void CopyMessage(Notebook.Message? m) { if (m is not null) { CopyRequested?.Invoke(m.Text); NotebookStatus = "Message copié : colle-le toi-même dans le chat."; } }

    // ============================== Abonnements ==============================
    public ObservableCollection<EcoSubRow> Subs { get; } = new();
    [ObservableProperty] public partial string SubsSummary { get; set; } = "";

    [RelayCommand]
    private void RefreshSubs()
    {
        Subs.Clear();
        var now = DateTime.UtcNow;
        var rows = SubscriptionStore.Locked(b => b.Entries.OrderBy(e => e.EndUtc).ToList());
        foreach (var e in rows)
        {
            var level = SubscriptionTracker.Level(e.EndUtc, now);
            (string label, string color) = level switch
            {
                SubscriptionLevel.Ok => ("OK", "#3FA45B"),
                SubscriptionLevel.Soon => ("Bientôt", "#E0A050"),
                SubscriptionLevel.Critical => ("Urgent", "#E06A50"),
                SubscriptionLevel.Expired => ("Expiré", "#C85450"),
                _ => ("Inconnu", "#8A93A0"),
            };
            Subs.Add(new EcoSubRow(e.Login, e.Manual, (e.Manual ? "✎ " : "") + (string.IsNullOrWhiteSpace(e.Character) ? e.Login : $"{e.Login} · {e.Character}"),
                e.EndUtc.Year < 2000 ? "—" : e.EndUtc.ToLocalTime().ToString("dd/MM/yyyy HH:mm"), SubscriptionTracker.Describe(e.EndUtc, now), label,
                new ImmutableSolidColorBrush(Color.Parse(color)), e.SeenUtc.ToLocalTime().ToString("dd/MM HH:mm")));
        }
        SubsSummary = Subs.Count == 0
            ? "Aucun abonnement suivi. Le jeu n'envoie pas toujours la date de fin : saisis-la ci-dessous (✎) ou connecte un compte qui la reçoit."
            : $"{Subs.Count} compte(s) suivi(s). Alertes Discord à 7, 3 et 1 jour(s) puis à l'expiration (webhook du personnage, Paramètres). ✎ = date saisie à la main.";
    }

    [ObservableProperty] public partial string SubLogin { get; set; } = "";
    [ObservableProperty] public partial string SubCharacter { get; set; } = "";
    [ObservableProperty] public partial DateTime? SubDate { get; set; } = DateTime.Today.AddDays(30);
    [ObservableProperty] public partial string SubTime { get; set; } = "22:00";

    [RelayCommand]
    private void SaveSub()
    {
        if (string.IsNullOrWhiteSpace(SubLogin) || SubDate is not { } d) { SubsSummary = "Indique le compte et la date de fin."; return; }
        if (!TimeSpan.TryParse(SubTime.Trim(), out var tod) || tod < TimeSpan.Zero || tod >= TimeSpan.FromDays(1)) { SubsSummary = "Heure invalide (HH:mm)."; return; }
        var local = new DateTime(d.Year, d.Month, d.Day, 0, 0, 0, DateTimeKind.Local) + tod;
        SubscriptionStore.Locked(b => SubscriptionTracker.SetManual(b, SubLogin.Trim(), SubCharacter.Trim(), local.ToUniversalTime(), DateTime.UtcNow));
        SubscriptionStore.Save();
        SubLogin = SubCharacter = "";
        RefreshSubs();
    }

    [RelayCommand]
    private void RemoveSub(EcoSubRow? r)
    {
        if (r is null) return;
        SubscriptionStore.Locked(b => b.Entries.RemoveAll(e => string.Equals(e.Login, r.Login, StringComparison.OrdinalIgnoreCase)));
        SubscriptionStore.Save();
        RefreshSubs();
    }
}
