using System;
using System.Collections.ObjectModel;
using System.Linq;
using System.Threading.Tasks;
using Avalonia.Media;
using Avalonia.Threading;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.App.Services;
using GivreClone.Protocol;

namespace GivreClone.App.ViewModels;

public sealed class CreationAccountChoice
{
    public string Login { get; init; } = "";
    public bool SocketSession { get; init; }
    public bool RememberedSocket { get; init; }
    public bool Running { get; init; }
    public int Pid { get; init; }
    public string Label { get; init; } = "";
}

public sealed class CreationServerChoice
{
    public int Id { get; init; }
    public string Name { get; init; } = "";
}

public sealed class CreationBreedChoice
{
    public int Index { get; init; }
    public string Label { get; init; } = "";
}

/// <summary>Page « Créer un personnage » ouverte depuis Comptes. Pas de suppression.</summary>
public partial class CharacterCreationTabViewModel : ViewModelBase
{
    private readonly AccountsViewModel _host;
    private CreationRoute _route = CreationRoute.Socket;
    private string? _appearanceError;

    public CharacterCreationTabViewModel(AccountsViewModel host)
    {
        _host = host;
        for (int i = 0; i < AccountsLogic.ColorSlots; i++)
            Colors.Add(new CreationColor { Slot = i, IsActive = i == 0 });
        for (int i = 0; CharacterInfoReader.BreedName(i) is { } name; i++)
            Breeds.Add(new CreationBreedChoice { Index = i, Label = name });
        CharacterCreationReports.Reported += OnReported;
    }

    public ObservableCollection<CreationAccountChoice> Accounts { get; } = new();
    public ObservableCollection<CreationServerChoice> Servers { get; } = new();
    public ObservableCollection<CreationBreedChoice> Breeds { get; } = new();
    public ObservableCollection<CreationColor> Colors { get; } = new();
    public PaletteSwatch[] Palette { get; } = BuildPalette();

    [ObservableProperty] public partial CreationAccountChoice? SelectedAccount { get; set; }
    [ObservableProperty] public partial CreationServerChoice? SelectedServer { get; set; }
    [ObservableProperty] public partial CreationBreedChoice? SelectedBreed { get; set; }
    [ObservableProperty] public partial int Sex { get; set; }
    [ObservableProperty] public partial string Name { get; set; } = "";
    [ObservableProperty] public partial string Notice { get; set; } = "";
    [ObservableProperty] public partial string Status { get; set; } = "Choisis le compte, le serveur, la classe et le sexe. Laisse le nom vide pour que le serveur le propose.";
    [ObservableProperty] public partial int StatusKind { get; set; }
    [ObservableProperty] public partial bool Busy { get; set; }

    public bool IsMale { get => Sex == 0; set { if (value) Sex = 0; } }
    public bool IsFemale { get => Sex == 1; set { if (value) Sex = 1; } }
    public bool HasNotice => !string.IsNullOrWhiteSpace(Notice);
    public string CreateLabel { get; private set; } = "Créer via Socket";
    public IBrush StatusBrush => StatusKind switch
    {
        1 => AccountImportRow.Ok,
        2 => AccountImportRow.Warn,
        _ => AccountImportRow.Neutral,
    };

    partial void OnNoticeChanged(string value) => OnPropertyChanged(nameof(HasNotice));
    partial void OnStatusKindChanged(int value) => OnPropertyChanged(nameof(StatusBrush));
    partial void OnSelectedAccountChanged(CreationAccountChoice? value) => UpdateNotice();
    partial void OnNameChanged(string value) => UpdateNotice();
    partial void OnSelectedBreedChanged(CreationBreedChoice? value) { LoadAppearance(); UpdateNotice(); }
    partial void OnSexChanged(int value)
    {
        OnPropertyChanged(nameof(IsMale));
        OnPropertyChanged(nameof(IsFemale));
        LoadAppearance();
        UpdateNotice();
    }

    public void Refresh()
    {
        string? login = SelectedAccount?.Login;
        int serverId = SelectedServer?.Id ?? 0;
        int breed = SelectedBreed?.Index ?? -1;
        try
        {
            Accounts.Clear();
            foreach (var row in _host.CreationAccounts()) Accounts.Add(row);
            SelectedAccount = Accounts.FirstOrDefault(a => string.Equals(a.Login, login, StringComparison.OrdinalIgnoreCase))
                ?? Accounts.FirstOrDefault();
            Servers.Clear();
            foreach (var (id, serverName) in _host.CreationServerList())
                Servers.Add(new CreationServerChoice { Id = id, Name = serverName });
            SelectedServer = Servers.FirstOrDefault(s => s.Id == serverId);
            SelectedBreed = Breeds.FirstOrDefault(b => b.Index == breed) ?? Breeds.FirstOrDefault();
            if (Servers.Count == 0)
            {
                Status = "Aucun serveur connu. Le jeu installé fournit la liste.";
                StatusKind = 2;
            }
        }
        catch
        {
            Status = "La liste des comptes ou des serveurs n'a pas pu être relue.";
            StatusKind = 2;
        }
        UpdateNotice();
    }

    [RelayCommand]
    private void Close() => _host.CloseCharacterCreation();

    [RelayCommand]
    private void SuggestServerName() => Name = "";

    [RelayCommand]
    private void SelectColor(CreationColor? slot)
    {
        if (slot is null) return;
        foreach (var color in Colors) color.IsActive = ReferenceEquals(color, slot);
    }

    [RelayCommand]
    private void ApplyColor(PaletteSwatch? swatch)
    {
        if (swatch is null) return;
        var slot = Colors.FirstOrDefault(c => c.IsActive) ?? Colors.FirstOrDefault();
        if (slot is null) return;
        slot.Rgb = swatch.Rgb;
    }

    [RelayCommand]
    private void RandomColors()
    {
        var colors = AccountsLogic.RandomColors(Random.Shared);
        for (int i = 0; i < Colors.Count && i < colors.Length; i++) Colors[i].Rgb = colors[i];
    }

    [RelayCommand]
    private void ClassColors()
    {
        foreach (var color in Colors) color.Rgb = AccountsLogic.NoColor;
    }

    [RelayCommand]
    private async Task CreateAsync()
    {
        if (Busy) return;
        var account = SelectedAccount;
        if (account is null) { Fail("Choisis un compte."); return; }
        var server = SelectedServer;
        var breed = SelectedBreed;
        string? form = CharacterCreationTab.FormError(server?.Id ?? 0, breed?.Index ?? -1, Sex, Name);
        if (form is not null) { Fail(form); return; }
        var look = _host.CreationAppearance(breed!.Index, Sex);
        if (look.Error is { } lookError) { Fail(lookError); return; }
        int[]? colors = CharacterCreationTab.ResolveColors(Colors.Select(c => c.Rgb).ToArray(), look.Defaults);
        if (colors is null)
        {
            Fail("Choisis les six couleurs, ou une classe dont les couleurs par défaut sont connues.");
            return;
        }
        var spec = new CharacterCreationSpec((Name ?? "").Trim(), Sex, ClassIconProvider.BreedId(breed.Index), look.Head, look.Body, colors, false);
        UpdateNotice();
        Busy = true;
        try
        {
            if (_route == CreationRoute.MitmList && account.Pid > 0)
            {
                var attempt = await _host.SendCreationOnListAsync(account.Pid, spec).ConfigureAwait(true);
                if (!string.IsNullOrWhiteSpace(attempt.Name)) Name = attempt.Name;
                if (!attempt.UseSocketInstead)
                {
                    Status = attempt.Status;
                    StatusKind = attempt.Created ? 1 : 2;
                    return;
                }
                Refresh();
                account = Accounts.FirstOrDefault(a => string.Equals(a.Login, account.Login, StringComparison.OrdinalIgnoreCase));
                if (account is null) { Fail("Choisis un compte."); return; }
                if (account.Running)
                {
                    Status = string.IsNullOrWhiteSpace(attempt.Status) ? CharacterCreationTab.StopClient : attempt.Status;
                    if (!Status.Contains(CharacterCreationTab.StopClient, StringComparison.Ordinal))
                        Status += " " + CharacterCreationTab.StopClient;
                    StatusKind = 2;
                    return;
                }
                if (!string.IsNullOrWhiteSpace(attempt.Name))
                    spec = spec with { Name = attempt.Name };
            }
            UpdateNotice();
            if (_route == CreationRoute.StopClientFirst || account.Running)
            {
                Status = string.IsNullOrWhiteSpace(Notice) ? CharacterCreationTab.StopClient : Notice;
                StatusKind = 2;
                return;
            }
            bool started;
            try { started = _host.BeginCharacterCreationSocket(account.Login, spec, server!.Id.ToString()); }
            catch { started = false; }
            if (!started)
            {
                Status = "Création Socket non démarrée.";
                StatusKind = 2;
                return;
            }
            Status = "Connexion Socket lancée : le personnage sera créé à l'arrivée de la liste.";
            StatusKind = 0;
            Refresh();
        }
        finally { Busy = false; }
    }

    private void Fail(string text)
    {
        Status = text;
        StatusKind = 2;
    }

    private void LoadAppearance()
    {
        if (SelectedBreed is null) return;
        var look = _host.CreationAppearance(SelectedBreed.Index, Sex);
        _appearanceError = look.Error;
        for (int i = 0; i < Colors.Count; i++)
            Colors[i].DefaultRgb = i < look.Defaults.Length ? look.Defaults[i] : AccountsLogic.NoColor;
    }

    private void UpdateNotice()
    {
        var account = SelectedAccount;
        if (account is null)
        {
            _route = CreationRoute.Socket;
            Notice = "";
            CreateLabel = "Créer via Socket";
            OnPropertyChanged(nameof(CreateLabel));
            return;
        }
        bool onList = account.Running && !account.SocketSession && account.Pid > 0 && _host.CreationOnList(account.Pid);
        var fields = _host.CreationFields();
        var context = new CreationContext(
            account.SocketSession,
            account.RememberedSocket,
            account.Running,
            onList,
            fields.Complete,
            string.IsNullOrWhiteSpace(Name),
            fields.CanSuggest);
        _route = CharacterCreationTab.Route(context);
        Notice = CharacterCreationTab.Notice(context);
        CreateLabel = _route == CreationRoute.MitmList ? "Créer" : "Créer via Socket";
        OnPropertyChanged(nameof(CreateLabel));
        if (_appearanceError is not null && string.IsNullOrWhiteSpace(Notice))
            Notice = _appearanceError;
    }

    private void OnReported(string login, string text, string? name)
    {
        try
        {
            Dispatcher.UIThread.Post(() =>
            {
                if (!string.Equals(SelectedAccount?.Login, login, StringComparison.OrdinalIgnoreCase)) return;
                if (!string.IsNullOrWhiteSpace(name)) Name = name;
                Status = text;
                StatusKind = text == CharacterCreationTab.CreatedText ? 1 : 2;
            });
        }
        catch { /* l'interface n'est plus là */ }
    }

    private static PaletteSwatch[] BuildPalette()
    {
        var swatches = new System.Collections.Generic.List<PaletteSwatch>();
        foreach (int gray in new[] { 0x000000, 0x4A4A4A, 0x8A8A8A, 0xC8C8C8, 0xFFFFFF })
            swatches.Add(new PaletteSwatch { Rgb = gray });
        foreach (double hue in new double[] { 0, 18, 32, 48, 70, 110, 150, 175, 195, 215, 240, 265, 290, 320, 345 })
        {
            swatches.Add(new PaletteSwatch { Rgb = Hsv(hue, 0.85, 0.95) });
            swatches.Add(new PaletteSwatch { Rgb = Hsv(hue, 0.55, 0.80) });
            swatches.Add(new PaletteSwatch { Rgb = Hsv(hue, 0.80, 0.50) });
        }
        return swatches.ToArray();
    }

    private static int Hsv(double h, double s, double v)
    {
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        (double r, double g, double b) = h switch
        {
            < 60 => (c, x, 0d),
            < 120 => (x, c, 0d),
            < 180 => (0d, c, x),
            < 240 => (0d, x, c),
            < 300 => (x, 0d, c),
            _ => (c, 0d, x),
        };
        return ((int)Math.Round((r + m) * 255) << 16) | ((int)Math.Round((g + m) * 255) << 8) | (int)Math.Round((b + m) * 255);
    }
}
