using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using GivreClone.App.Services;

namespace GivreClone.App.Views;

/// <summary>
/// Real Accounts-page version/mapping readiness widget. Reads only already
/// admitted structural status in memory; never opens account files, launches a
/// client, fetches a mapping, injects data, or claims an authenticated session.
/// </summary>
public sealed class MZP10MappingVerificationPanel : UserControl
{
    private readonly TextBlock _summary = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly TextBlock _version = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly TextBlock _diagnostics = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly TextBlock _sessionGate = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private MZP11CytrusVersionProbe.Result? _installed;

    public string StatusText => _summary.Text ?? string.Empty;
    public bool HasGameProtocolProof => false;
    public bool CanLaunchDofus => false;

    public MZP10MappingVerificationPanel()
    {
        var brush = Brush.Parse("#A9C3DC");
        _summary.Foreground = brush;
        _version.Foreground = brush;
        _diagnostics.Foreground = brush;
        _sessionGate.Foreground = brush;
        var button = new Button
        {
            Content = "Revérifier version et mapping (sans réseau)",
            Padding = new Thickness(12, 8), HorizontalAlignment = HorizontalAlignment.Left,
            Background = Brush.Parse("#294258"), Foreground = Brush.Parse("#F2F7FC")
        };
        button.Click += (_, _) =>
        {
            // Explicit read-only operator verification: installed *game*
            // manifest only, never launcher accounts or an active process.
            var exe = GivreLauncher.Core.Launch.GameLauncher.ResolveDofusPath(AppConfig.Current.DofusExePath);
            _installed = MZP11CytrusVersionProbe.InspectInstalledDofus(exe);
            Refresh();
        };
        Content = new Border
        {
            CornerRadius = new CornerRadius(12), Padding = new Thickness(16),
            Background = Brush.Parse("#172736"), BorderBrush = Brush.Parse("#344E65"),
            BorderThickness = new Thickness(1),
            Child = new StackPanel
            {
                Spacing = 8,
                Children =
                {
                    new TextBlock { Text = "Compatibilité des mappings Dofus 3.7",
                        FontSize = 16, FontWeight = FontWeight.SemiBold,
                        Foreground = Brush.Parse("#D7EAF7") },
                    _version, _summary, _diagnostics, _sessionGate, button,
                    new TextBlock { Text = "Version Unity ≠ protocole Dofus. Un test de trames indépendantes reste obligatoire avant gameplay.",
                        FontSize = 11, Foreground = brush, TextWrapping = TextWrapping.Wrap }
                }
            }
        };
        Refresh();
    }

    public void Refresh()
    {
        _version.Text = _installed is { HasLocalVersionEvidence: true }
            ? $"Version du client installé : {_installed.ProtocolVersion} · source {_installed.Train} (métadonnées locales, sans ACK)."
            : "Version du client installé : " +
              (_installed?.Diagnostic ?? "non vérifiée (cliquer sur Revérifier)");
        var decision = MappingCatalog.LastVersionDecision;
        _summary.Text = decision is null
            ? "Mapping non comparé : aucune décision de version pour cette session."
            : $"Contrôle de version : {decision.Status} — {decision.SafeReason}";
        if (LocalEdition.Enabled && MappingCatalog.IsReady)
            _summary.Text = "Édition locale : mapping historique de data/ ; version source déclarée dans bundle-build.json. Compatibilité gameplay non certifiée.";
        _diagnostics.Text = MappingCatalog.IsReady
            ? (LocalEdition.Enabled ? "Catalogue local publié en mémoire ; ACK, identité du client et gameplay non certifiés."
                                    : "Catalogue streamé présent en mémoire ; ACK, identité du client et gameplay non certifiés.")
            : "Catalogue non publié : le lancement piloté reste indisponible.";
        var binding = MappingCatalog.SessionVersionBinding;
        if (_installed is not null)
        {
            var state = MZP12SocketVersionBinding.Evaluate(binding, _installed);
            if (state.IsUnverifiedCrossPatchReview)
                _summary.Text = "Socket : essai admis en revue inter-patch (schéma Protobuf identique). Le mapping conserve sa version source ; scripts et déplacements désactivés jusqu'à preuve wire/ACK.";
            _sessionGate.Text = "Protection Socket : " + state.SafeMessage +
                (state.Reason == MZP12SocketVersionBinding.Refusal.MappingBuildMismatch
                    ? " · Demander le bundle pour la version installée ou comparer les artefacts réels ; ne pas renommer l'ancien bundle."
                    : "");
        }
        else
            _sessionGate.Text = "Protection Socket : revérifier le manifeste avant toute connexion.";
    }
}
