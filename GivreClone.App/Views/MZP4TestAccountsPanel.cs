using System;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using GivreClone.App.Services;
using GivreClone.Engine.Recovery;

namespace GivreClone.App.Views;

/// <summary>
/// Explicitly opened, independent offline TEST profiles on the real Accounts page.
/// Never reads AccountManager, keydata, the installed GivreClone configuration, or game processes.
/// A candidate server/character is NOT an authenticated selection.
/// </summary>
public sealed class MZP4TestAccountsPanel : UserControl, IDisposable
{
    private readonly string _testRun;
    private static readonly IBrush Surface = Brush.Parse("#182330");
    private static readonly IBrush Muted = Brush.Parse("#A6B5C5");
    private static readonly IBrush Accent = Brush.Parse("#82B9DD");
    private readonly StackPanel _editor = new() { Spacing = 8, IsVisible = false };
    private readonly TextBlock _status = Label("Stockage non ouvert · aucun client connecté.", Muted);
    private readonly ListBox _profiles = new() { MinHeight = 125, MaxHeight = 220 };
    private readonly TextBox _alias = new() { MaxLength = 48, PlaceholderText = "Alias local sans identifiants" };
    private readonly TextBox _server = new() { MaxLength = 64, PlaceholderText = "Serveur candidat hors ligne" };
    private readonly TextBox _character = new() { MaxLength = 64, PlaceholderText = "Personnage candidat hors ligne" };
    private readonly TextBox _testPort = new()
    {
        MaxLength = 5, PlaceholderText = "Port relais TEST dédié (saisie manuelle)"
    };
    private readonly Button _open = ActionButton("Ouvrir l'espace de test");
    private readonly Button _preflight = ActionButton("Vérifier l'isolation du TEST (sans lancement)");
    private readonly Button _prepareRecovery = ActionButton("Préparer journal TEST privé (sans connexion)");
    private readonly Button _journalHealth = ActionButton("Initialiser et lire le journal TEST (hors ligne)");
    private readonly Button _interactiveLogin = ActionButton("Authentification Ankama TEST isolée (navigateur)");
    private readonly Button[] _commands;
    private MZP3TestAccountService? _store;
    private Guid? _selected;
    private bool _busy;
    private bool _disposed;
    private long _visibilityGeneration;
    private CancellationTokenSource? _loginCancellation;

    public bool IsTestStoreOpen => _store is not null && !_disposed;
    public int TestProfileCount => _store?.Profiles.Count ?? 0;
    public string OperatorStatus => _status.Text ?? string.Empty;

    public MZP4TestAccountsPanel() : this("givre-app-offline") { }

    // Test harness uses a unique run ID; production always uses the dedicated local run above.
    internal MZP4TestAccountsPanel(string testRun)
    {
        _testRun = testRun;
        _commands = new[]
        {
            ActionButton("Créer"), ActionButton("Sélectionner"), ActionButton("Renommer"),
            ActionButton("Définir candidat"), ActionButton("Effacer candidat"),
            ActionButton("Supprimer"), ActionButton("Relire le disque")
        };

        var heading = new TextBlock
        {
            Text = "Profils de TEST indépendants", FontSize = 17,
            FontWeight = FontWeight.SemiBold, Foreground = Accent
        };
        var intro = Label(
            "Espace volontaire, sans secrets. Distinct des comptes GivreClone et du launcher Ankama. " +
            "Aucune action ici ne lance ni ne connecte un bot.", Muted);
        _open.Click += async (_, _) => await OpenAsync();
        _profiles.SelectionChanged += (_, _) =>
        {
            if (_profiles.SelectedItem is not ProfileRow row || _store is null) return;
            _selected = row.Id;
            var p = _store.Profiles.FirstOrDefault(p => p.Id == row.Id);
            if (p is null) return;
            _alias.Text = p.Alias;
            _server.Text = p.ServerCandidate ?? "";
            _character.Text = p.CharacterCandidate ?? "";
        };
        _commands[0].Click += (_, _) => Commit(s =>
        {
            var p = s.CreateProfile(_alias.Text ?? "");
            s.SelectProfile(p.Id);
            _selected = p.Id;
        }, "Profil de test enregistré.");
        _commands[1].Click += (_, _) => Commit(s => s.SelectProfile(SelectedId()), "Sélection locale enregistrée.");
        _commands[2].Click += (_, _) => Commit(s => s.RenameProfile(SelectedId(), _alias.Text ?? ""), "Alias enregistré.");
        _commands[3].Click += (_, _) => Commit(s => s.SetOfflineCandidate(
            SelectedId(), _server.Text ?? "", _character.Text ?? "", explicitOfflineSelection: true),
            "Candidats sauvegardés · serveur/personnage NON authentifiés.");
        _commands[4].Click += (_, _) => Commit(s => s.ClearOfflineCandidate(SelectedId()), "Candidats effacés.");
        _commands[5].Click += (_, _) => Commit(s =>
        {
            s.RemoveProfile(SelectedId());
            _selected = null;
        }, "Profil local supprimé.");
        _commands[6].Click += (_, _) => Commit(s =>
        {
            s.Reload();
            _selected = s.SelectedProfileId;
        }, "État relu sur le disque.");

        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        foreach (var button in _commands) actions.Children.Add(button);
        _editor.Children.Add(Label("Profils sauvegardés uniquement dans GivreReplica/TestRuns", Muted));
        _editor.Children.Add(_profiles);
        _editor.Children.Add(_alias);
        _editor.Children.Add(_server);
        _editor.Children.Add(_character);
        _editor.Children.Add(actions);
        _editor.Children.Add(Label("Prévol du launcher : uniquement chemins, permissions et port; aucun client lancé.", Muted));
        _editor.Children.Add(_testPort);
        _editor.Children.Add(_prepareRecovery);
        _prepareRecovery.Click += (_, _) => PrepareRecovery();
        _editor.Children.Add(_journalHealth);
        _journalHealth.Click += (_, _) => InspectRecoveryHealth();
        _editor.Children.Add(_preflight);
        _preflight.Click += (_, _) => CheckLauncherPreflight();
        _editor.Children.Add(Label("Connexion officielle manuelle : navigateur neuf sous GivreReplica/TestRuns. " +
            "Aucun compte Givre existant consulté. La session WebView2 TEST peut garder des cookies privés; " +
            "les jetons renvoyés ne sont pas conservés.", Muted));
        _editor.Children.Add(_interactiveLogin);
        _interactiveLogin.Click += async (_, _) => await AuthenticateTestAsync();
        _editor.Children.Add(Label("Connexion, MITM et gameplay : indisponibles sans preuves externes indépendantes.", Muted));

        var stack = new StackPanel { Spacing = 12 };
        stack.Children.Add(heading);
        stack.Children.Add(intro);
        stack.Children.Add(_open);
        stack.Children.Add(_status);
        stack.Children.Add(_editor);
        Content = new Border
        {
            Padding = new Thickness(20), Background = Surface, CornerRadius = new CornerRadius(10),
            BorderBrush = Brush.Parse("#31465B"), BorderThickness = new Thickness(1), Child = stack
        };
        // ContentPresenter may cache a page instance across navigation. Release
        // the disk lease on detach, but allow the same panel to be reopened on
        // the next visit. A late OpenAsync completion belongs to the old visit.
        DetachedFromVisualTree += (_, _) => CloseForNavigation();
        RefreshEnabled();
    }

    private static TextBlock Label(string message, IBrush brush) => new()
    {
        Text = message, Foreground = brush, TextWrapping = TextWrapping.Wrap, FontSize = 12
    };

    private static Button ActionButton(string label) => new()
    {
        Content = label, Margin = new Thickness(0, 0, 8, 8), Padding = new Thickness(12, 8),
        Background = Brush.Parse("#24384A"), Foreground = Brush.Parse("#EDF4FA")
    };

    private async Task OpenAsync()
    {
        if (_busy || _disposed || _store is not null) return;
        _busy = true;
        var generation = _visibilityGeneration;
        RefreshEnabled();
        try
        {
            // The button is the required explicit user approval. Do not open storage on construction.
            var service = await Task.Run(() => MZP3TestAccountService.Open(_testRun, explicitOperatorApproval: true));
            if (_disposed || generation != _visibilityGeneration)
            {
                service.Dispose();
                return;
            }
            _store = service;
            _selected = service.SelectedProfileId;
            _editor.IsVisible = true;
            _status.Text = "Stockage de test ouvert · aucune session réelle admise.";
            RefreshProfiles();
        }
        catch (MZP3StoreException ex)
        {
            _status.Text = $"Stockage refusé ({ex.Failure}). Vérifier droits/dossier TestRuns; aucun compte réel touché.";
        }
        catch
        {
            _status.Text = "Stockage de test indisponible. Aucun compte réel touché.";
        }
        finally { _busy = false; if (!_disposed) RefreshEnabled(); }
    }

    private Guid SelectedId() => _profiles.SelectedItem is ProfileRow row ? row.Id : _selected ?? Guid.Empty;

    private void Commit(Action<MZP3TestAccountService> operation, string success)
    {
        if (_busy || _disposed || _store is null) return;
        _busy = true;
        RefreshEnabled();
        try
        {
            // Service operations and Save are paired; no uncertain mutation is silently discarded.
            operation(_store);
            _store.Save();
            _status.Text = success;
            RefreshProfiles();
        }
        catch (MZP3StoreException ex)
        {
            _status.Text = $"Opération refusée ({ex.Failure}). État non certifié; corriger avant reprise.";
        }
        catch
        {
            _status.Text = "Opération de test refusée; inspecter le stockage avant reprise.";
        }
        finally { _busy = false; RefreshEnabled(); }
    }

    private void RefreshProfiles()
    {
        if (_store is null) return;
        var rows = _store.Profiles.Select(p => new ProfileRow(p.Id, p.Alias,
            p.IsOfflineCandidate ? p.ServerCandidate : null,
            p.IsOfflineCandidate ? p.CharacterCandidate : null)).ToArray();
        _profiles.ItemsSource = rows;
        _profiles.SelectedItem = rows.FirstOrDefault(p => p.Id == _selected);
    }

    private void RefreshEnabled()
    {
        _open.IsEnabled = !_disposed && !_busy && _store is null;
        foreach (var cmd in _commands) cmd.IsEnabled = !_disposed && !_busy && _store is not null;
        _preflight.IsEnabled = !_disposed && !_busy && _store is not null;
        _prepareRecovery.IsEnabled = !_disposed && !_busy && _store is not null;
        _journalHealth.IsEnabled = !_disposed && !_busy && _store is not null;
        _interactiveLogin.IsEnabled = !_disposed && !_busy && _store is not null &&
                                      _testRun == "givre-app-offline" &&
                                      _store.SelectedProfileId is not null;
    }

    private async Task AuthenticateTestAsync()
    {
        if (_disposed || _busy || _store is null || _testRun != "givre-app-offline" ||
            _store.SelectedProfileId is null) return;
        _busy = true;
        var generation = _visibilityGeneration;
        using var cancellation = new CancellationTokenSource();
        _loginCancellation = cancellation;
        _status.Text = "Navigateur Ankama TEST : saisie manuelle sur le site officiel. Aucun client de jeu ouvert.";
        RefreshEnabled();
        try
        {
            var result = await MZP18IsolatedWebAuthRunner.RunInteractiveAsync(cancellation.Token);
            if (_disposed || generation != _visibilityGeneration) return;
            _status.Text = result.Reason + (result.State == MZP18IsolatedWebAuthRunner.State.Succeeded
                ? " Auth navigateur uniquement; identité serveur/personnage et ACK toujours non vérifiés."
                : " Aucune admission de jeu.");
        }
        catch (Exception)
        {
            // Never display raw exception payload: embedded browser messages
            // or authentication JSON may contain private authorization data.
            if (!_disposed && generation == _visibilityGeneration)
                _status.Text = "Navigateur TEST indisponible (erreur locale). Aucun jeton conservé ni client lancé.";
        }
        finally
        {
            if (ReferenceEquals(_loginCancellation, cancellation)) _loginCancellation = null;
            _busy = false;
            if (!_disposed && generation == _visibilityGeneration) RefreshEnabled();
        }
    }

    private void PrepareRecovery()
    {
        if (_disposed || _busy || _store is null) return;
        // Explicit local UI click authorizes a private empty directory only.
        // The service revalidates its own exclusive lease and saved candidate.
        var result = _store.PrepareIsolatedRecovery(explicitOperatorApproval: true);
        _status.Text = result.LocalDirectoryReady
            ? "Journal TEST privé prêt (répertoire seulement) · aucune connexion/ACK autorisée."
            : $"Préparation du journal refusée ({result.State}). Aucun lancement.";
    }

    private void InspectRecoveryHealth()
    {
        if (_disposed || _busy || _store is null) return;
        var status = _store.InitializeAndInspectTestJournal(explicitOperatorApproval: true);
        _status.Text = status.State == MZP5RecoveryHealthState.Unavailable
            ? "Journal TEST indisponible : préparer le dossier privé et contrôler le magasin. Aucun envoi."
            : $"Journal TEST réel : {status.State} · révision {status.Revision} · " +
              $"attentes {status.AwaitingExternalResponse}, revues {status.RequiresReview}. " +
              "H24/connexion/ACK serveur NON certifiés.";
    }

    private void CheckLauncherPreflight()
    {
        if (_disposed || _busy || _store is null) return;
        // The port is deliberately not guessed or reused from the original Givre.
        if (!int.TryParse(_testPort.Text, out var port))
        {
            _status.Text = "Prévol refusé : renseigner un port de TEST explicite; aucun lancement.";
            return;
        }
        if (_store.SelectedProfileId is not { } selected ||
            !_store.Profiles.Any(p => p.Id == selected && p.IsOfflineCandidate))
        {
            _status.Text = "Prévol refusé : sélectionner et sauvegarder d'abord un profil candidat hors ligne.";
            return;
        }

        // Read-only boundary: the preflight checks only the exact isolated test
        // folder metadata. It never opens accounts.json or the original app.
        var run = System.IO.Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData), "GivreReplica", "TestRuns", _testRun);
        var preflight = MZP4LauncherPreflight.Evaluate(new MZP4LauncherSelection(
            _testRun, port, System.IO.Path.Combine(run, "accounts.json"),
            System.IO.Path.Combine(run, "recovery"),
            MZP4LauncherPreflight.TargetClientVersion, ExplicitOperatorApproval: true));
        _status.Text = preflight.IsConfigurationCandidate
            ? "Prévol local conforme (CANDIDAT uniquement). Connexion, identité, port libre et ACK NON vérifiés; lancement désactivé."
            : $"Prévol local refusé ({preflight.Refusal}). Aucun client ni réseau contacté.";
    }

    private void CloseForNavigation()
    {
        ++_visibilityGeneration;
        _loginCancellation?.Cancel();
        _store?.Dispose();
        _store = null;
        _selected = null;
        _profiles.ItemsSource = null;
        _editor.IsVisible = false;
        _testPort.Text = "";
        _status.Text = "Espace de test fermé (navigation). Cliquer pour rouvrir, aucune session admise.";
        RefreshEnabled();
    }

    public void Dispose()
    {
        if (_disposed) return;
        _disposed = true;
        CloseForNavigation();
    }

    private sealed record ProfileRow(Guid Id, string Alias, string? Server, string? Character)
    {
        public override string ToString() => Alias +
            (Server is null ? " · aucune sélection attestée" : $" · {Server} / {Character} (candidats)");
    }
}
