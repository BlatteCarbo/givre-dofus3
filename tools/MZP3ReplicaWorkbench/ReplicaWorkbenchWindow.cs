using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using GivreClone.App.Controls;
using GivreClone.App.Services;
using GivreClone.App.Views;
using GivreClone.Protocol;

namespace MZP3ReplicaWorkbench;

/// <summary>
/// A real, independent Windows application for the parts of the replica that
/// can currently operate safely: isolated TEST profiles, offline server/character
/// candidates, durable reload, session capability and the original MZP0
/// observation panel. Other pages are navigable but plainly unavailable.
/// No game process, socket, injected DLL, authentication or password is used.
/// </summary>
public sealed class ReplicaWorkbenchWindow : Window
{
    private static readonly string[] Pages =
    [
        "Comptes", "Sécurité TEST", "Bots", "Équipe", "Proxys", "Adaptateurs",
        "Console", "Combat", "Carte", "Inventaire", "Banque", "HDV",
        "Lua", "Stats", "Planning", "Paramètres", "Paquets", "Infos", "Diagnostics"
    ];
    private static readonly IBrush CanvasBrush = Paint("#0B0F16");
    private static readonly IBrush Panel = Paint("#151B26");
    private static readonly IBrush Light = Paint("#E6EDF8");
    private static readonly IBrush Muted = Paint("#A6B4CA");
    private static readonly IBrush Accent = Paint("#8BAFFA");
    private static readonly IBrush Warning = Paint("#E9BB6D");
    private readonly StackPanel _nav = new() { Spacing = 3 };
    private readonly Border _main = new() { Background = CanvasBrush, Padding = new Thickness(24) };
    private readonly TextBlock _status = new()
    {
        Text = "Espace TEST non ouvert. Cliquer volontairement pour autoriser le stockage local.",
        FontSize = 12, Foreground = Warning, TextWrapping = TextWrapping.Wrap
    };
    private readonly Dictionary<string, Button> _navButtons = new();
    private MZP3TestAccountService? _accounts;
    private string _page = "Comptes";
    private bool _busy;
    private bool _closed;
    private ListBox? _profiles;
    private TextBox? _alias;
    private TextBox? _server;
    private TextBox? _character;
    private TextBox? _packetHex;
    private TextBlock? _packetResult;
    private Guid? _current;

    public ReplicaWorkbenchWindow()
    {
        Title = "Givre Replica · Atelier produit indépendant · TEST hors ligne";
        Width = 1320;
        Height = 850;
        MinWidth = 900;
        MinHeight = 600;
        Background = CanvasBrush;
        var root = new Grid { ColumnDefinitions = new ColumnDefinitions("224,*") };
        var rail = new DockPanel
        {
            Background = Paint("#111722"),
            Margin = new Thickness(0),
            LastChildFill = true
        };
        var railHeader = new StackPanel { Margin = new Thickness(18, 20, 16, 15), Spacing = 5 };
        railHeader.Children.Add(Txt("GIVRE / REPLICA", 17, Light, FontWeight.Bold));
        railHeader.Children.Add(Txt("Atelier de test indépendant", 11, Accent));
        DockPanel.SetDock(railHeader, Dock.Top);
        rail.Children.Add(railHeader);
        var footer = new StackPanel { Margin = new Thickness(14, 10, 14, 17), Spacing = 6 };
        footer.Children.Add(Txt("Client réel : NON CONNECTÉ", 11, Warning, FontWeight.SemiBold));
        footer.Children.Add(Txt("Aucun ACK ni gameplay attesté", 10, Muted));
        DockPanel.SetDock(footer, Dock.Bottom);
        rail.Children.Add(footer);
        var scroller = new ScrollViewer { Content = _nav };
        rail.Children.Add(scroller);
        foreach (var page in Pages)
        {
            var selectedPage = page;
            var b = new Button
            {
                Content = page,
                Height = 32,
                FontSize = 12,
                HorizontalContentAlignment = HorizontalAlignment.Left,
                Padding = new Thickness(18, 0),
                Margin = new Thickness(8, 0),
                Background = Paint("#111722"),
                Foreground = Muted,
                BorderThickness = new Thickness(0)
            };
            b.Click += (_, _) => Navigate(selectedPage);
            _navButtons.Add(page, b);
            _nav.Children.Add(b);
        }
        root.Children.Add(rail);
        Grid.SetColumn(_main, 1);
        root.Children.Add(_main);
        Content = root;
        // Never treat window creation as operator approval to open/create a
        // persistent store. The Accounts page presents an explicit button.
        Closed += (_, _) =>
        {
            _closed = true;
            _accounts?.Dispose();
            _accounts = null;
        };
        Navigate("Comptes");
    }

    private static IBrush Paint(string hex) => new SolidColorBrush(Color.Parse(hex));
    private static TextBlock Txt(string text, double size = 13, IBrush? color = null,
        FontWeight weight = FontWeight.Normal) =>
        new() { Text = text, FontSize = size, Foreground = color ?? Light,
            FontWeight = weight, TextWrapping = TextWrapping.Wrap };

    private static Border Section(Control child) => new()
    {
        Background = Panel, CornerRadius = new CornerRadius(12),
        Padding = new Thickness(20), BorderBrush = Paint("#263248"),
        BorderThickness = new Thickness(1), Child = child
    };

    private static Button ActionButton(string label, Action onClick)
    {
        var b = new Button
        {
            Content = label, Padding = new Thickness(14, 9), MinHeight = 36,
            Background = Paint("#253451"), Foreground = Light,
            BorderBrush = Paint("#344F7D"), BorderThickness = new Thickness(1),
            CornerRadius = new CornerRadius(6), Margin = new Thickness(0, 0, 6, 7)
        };
        b.Click += (_, _) => onClick();
        return b;
    }

    private async Task OpenTestStoreAsync()
    {
        if (_busy || _closed || _accounts is not null) return;
        _busy = true;
        try
        {
            // The ONLY storage opened by this executable is the dedicated
            // GivreReplica/TestRuns/workbench workspace, never %APPDATA%/GivreClone.
            var store = await Task.Run(() =>
                MZP3TestAccountService.Open("workbench", explicitOperatorApproval: true));
            if (_closed) { store.Dispose(); return; }
            _accounts = store;
            _current = store.SelectedProfileId;
            _status.Text = "Stockage de test disponible · uniquement des alias et choix candidats hors ligne.";
        }
        catch
        {
            _status.Text = "Stockage local refusé ou indisponible : aucune donnée écrite. Vérifier les droits Windows et l'intégrité du dossier TestRuns.";
        }
        finally
        {
            _busy = false;
            if (!_closed) Navigate(_page);
        }
    }

    private void Navigate(string page)
    {
        _page = page;
        foreach (var (key, button) in _navButtons)
        {
            button.Background = key == page ? Paint("#253450") : Paint("#111722");
            button.Foreground = key == page ? Light : Muted;
        }
        if (page == "Diagnostics")
        {
            _main.Padding = new Thickness(0);
            _main.Child = new MZP0OperatorPanel();
            return;
        }
        _main.Padding = new Thickness(24);
        var stack = new StackPanel { Spacing = 16, MaxWidth = 1030,
            HorizontalAlignment = HorizontalAlignment.Stretch };
        stack.Children.Add(Txt(page, 27, Light, FontWeight.SemiBold));
        stack.Children.Add(Txt(_status.Text ?? "", 12, Warning));
        if (page == "Comptes") BuildAccounts(stack);
        else if (page == "Sécurité TEST") BuildIsolatedTestSecurity(stack);
        else if (page == "Bots") BuildBots(stack);
        else if (page == "Paquets") BuildPackets(stack);
        else BuildUnavailable(stack, page);
        _main.Child = new ScrollViewer
        {
            Content = stack, HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled
        };
    }

    private void BuildAccounts(StackPanel stack)
    {
        stack.Children.Add(Txt("Profils locaux de TEST, sans mot de passe, sans token et sans connexion à Ankama.",
            13, Muted));
        if (_accounts is null)
        {
            stack.Children.Add(ActionButton("Ouvrir les profils locaux du Workbench (TEST)",
                () => _ = OpenTestStoreAsync()));
            stack.Children.Add(Section(Txt("Le magasin de test n'est pas prêt. Aucune opération sur les comptes du GivreClone installé n'est possible.", 13, Warning)));
            return;
        }
        var content = new Grid { ColumnDefinitions = new ColumnDefinitions("0.85*,1.15*"), ColumnSpacing = 18 };
        var left = new StackPanel { Spacing = 12 };
        left.Children.Add(Txt("Profils sauvegardés", 17, Light, FontWeight.SemiBold));
        _profiles = new ListBox { MinHeight = 235, MaxHeight = 370 };
        var rows = _accounts.Profiles.Select(x =>
            new ProfileRow(x.Id, x.Alias, x.ServerCandidate, x.CharacterCandidate)).ToArray();
        _profiles.ItemsSource = rows;
        _profiles.SelectedItem = rows.FirstOrDefault(x => x.Id == _current);
        _profiles.SelectionChanged += (_, _) =>
        {
            if (_profiles.SelectedItem is not ProfileRow selected) return;
            _current = selected.Id;
            var profile = _accounts?.Profiles.FirstOrDefault(x => x.Id == selected.Id);
            if (profile is null) return;
            if (_alias is not null) _alias.Text = profile.Alias;
            if (_server is not null) _server.Text = profile.ServerCandidate ?? "";
            if (_character is not null) _character.Text = profile.CharacterCandidate ?? "";
        };
        left.Children.Add(_profiles);
        left.Children.Add(Txt("Une sélection serveur/personnage reste CANDIDATE tant qu'aucun serveur n'a confirmé l'identité.", 11, Warning));
        var right = new StackPanel { Spacing = 11 };
        right.Children.Add(Txt("Créer ou modifier un profil", 17, Light, FontWeight.SemiBold));
        right.Children.Add(Txt("Alias non secret", 11, Muted));
        _alias = new TextBox { PlaceholderText = "Ex. test-local-01", MaxLength = 48 };
        right.Children.Add(_alias);
        right.Children.Add(Txt("Serveur candidat (hors ligne)", 11, Muted));
        _server = new TextBox { PlaceholderText = "Ex. serveur-test", MaxLength = 72 };
        right.Children.Add(_server);
        right.Children.Add(Txt("Personnage candidat (hors ligne)", 11, Muted));
        _character = new TextBox { PlaceholderText = "Ex. personnage-test", MaxLength = 72 };
        right.Children.Add(_character);
        var selectedProfile = _accounts.Profiles.FirstOrDefault(x => x.Id == _current);
        if (selectedProfile is not null)
        {
            _alias.Text = selectedProfile.Alias;
            _server.Text = selectedProfile.ServerCandidate ?? "";
            _character.Text = selectedProfile.CharacterCandidate ?? "";
        }
        var commands = new WrapPanel { Orientation = Orientation.Horizontal };
        commands.Children.Add(ActionButton("Créer", () =>
        {
            var alias = _alias?.Text ?? "";
            Mutate(s => { var p = s.CreateProfile(alias); s.SelectProfile(p.Id); _current = p.Id; },
                "Profil créé et enregistré.");
        }));
        commands.Children.Add(ActionButton("Sélectionner", () =>
        {
            var id = SelectedProfile();
            Mutate(s => s.SelectProfile(id), "Profil sélectionné.");
        }));
        commands.Children.Add(ActionButton("Renommer", () =>
        {
            var id = SelectedProfile();
            var alias = _alias?.Text ?? "";
            Mutate(s => s.RenameProfile(id, alias), "Alias enregistré.");
        }));
        commands.Children.Add(ActionButton("Définir candidat", () =>
        {
            var id = SelectedProfile();
            var server = _server?.Text ?? "";
            var character = _character?.Text ?? "";
            Mutate(s => s.SetOfflineCandidate(id, server, character,
                explicitOfflineSelection: true),
                "Serveur et personnage candidats sauvegardés (non vérifiés).");
        }));
        commands.Children.Add(ActionButton("Effacer candidat", () =>
            Mutate(s => s.ClearOfflineCandidate(SelectedProfile()),
                "Candidat effacé.")));
        commands.Children.Add(ActionButton("Supprimer", () =>
            Mutate(s => { s.RemoveProfile(SelectedProfile()); _current = null; },
                "Profil local supprimé.")));
        commands.Children.Add(ActionButton("Relire le disque", () =>
            Mutate(s => { s.Reload(); _current = s.SelectedProfileId; },
                "État persistant relu.")));
        commands.Children.Add(ActionButton("Enregistrer", () =>
            Mutate(s => s.Save(), "Enregistrement confirmé sur disque.")));
        right.Children.Add(commands);
        var leftCard = Section(left);
        content.Children.Add(leftCard);
        var rightCard = Section(right);
        Grid.SetColumn(rightCard, 1);
        content.Children.Add(rightCard);
        stack.Children.Add(content);
        stack.Children.Add(Section(Txt(
            "Connexion désactivée : il manque encore GivreCore, GivreLauncher.Core, l'origine serveur indépendante et le vérificateur de réponse positive. Aucun bouton ne démarre un client.", 12, Warning)));
    }

    private void BuildIsolatedTestSecurity(StackPanel stack)
    {
        stack.Children.Add(Txt(
            "Production C# MZP3 → MZP5 : espace de TEST séparé, journal durable " +
            "et contrôles explicites. Aucune action n'ouvre une connexion au jeu.",
            13, Muted));
        // Mount the same actual App UserControl as AccountsView.axaml, rather
        // than copying its workflow into this independently buildable shell.
        // The UI opens its own TEST store only when the operator clicks Open.
        stack.Children.Add(new MZP4TestAccountsPanel());
        stack.Children.Add(Section(Txt(
            "Transport MITM, authentification, personnage, carte et ACK serveur : " +
            "NON attestés. Un prévol réussi n'autorise aucun démarrage.",
            12, Warning)));
    }

    private void BuildBots(StackPanel stack)
    {
        var profiles = _accounts?.Profiles.Count ?? 0;
        stack.Children.Add(Section(new StackPanel
        {
            Spacing = 10,
            Children =
            {
                Txt("État d'exécution", 18, Light, FontWeight.SemiBold),
                Txt("Sessions authentifiées : 0 (aucune source externe attachée)", 13, Warning),
                Txt("Profils de test locaux : " + profiles, 13, Muted),
                Txt("Transport MITM / action / connexion : NON DISPONIBLES", 13, Warning),
                Txt("Démarrage et reprise désactivés tant que le moteur complet et une instance de test distincte ne sont pas prouvés.", 12, Muted)
            }
        }));
    }

    private void BuildPackets(StackPanel stack)
    {
        // Compile and embed the genuine MZP6 protocol panel from GivreClone.App.
        // Without external version-pinned catalogues its decode command is
        // intentionally disabled; no synthetic mapping is installed here.
        stack.Children.Add(new MZP6ProtocolInspectorPanel());
        stack.Children.Add(Txt(
            "Inspecteur de champs Protobuf NEUTRE et borné. Aucun mapping Dofus ni origine serveur certifiée.",
            13, Muted));
        var packet = new StackPanel { Spacing = 12 };
        packet.Children.Add(Txt("Octets hexadécimaux locaux", 15, Light, FontWeight.SemiBold));
        _packetHex = new TextBox
        {
            AcceptsReturn = true, MinHeight = 85, MaxLength = 262144,
            PlaceholderText = "Exemple : 08 2A (field #1, varint 42)"
        };
        packet.Children.Add(_packetHex);
        _packetResult = Txt("Aucune inspection effectuée.", 12, Muted);
        var actions = new WrapPanel();
        actions.Children.Add(ActionButton("Charger une fixture neutre", () =>
        {
            var bytes = new MZP3MessageBuilder().AddVarint(1, 42).Build().Encode();
            if (_packetHex is not null) _packetHex.Text = Convert.ToHexString(bytes);
            if (_packetResult is not null)
                _packetResult.Text = "Fixture produite localement ; elle ne provient pas de Dofus.";
        }));
        actions.Children.Add(ActionButton("Inspecter sans transmettre", () =>
        {
            var input = _packetHex?.Text ?? "";
            var compact = string.Concat(input.Where(c => !char.IsWhiteSpace(c)));
            if (compact.Length is < 2 or > 262144 || (compact.Length & 1) != 0)
            {
                if (_packetResult is not null)
                    _packetResult.Text = "Format hexadécimal incomplet ou trop grand.";
                return;
            }
            try
            {
                var wire = Convert.FromHexString(compact);
                var parsed = MZP3RawProtobuf.TryParse(wire);
                System.Security.Cryptography.CryptographicOperations.ZeroMemory(wire);
                if (_packetResult is null) return;
                _packetResult.Text = parsed.Ok
                    ? $"Structure neutre décodée : {parsed.Message!.Fields.Count} champ(s) · " +
                      string.Join(", ", parsed.Message.Fields.Take(25)
                          .Select(f => $"#{f.Number} {f.WireType} [{f.DataLength} o]")) +
                      (parsed.Message.Fields.Count > 25 ? " …" : "") +
                      ". Type métier, ACK et session : INCONNUS."
                    : $"Donnée refusée ({parsed.Error}). Aucun message promu.";
            }
            catch
            {
                if (_packetResult is not null)
                    _packetResult.Text = "Entrée refusée : octets non hexadécimaux.";
            }
        }));
        packet.Children.Add(actions);
        packet.Children.Add(_packetResult);
        stack.Children.Add(Section(packet));
        stack.Children.Add(Section(Txt(
            "Cet outil ne connaît ni les tags d'un message Dofus 3.7.10.10 ni les données de session. Le traitement reste en mémoire, sans socket ni envoi.", 12, Warning)));
    }

    private static void BuildUnavailable(StackPanel stack, string page)
    {
        stack.Children.Add(Section(new StackPanel
        {
            Spacing = 12,
            Children =
            {
                Txt("Module " + page + " · indisponible dans cet exécutable", 19, Warning, FontWeight.SemiBold),
                Txt("Le code source métier GivreShareWork existe mais sa chaîne runtime complète dépend encore des projets authentiques GivreCore et GivreLauncher.Core.", 13, Muted),
                Txt("Ce panneau ne simule ni connexion, ni carte, ni combat, ni inventaire, ni ACK. Les contrôles de gameplay restent désactivés.", 12, Muted),
                Txt("Les sources des vues originales sont conservées dans GivreClone.App/Views et seront réutilisées lors du build complet.", 12, Accent)
            }
        }));
    }

    private Guid SelectedProfile() => _profiles?.SelectedItem is ProfileRow row
        ? row.Id : _current ?? Guid.Empty;

    private async void Mutate(Action<MZP3TestAccountService> operation, string message)
    {
        if (_busy || _accounts is null) return;
        _busy = true;
        try
        {
            var store = _accounts;
            await Task.Run(() => { operation(store); store.Save(); });
            _status.Text = message;
        }
        catch
        {
            _status.Text = "Action refusée. Vérifiez la sélection, les champs et les droits du dossier TestRuns ; aucun client de jeu n'a été contacté.";
        }
        finally
        {
            _busy = false;
            if (!_closed) Navigate(_page);
        }
    }

    private sealed record ProfileRow(Guid Id, string Alias,
        string? Server, string? Character)
    {
        public override string ToString() =>
            Alias + (string.IsNullOrWhiteSpace(Server) ? "  ·  non sélectionné"
                : "  ·  " + Server + " / " + Character + " (candidat)");
    }
}
