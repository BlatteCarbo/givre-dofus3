using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Linq;
using System.Threading.Tasks;
using CommunityToolkit.Mvvm.ComponentModel;
using CommunityToolkit.Mvvm.Input;
using GivreClone.Engine.Accounts;
using GivreClone.Engine.Proxies;
using GivreClone.App.Services;
using GivreClone.App.Services.Transport.Socket;
using GivreClone.Protocol;

namespace GivreClone.App.ViewModels;

public partial class AccountRow
{
    /// <summary>Case de sélection multiple de la carte.</summary>
    [ObservableProperty] public partial bool IsSelected { get; set; }

    /// <summary>Libellé du bouton qui bascule le mode de connexion (MITM ↔ Socket).</summary>
    public string SwitchModeText => ModeIsSocket ? "Passer en MITM" : "Passer en Socket";

    protected override void OnPropertyChanged(PropertyChangedEventArgs e)
    {
        base.OnPropertyChanged(e);
        if (e.PropertyName == nameof(ModeIsSocket)) OnPropertyChanged(nameof(SwitchModeText));
    }
}

// Onglet Comptes : sélection multiple, import en masse, pseudo, création de personnage (MZP23). Logique pure dans AccountsLogic (testée).
// Les envois réseau de création de personnage et de choix de pseudo NE SONT PAS branchés : le mapping 3.7 candidat n'a ni message
// de pseudo ni sous-structure de CharacterCreationRequest résolue ; ces commandes valident puis l'annoncent clairement.
public partial class AccountsViewModel
{
    private const string NotWired = "Envoi indisponible : message réseau non résolu dans le mapping 3.7 du clone.";
    private bool _multiWired;

    private void WireMulti()
    {
        if (_multiWired) return;
        _multiWired = true;
        foreach (var r in Accounts) r.PropertyChanged += OnRowForSelection;
        Accounts.CollectionChanged += (_, e) =>
        {
            if (e.NewItems is not null) foreach (AccountRow r in e.NewItems) r.PropertyChanged += OnRowForSelection;
            RaiseSelection();
        };
        foreach (var o in CharacterBreedOptions(0)) CreateBreeds.Add(o);
        CreateSexes.Add(new PickOption { Value = 0, Label = "♂  Masculin" });
        CreateSexes.Add(new PickOption { Value = 1, Label = "♀  Féminin" });
        for (int i = 0; i < AccountsLogic.ColorSlots; i++) CreateColors.Add(new CreationColor { Slot = i });
        Loc.Changed += () => OnPropertyChanged(nameof(LaunchSelectedLabel));
    }

    private static IEnumerable<PickOption> CharacterBreedOptions(int gender)
    {
        for (int i = 0; CharacterInfoReader.BreedName(i) is { } n; i++)
            yield return new PickOption { Value = i, Label = n, Icon = Stores.ClassIcons.Get(i, gender) };
    }

    private void OnRowForSelection(object? s, PropertyChangedEventArgs e)
    {
        if (e.PropertyName is nameof(AccountRow.IsSelected) or nameof(AccountRow.Running)) RaiseSelection();
    }

    // ---- Sélection multiple ----
    public IEnumerable<AccountRow> SelectedRows => Accounts.Where(a => a.IsSelected);
    public string SelectionText => AccountsLogic.SelectionText(SelectedRows.Count(), Accounts.Count);

    /// <summary>Case « tout » à trois états (null = partiel). L'écriture null est ignorée (pas de sélection implicite).</summary>
    public bool? AllSelected
    {
        get => AccountsLogic.AllSelectedState(SelectedRows.Count(), Accounts.Count);
        set
        {
            if (value is not bool b) return;
            foreach (var r in Accounts) r.IsSelected = b;
            RaiseSelection();
        }
    }

    [ObservableProperty] public partial ProxyEntry? SelectionProxy { get; set; }
    public ObservableCollection<ProxyEntry> SelectionProxyChoices => Proxies;

    private void RaiseSelection()
    {
        OnPropertyChanged(nameof(SelectionText));
        OnPropertyChanged(nameof(AllSelected));
        OnPropertyChanged(nameof(LaunchSelectedCount));
        OnPropertyChanged(nameof(LaunchSelectedLabel));
        LaunchSelectedCommand.NotifyCanExecuteChanged();
    }

    /// <summary>Comptes que « Lancer la sélection » lancera : cochés explicitement ET hors ligne. Rien d'autre.</summary>
    public List<AccountRow> LaunchSelectionTargets() => SelectedRows.Where(r => !r.Running).ToList();
    public int LaunchSelectedCount => LaunchSelectionTargets().Count;
    public string LaunchSelectedLabel => Loc.Fmt("L.Accounts.LaunchSelected", LaunchSelectedCount);
    private bool CanLaunchSelected() => LaunchSelectedCount > 0;

    [RelayCommand]
    private void AssignSelectedProxy()
    {
        if (SelectionProxy is not { } p) { Toasts.Show("Choisis d'abord un proxy.", success: false); return; }
        int n = 0, skipped = 0;
        foreach (var r in SelectedRows.ToList())
        {
            if (r.Running) { skipped++; continue; }   // jamais de changement de route sous un compte en ligne
            r.SelectedProxy = p; n++;
        }
        Toasts.Show(skipped > 0 ? $"Proxy appliqué à {n} compte(s) ; {skipped} en ligne ignoré(s)." : $"Proxy appliqué à {n} compte(s).", success: n > 0);
    }

    [RelayCommand]
    private void SetSelectedMode(string? mode)
    {
        bool socket = string.Equals(mode, "socket", StringComparison.OrdinalIgnoreCase);
        int n = 0;
        foreach (var r in SelectedRows.Where(r => !r.Running && r.IsSocketMode != socket).ToList()) { r.IsSocketMode = socket; n++; }
        Toasts.Show($"{n} compte(s) passé(s) en {(socket ? "Socket" : "MITM")}.", success: n > 0);
    }

    [RelayCommand(CanExecute = nameof(CanLaunchSelected))]
    private void LaunchSelected()
    {
        var rows = LaunchSelectionTargets();
        if (rows.Count == 0) return;
        Dialogs.Confirm("Lancer la sélection", $"Lancer {rows.Count} compte(s) sélectionné(s) ?", onConfirm: () => _ = LaunchSelectedAsync(rows));
    }

    private async Task LaunchSelectedAsync(List<AccountRow> rows)
    {
        if (MainViewModel.Instance is { } mvm) await mvm.EnsureMappingReadyAsync();   // même garde que « Lancer tout »
        var missing = MappingCatalog.MissingEssentials();
        if (missing.Count > 0) { WarnMappingMissing(missing, () => _ = LaunchSelectedCoreAsync(rows)); return; }
        await LaunchSelectedCoreAsync(rows);
    }

    private async Task LaunchSelectedCoreAsync(List<AccountRow> rows)
    {
        bool multi = LicenseService.HasPermission(LicenseService.Perm.Multi);
        foreach (var row in rows)
        {
            if (row.Running) continue;
            if (!multi && Accounts.Any(a => a.Running)) { Toasts.Show(Loc.T("L.Perm.MultiDenied"), success: false); break; }
            await LaunchCoreAsync(row).ConfigureAwait(true);
        }
    }

    [RelayCommand]
    private void StopSelected()
    {
        var rows = SelectedRows.Where(r => r.Running || r.ReconnectPending).ToList();
        if (rows.Count == 0) return;
        Dialogs.Confirm("Arrêter la sélection", $"Arrêter {rows.Count} compte(s) ?", onConfirm: () =>
        {
            foreach (var r in rows)
            {
                if (r.Running) { StopCore(r); UserStopped?.Invoke(r); }
                else CancelPendingReconnect(r);
            }
            Toasts.Show($"{rows.Count} compte(s) arrêté(s).");
        }, danger: true);
    }

    [RelayCommand]
    private void RemoveSelected()
    {
        var rows = SelectedRows.ToList();
        if (rows.Count == 0) return;
        Dialogs.Confirm("Supprimer la sélection", $"Supprimer {rows.Count} compte(s) ? Les comptes en ligne sont d'abord arrêtés.", onConfirm: () =>
        {
            foreach (var r in rows)
            {
                if (r.Running) StopCore(r);
                else if (r.Pid is int pid && !RelaunchOwnedBy(pid, r)) TryKill(pid);   // même teardown que Remove
                Stores.Accounts.Remove(r.Model.Id);
            }
            Reload();
            Toasts.Show($"{rows.Count} compte(s) supprimé(s).");
        }, danger: true);
    }

    [RelayCommand]
    private void OpenAccountBrowser(AccountRow? row)
    {
        if (row is null) return;
        Toasts.Show("Navigateur de compte : utilise « Ajouter via navigateur » ; l'ouverture directe n'est pas disponible dans le clone.", success: false);
    }

    // ---- Import de comptes ----
    [ObservableProperty] public partial bool ImportVisible { get; set; }
    [ObservableProperty] public partial string ImportText { get; set; } = "";
    [ObservableProperty] public partial ProxyEntry? ImportProxy { get; set; } = ProxyEntry.None;
    [ObservableProperty] public partial int ImportStep { get; set; }   // 0 = saisie, 1 = résultats
    [ObservableProperty] public partial bool ImportBusy { get; set; }
    [ObservableProperty] public partial bool ImportDone { get; set; }
    public ObservableCollection<AccountImportRow> ImportResults { get; } = new();
    public ObservableCollection<ProxyEntry> ImportProxyChoices => Proxies;

    private AccountImportResult _importParsed = AccountsLogic.ParseAccounts(null);
    public string ImportPreview => string.IsNullOrWhiteSpace(ImportText) ? "" : _importParsed.Summary;
    public bool ImportReady => _importParsed.Valid.Count > 0 && !ImportBusy;
    public bool ImportEditing => ImportStep == 0 && !ImportBusy;

    partial void OnImportTextChanged(string value)
    {
        _importParsed = AccountsLogic.ParseAccounts(value);
        OnPropertyChanged(nameof(ImportPreview));
        OnPropertyChanged(nameof(ImportReady));
    }
    partial void OnImportBusyChanged(bool value) { OnPropertyChanged(nameof(ImportReady)); OnPropertyChanged(nameof(ImportEditing)); }
    partial void OnImportStepChanged(int value) => OnPropertyChanged(nameof(ImportEditing));

    [RelayCommand]
    private void OpenImport()
    {
        ImportText = ""; ImportResults.Clear(); ImportStep = 0; ImportDone = false; ImportProxy = ProxyEntry.None;
        ImportVisible = true;
    }

    [RelayCommand] private void CloseImport() { ImportVisible = false; ImportText = ""; }

    [RelayCommand]
    private async Task ImportAccounts()
    {
        if (!ImportReady) return;
        ImportBusy = true;
        try
        {
            ImportResults.Clear();
            var existing = Stores.Accounts.Accounts.Select(a => a.Login).ToList();
            foreach (var p in _importParsed.Valid)
            {
                var row = new AccountImportRow { Login = p.Login };
                if (AccountsLogic.StatusOf(p.Login, existing) == AccountsLogic.ImportStatus.AlreadyExists)
                { row.Result = "Existe déjà"; row.Brush = AccountImportRow.Warn; }
                else
                {
                    var acc = Stores.Accounts.Add(p.Login, p.Password, null);
                    if (ImportProxy is { IsNone: false } px) { acc.ProxyId = px.Id; Stores.Accounts.Persist(); }
                    existing.Add(p.Login);
                    row.Result = "Ajouté"; row.Brush = AccountImportRow.Ok;
                }
                ImportResults.Add(row);
                await Task.Yield();
            }
            Reload();
            ImportStep = 1; ImportDone = true;
            Toasts.Show($"{ImportResults.Count(r => r.Result == "Ajouté")} compte(s) importé(s).");
        }
        finally { ImportBusy = false; }
    }

    // ---- Pseudo ----
    [ObservableProperty] public partial bool NicknameVisible { get; set; }
    [ObservableProperty] public partial string NicknameLogin { get; set; } = "";
    [ObservableProperty] public partial string NicknameText { get; set; } = "";
    [ObservableProperty] public partial string NicknameError { get; set; } = "";
    [ObservableProperty] public partial bool NicknameBusy { get; set; }

    [RelayCommand] private void NewNicknameSuggestion() { NicknameText = AccountsLogic.SuggestName(Random.Shared); NicknameError = ""; }
    [RelayCommand] private void CancelNickname() { NicknameVisible = false; NicknameError = ""; }

    [RelayCommand]
    private void ConfirmNickname()
    {
        NicknameError = AccountsLogic.ValidateName(NicknameText) ?? NotWired;   // valide d'abord ; l'envoi lui-même n'est pas câblé
    }

    // ---- Création de personnage (mode Socket) ----
    public ObservableCollection<PickOption> CreateBreeds { get; } = new();
    public ObservableCollection<PickOption> CreateSexes { get; } = new();
    public ObservableCollection<PickOption> CreateHeads { get; } = new();    // aucune source de têtes dans le clone
    public ObservableCollection<PickOption> CreateBodies { get; } = new();   // idem pour les corps
    public ObservableCollection<CreationColor> CreateColors { get; } = new();
    [ObservableProperty] public partial string CreateName { get; set; } = "";
    [ObservableProperty] public partial int CreateBreed { get; set; } = -1;
    [ObservableProperty] public partial int CreateSex { get; set; } = -1;
    [ObservableProperty] public partial int CreateHead { get; set; } = -1;
    [ObservableProperty] public partial int CreateBody { get; set; } = -1;
    [ObservableProperty] public partial bool CreateLeaveTutorial { get; set; } = true;
    public bool CreateNeedsSex => CreateBreed >= 0;
    public bool HasCreateColors => CreateColors.Any(c => c.Rgb >= 0);
    partial void OnCreateBreedChanged(int value) { OnPropertyChanged(nameof(CreateNeedsSex)); RefreshCreateParts(); OnPropertyChanged(nameof(CreateSummary)); }
    partial void OnCreateSexChanged(int value) { RefreshCreateBreeds(); RefreshCreateParts(); OnPropertyChanged(nameof(CreateSummary)); }
    partial void OnCreateHeadChanged(int value) => OnPropertyChanged(nameof(CreateSummary));
    partial void OnCreateBodyChanged(int value) => OnPropertyChanged(nameof(CreateSummary));
    partial void OnCreateNameChanged(string value) => OnPropertyChanged(nameof(CreateSummary));

    /// <summary>« ♀ Féminin · Crâ · Tête 3 · Classique · nom proposé par le serveur » : récapitulatif de ce qui sera créé.</summary>
    public string CreateSummary
    {
        get
        {
            string sex = CreateSex switch { 0 => "♂ Masculin", 1 => "♀ Féminin", _ => "sexe ?" };
            string cls = CreateBreed >= 0 && CharacterInfoReader.BreedName(CreateBreed) is { } n ? n : "classe ?";
            string head = CreateHead > 0 ? CreateHeads.FirstOrDefault(h => h.Value == CreateHead)?.Label ?? "tête" : "tête au hasard";
            string body = CreateBody > 0 ? CreateBodies.FirstOrDefault(b => b.Value == CreateBody)?.Label ?? "corps" : "corps au hasard";
            string name = string.IsNullOrWhiteSpace(CreateName) ? "nom proposé par le serveur" : CreateName.Trim();
            return $"{sex}  ·  {cls}  ·  {head}  ·  {body}  ·  {name}";
        }
    }

    private static string HeadLabel(string label) => label.Equals("retro", StringComparison.OrdinalIgnoreCase) ? "Rétro" : "Tête " + label;

    private static string BodyLabel(string label) => label switch { "base" => "Classique", "retro" => "Rétro", "new_age" => "New Age", _ => label };

    /// <summary>Classes proposées, avec l'icône du jeu (variante du sexe choisi, masculin par défaut) quand le bundle des icônes est lu.</summary>
    private void RefreshCreateBreeds()
    {
        int keep = CreateBreed;
        var wasLoading = CreateBreeds.Count;
        CreateBreeds.Clear();
        foreach (var o in CharacterBreedOptions(Math.Max(0, CreateSex))) CreateBreeds.Add(o);
        if (keep >= 0 && wasLoading > 0) CreateBreed = keep;
    }

    /// <summary>Têtes et corps proposés à la création pour la classe + le sexe choisis (HeadData / BodyData du jeu, BreedData.id = index + 1).</summary>
    private void RefreshCreateParts()
    {
        CreateHeads.Clear(); CreateBodies.Clear();
        CreateHead = CreateBody = -1;   // -1 = « Au hasard »
        if (CreateBreed < 0 || CreateSex < 0 || _launch.GameData is not { } db) return;
        CreateHeads.Add(new PickOption { Value = -1, Label = "Au hasard" });
        CreateBodies.Add(new PickOption { Value = -1, Label = "Au hasard" });
        try
        {
            int breedId = ClassIconProvider.BreedId(CreateBreed);   // index d'enum -> id de classe (Forgelance = 20)
            foreach (var h in db.CreationHeads(breedId, CreateSex)) CreateHeads.Add(new PickOption { Value = h.Id, Label = HeadLabel(h.Label), Icon = Stores.CosmeticIcons.Head(h.AssetId) });
            foreach (var b in db.CreationBodies(breedId, CreateSex)) CreateBodies.Add(new PickOption { Value = b.Id, Label = BodyLabel(b.Label), Icon = Stores.CosmeticIcons.Body(b.AssetId) });
            var defaults = db.BreedDefaultColors(breedId, CreateSex);
            for (int i = 0; i < CreateColors.Count; i++) CreateColors[i].DefaultRgb = i < defaults.Count ? defaults[i] : AccountsLogic.NoColor;
        }
        catch { /* données du jeu illisibles : listes vides, « Au hasard » refusé à la validation */ }
        // Les listes viennent d'être reconstruites : relance la sélection « Au hasard » pour qu'elle s'affiche (la valeur ne changerait pas sinon).
        CreateHead = CreateBody = 0;
        CreateHead = CreateBody = -1;
    }

    // Palette de couleurs : on sélectionne un emplacement (peau, cheveux, tenue 1-4) puis une couleur.
    public PaletteSwatch[] CreatePalette { get; } = BuildPalette();
    private static PaletteSwatch[] BuildPalette()
    {
        var l = new List<PaletteSwatch>();
        foreach (int g in new[] { 0x000000, 0x4A4A4A, 0x8A8A8A, 0xC8C8C8, 0xFFFFFF })
            l.Add(new PaletteSwatch { Rgb = g });
        foreach (double h in new double[] { 0, 18, 32, 48, 70, 110, 150, 175, 195, 215, 240, 265, 290, 320, 345 })
        {
            l.Add(new PaletteSwatch { Rgb = Hsv(h, 0.85, 0.95) });
            l.Add(new PaletteSwatch { Rgb = Hsv(h, 0.55, 0.80) });
            l.Add(new PaletteSwatch { Rgb = Hsv(h, 0.80, 0.50) });
        }
        return l.ToArray();
    }
    private static int Hsv(double h, double s, double v)
    {
        double c = v * s, x = c * (1 - Math.Abs(h / 60 % 2 - 1)), m = v - c;
        (double r, double g, double b) = h switch { < 60 => (c, x, 0d), < 120 => (x, c, 0d), < 180 => (0d, c, x), < 240 => (0d, x, c), < 300 => (x, 0d, c), _ => (c, 0d, x) };
        return ((int)Math.Round((r + m) * 255) << 16) | ((int)Math.Round((g + m) * 255) << 8) | (int)Math.Round((b + m) * 255);
    }

    [RelayCommand]
    private void SelectCreateColorSlot(CreationColor? c)
    {
        if (c is null) return;
        foreach (var x in CreateColors) x.IsActive = ReferenceEquals(x, c);
    }

    [RelayCommand]
    private void ApplyCreateColor(PaletteSwatch? p)
    {
        if (p is null) return;
        var slot = CreateColors.FirstOrDefault(x => x.IsActive) ?? CreateColors.FirstOrDefault();
        if (slot is null) return;
        slot.Rgb = p.Rgb;
        OnPropertyChanged(nameof(HasCreateColors));
        int i = CreateColors.IndexOf(slot);
        if (i + 1 < CreateColors.Count) SelectCreateColorSlot(CreateColors[i + 1]);   // enchaîne sur l'emplacement suivant
    }

    [RelayCommand]
    private void RandomizeCreateColors()
    {
        var c = AccountsLogic.RandomColors(Random.Shared);
        for (int i = 0; i < CreateColors.Count; i++) CreateColors[i].Rgb = c[i];
        OnPropertyChanged(nameof(HasCreateColors));
    }

    [RelayCommand]
    private void ResetCreateColors()
    {
        foreach (var c in CreateColors) c.Rgb = AccountsLogic.NoColor;
        OnPropertyChanged(nameof(HasCreateColors));
    }

    // Modal serveur → personnages : étape 2 = création (0 serveurs, 1 personnages, 2 création).
    public bool ServerPickOnCreate => ServerPickStep == 2;
    public bool ServerPickCharStep => ServerPickStep >= 1;
    partial void OnServerPickStepChanged(int value)
    {
        OnPropertyChanged(nameof(ServerPickOnCreate));
        OnPropertyChanged(nameof(ServerPickCharStep));
        OnPropertyChanged(nameof(ServerPickOnDelete));
    }

    [RelayCommand]
    private void NewSocketCharacter()
    {
        CreateName = "";   // vide = nom aléatoire proposé par le serveur
        CreateBreed = CreateHead = CreateBody = -1;
        CreateSex = 0;   // masculin par défaut : sans sexe, aucune tête ni corps n'est proposé
        ResetCreateColors();
        RefreshCreateBreeds();   // icônes de classes (bundle lu à l'ouverture du sélecteur)
        ServerPickStep = 2;
        ServerPickTitle = "Nouveau personnage";
        ServerPickSubtitle = _pickServerId > 0 ? PickServerLabel(_pickServerId) : "";
    }

    [RelayCommand] private void SocketCreateBack() => ShowPickStep(_pickServerId);

    [RelayCommand]
    private void ConfirmSocketCreation()
    {
        // Nom vide = nom proposé par le serveur (CharacterNameSuggestionRequest), comme le client.
        string? err = string.IsNullOrWhiteSpace(CreateName) ? null : AccountsLogic.ValidateName(CreateName);
        if (err is null && CreateBreed < 0) err = "choisis une classe";
        if (err is null && CreateSex < 0) err = "choisis un sexe";
        var db = _launch.GameData;
        if (err is null && !CreateAllowed) err = CreateCardHint;
        if (err is null && db is null) err = "données du jeu indisponibles (dofus.sqlite)";
        if (err is null && string.IsNullOrEmpty(ServerPickLogin)) err = "compte inconnu";
        if (err is not null) { Toasts.Show($"Création impossible : {err}.", success: false); return; }
        int breedId = ClassIconProvider.BreedId(CreateBreed);
        var heads = db!.CreationHeads(breedId, CreateSex);
        var bodies = db.CreationBodies(breedId, CreateSex);
        if (heads.Count == 0 || bodies.Count == 0) { Toasts.Show("Création impossible : têtes ou corps de cette classe introuvables dans les données du jeu.", success: false); return; }
        int head = CreateHead > 0 ? CreateHead : heads[Random.Shared.Next(heads.Count)].Id;
        int body = CreateBody > 0 ? CreateBody : bodies[Random.Shared.Next(bodies.Count)].Id;
        var defaults = db.BreedDefaultColors(breedId, CreateSex);
        var colors = new int[CharacterCreationEncoder.ColorCount];
        for (int i = 0; i < colors.Length; i++)
            colors[i] = i < CreateColors.Count && CreateColors[i].Rgb >= 0 ? CreateColors[i].Rgb : i < defaults.Count ? defaults[i] : 0;
        SocketCreationRegistry.Set(ServerPickLogin, new CharacterCreationSpec((CreateName ?? "").Trim(), CreateSex, breedId, head, body, colors, !CreateLeaveTutorial));
        // Même issue que le choix d'un personnage : le serveur est retenu, aucun personnage existant n'est sélectionné ; la session de jeu
        // crée le personnage (SocketGameClient) puis le sélectionne.
        _serverPickTcs?.TrySetResult((_pickServerId, null));
    }

    /// <summary>Pendant l'édition d'un compte : un bot en ligne possède la session (champs d'identité à ne pas modifier).</summary>
    public bool EditBotOwnsSession => EditVisible && _editRow is { Running: true };
    partial void OnEditVisibleChanged(bool value) => OnPropertyChanged(nameof(EditBotOwnsSession));
}
