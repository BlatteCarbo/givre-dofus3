using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using GivreClone.Protocol;

namespace GivreClone.App.Views;

/// <summary>
/// Standalone operator-initiated, in-memory UNFRAMED GameMessage inspector.
/// An App integrator must explicitly inject a SHA-pinned MZP4 catalogue and a
/// matching declared MZP5 scope. This control never acquires a catalogue,
/// launches a client, loads a file, listens to a socket, or publishes raw data.
/// </summary>
public sealed class MZP6ProtocolInspectorPanel : UserControl
{
    private static readonly IBrush BackgroundSurface = Brush.Parse("#131D2A");
    private static readonly IBrush ElevatedSurface = Brush.Parse("#1D293A");
    private static readonly IBrush Stroke = Brush.Parse("#34485E");
    private static readonly IBrush Primary = Brush.Parse("#F0F5FC");
    private static readonly IBrush Secondary = Brush.Parse("#A7B9CE");
    private static readonly IBrush Accent = Brush.Parse("#9CCBFA");
    private static readonly IBrush Warning = Brush.Parse("#F3CA8E");
    private static readonly IBrush Success = Brush.Parse("#9BDCC5");

    private readonly TextBox _hex = new()
    {
        Name = "MZP6HexInput", AcceptsReturn = true, TextWrapping = TextWrapping.Wrap,
        MinHeight = 92, MaxHeight = 170, MaxLength = 262144,
        PlaceholderText = "Coller un seul GameMessage NON préfixé, en hexadécimal…",
        FontFamily = new FontFamily("Consolas"),
        FontSize = 12, Background = Brush.Parse("#101722"), Foreground = Primary
    };
    private readonly TextBox _sequence = new()
    {
        Name = "MZP6SequenceInput", MaxLength = 19,
        PlaceholderText = "Séquence déclarée, dès 1",
        Width = 205, Background = Brush.Parse("#101722"), Foreground = Primary
    };
    private readonly ComboBox _direction = new()
    {
        Name = "MZP6Direction", Width = 240,
        Background = ElevatedSurface, Foreground = Primary,
        ItemsSource = new[]
        {
            "Direction non choisie",
            "Client → serveur (déclaré)",
            "Serveur → client (déclaré)"
        },
        SelectedIndex = 0
    };
    private readonly Button _inspect = MakeButton("MZP6Inspect", "Analyser en mémoire", true);
    private readonly Button _clear = MakeButton("MZP6Clear", "Effacer les octets", false);
    private readonly TextBlock _readiness = Text("Aucun catalogue vérifié. Inspection désactivée.", Warning, 13);
    private readonly TextBlock _result = Text(
        "Aucun message analysé. Origine, type, version réelle, authentification et ACK : INCONNUS.",
        Secondary, 12);
    private readonly TextBlock _packetType = Text("Branche : —", Primary, 13);
    private readonly TextBlock _fieldCount = Text("Champs : —", Primary, 13);
    private readonly TextBlock _wireSummary = Text("Wire : —", Primary, 13);
    private readonly TextBlock _sessionState = Text("Confiance : aucune", Warning, 13);
    private readonly StackPanel _fields = new() { Spacing = 6 };
    private readonly StackPanel _history = new() { Spacing = 5 };
    // Avoid reopening the same observed scope to evade MZP5 sequence/quarantine
    // state. A genuinely new source generation is required for a fresh reader.
    private readonly HashSet<MZP5SourceScope> _observedScopes = new();
    private MZP5InboundReader? _reader;
    private bool _needsNewScope;

    /// <summary>A categoric result only; never embeds source labels or field values.</summary>
    public string ResultText => _result.Text ?? string.Empty;
    public string ReadinessText => _readiness.Text ?? string.Empty;
    public bool IsOfflineCandidate => _reader is not null && !_needsNewScope;
    public int DisplayedFieldCount => _fields.Children.Count;
    public int HistoryCount => _history.Children.Count;
    public int InputCharacterCount => _hex.Text?.Length ?? 0;
    public bool HasAuthenticSource => false;
    public bool ServerAckCertified => false;
    public bool CanConnect => false;

    public MZP6ProtocolInspectorPanel()
    {
        var header = new StackPanel { Spacing = 6 };
        header.Children.Add(Text("MZP6  /  PROTOCOL INSPECTOR", Accent, 12, bold: true));
        header.Children.Add(Text("Inspection privée des trames neutres", Primary, 21, bold: true));
        header.Children.Add(Text(
            "Une seule trame GameMessage non préfixée, collée volontairement. " +
            "Aucun service connecté, aucun fichier de comptes, aucune lecture du client.",
            Secondary, 12));

        var metadata = new StackPanel { Spacing = 6 };
        metadata.Children.Add(Text("01  •  CATALOGUE ET SCOPE", Accent, 12, bold: true));
        metadata.Children.Add(_readiness);
        metadata.Children.Add(Text(
            "Le catalogue SHA-épinglé et les métadonnées de source doivent être fournis " +
            "par un appelant externe autorisé. Même validés en forme, ils ne prouvent pas la provenance.",
            Secondary, 11));

        var editor = new StackPanel { Spacing = 10 };
        editor.Children.Add(Text("02  •  OBSERVATION MANUELLE", Accent, 12, bold: true));
        var entryRow = new WrapPanel { Orientation = Orientation.Horizontal };
        entryRow.Children.Add(new StackPanel
        {
            Margin = new Thickness(0, 0, 12, 4), Spacing = 4,
            Children = { Text("Direction déclarée", Secondary, 11), _direction }
        });
        entryRow.Children.Add(new StackPanel
        {
            Margin = new Thickness(0, 0, 8, 4), Spacing = 4,
            Children = { Text("Numéro de séquence (continu)", Secondary, 11), _sequence }
        });
        editor.Children.Add(entryRow);
        editor.Children.Add(Text("Octets hexadécimaux — jamais affichés dans les résultats", Secondary, 11));
        editor.Children.Add(_hex);
        var actions = new WrapPanel { Orientation = Orientation.Horizontal };
        _inspect.Margin = new Thickness(0, 0, 10, 5);
        _clear.Margin = new Thickness(0, 0, 10, 5);
        actions.Children.Add(_inspect);
        actions.Children.Add(_clear);
        editor.Children.Add(actions);

        var overview = new StackPanel { Spacing = 8 };
        overview.Children.Add(Text("03  •  SYNTHÈSE NON SENSIBLE", Accent, 12, bold: true));
        var cards = new WrapPanel { Orientation = Orientation.Horizontal };
        cards.Children.Add(Metric(_packetType));
        cards.Children.Add(Metric(_fieldCount));
        cards.Children.Add(Metric(_wireSummary));
        cards.Children.Add(Metric(_sessionState));
        overview.Children.Add(cards);
        overview.Children.Add(_result);
        overview.Children.Add(Text(
            "Les valeurs et l'UID sont toujours masqués. Champs affichés : numéros, " +
            "wire types et longueurs uniquement. Un message Response n'est pas un ACK.",
            Secondary, 11));
        overview.Children.Add(_fields);

        var audit = new StackPanel { Spacing = 7 };
        audit.Children.Add(Text("04  •  HISTORIQUE LOCAL EXPURGÉ", Accent, 12, bold: true));
        audit.Children.Add(Text(
            "Dernières tentatives uniquement. Aucun code de message, octet, compte, " +
            "PID, identifiant de source ou UID conservé dans cet historique.",
            Secondary, 11));
        audit.Children.Add(_history);

        _inspect.Click += (_, _) => Inspect();
        _clear.Click += (_, _) => ClearInput();
        var sections = new StackPanel { Spacing = 14 };
        sections.Children.Add(header);
        sections.Children.Add(Section(metadata));
        sections.Children.Add(Section(editor));
        sections.Children.Add(Section(overview));
        sections.Children.Add(Section(audit));
        Content = new ScrollViewer
        {
            HorizontalScrollBarVisibility = Avalonia.Controls.Primitives.ScrollBarVisibility.Disabled,
            Content = new Border
            {
                Padding = new Thickness(19), Background = BackgroundSurface,
                CornerRadius = new CornerRadius(12),
                BorderThickness = new Thickness(1), BorderBrush = Stroke,
                Child = sections
            }
        };
        RefreshAction();
    }

    /// <summary>
    /// Explicit DI only. Failed or incomplete injection invalidates any previous
    /// reader; this panel never invents a schema or reuses a former scope.
    /// </summary>
    public bool Configure(MZP4ProtocolAdapter? catalogue, MZP5SourceScope? scope)
    {
        _reader = null;
        _scopeForCurrentReader = null;
        _needsNewScope = false;
        ScrubInput();
        ResetReport();
        _history.Children.Clear();

        if (scope is not null && _observedScopes.Contains(scope))
        {
            _needsNewScope = true;
            _readiness.Text = "Génération déjà consommée. Nouveau scope explicite requis.";
            _readiness.Foreground = Warning;
            _result.Text = "Réouverture refusée : quarantaine et séquences jamais réinitialisées silencieusement.";
            RefreshAction();
            return false;
        }
        var opened = MZP5InboundReader.Open(catalogue, scope);
        if (!opened.IsOfflineCandidate || opened.Reader is null)
        {
            _readiness.Text = "Catalogue ou scope refusé (" + SafeOpenReason(opened.Reason) +
                              "). Aucun type/version/source inféré.";
            _readiness.Foreground = Warning;
            _result.Text = "Inspection bloquée. Les métadonnées déclarées ne sont pas admises.";
            RefreshAction();
            return false;
        }
        _reader = opened.Reader;
        _scopeForCurrentReader = scope;
        _readiness.Text = "Version candidate 3.7.10.10 · 2 empreintes cohérentes · " +
                          "scope déclaré présent · AUCUNE attestation runtime.";
        _readiness.Foreground = Success;
        _result.Text = "Catalogue fourni explicitement. Choisir direction et séquence; " +
                       "aucune origine ni connexion vérifiée.";
        RefreshAction();
        return true;
    }

    private void Inspect()
    {
        byte[]? bytes = null;
        try
        {
            ResetReport();
            if (_reader is null || _needsNewScope)
            {
                DisplayFailure("SOURCE_OR_CATALOGUE_UNAVAILABLE", lockReader: false);
                return;
            }
            var direction = _direction.SelectedIndex switch
            {
                1 => MZP5Direction.ClientToServer,
                2 => MZP5Direction.ServerToClient,
                _ => MZP5Direction.Unknown
            };
            if (direction == MZP5Direction.Unknown)
            {
                DisplayFailure("DIRECTION_NOT_EXPLICIT", lockReader: false);
                return;
            }
            if (!long.TryParse(_sequence.Text, System.Globalization.NumberStyles.None,
                    System.Globalization.CultureInfo.InvariantCulture, out long sequence) ||
                sequence < 1)
            {
                DisplayFailure("INVALID_SEQUENCE", lockReader: false);
                return;
            }
            // UI input is bounded BEFORE the copy/hex decode. Only ASCII hex and
            // whitespace are tolerated. Reject 0x prefixes, separators, odd nibbles.
            var source = _hex.Text;
            if (string.IsNullOrWhiteSpace(source) || source.Length > 262144)
            {
                DisplayFailure("EMPTY_OR_EXCESSIVE_BYTES", lockReader: false);
                return;
            }
            var compact = new char[source.Length];
            try
            {
                int length = 0;
                foreach (char c in source)
                {
                    if (c is ' ' or '\r' or '\n' or '\t') continue;
                    if (!IsAsciiHex(c))
                    {
                        DisplayFailure("HEX_SYNTAX_INVALID", lockReader: false);
                        return;
                    }
                    compact[length++] = c;
                }
                if (length < 2 || (length & 1) != 0 || length > 262144)
                {
                    DisplayFailure("HEX_LENGTH_INVALID", lockReader: false);
                    return;
                }
                bytes = Convert.FromHexString(compact.AsSpan(0, length));
            }
            finally { Array.Clear(compact); }
            // Scope is captured at successful Configure and never reconstructed
            // from user-edited identifiers or inferred from these bytes.
            var observation = new MZP5InboundObservation(_scopeForCurrentReader!, direction, sequence);
            _observedScopes.Add(_scopeForCurrentReader!);
            var parsed = _reader.ReadUnframed(observation, bytes);
            if (!parsed.Parsed || parsed.Packet is null)
            {
                // A MZP5 wire/sequence error quarantines its own direction.
                DisplayFailure("WIRE_OR_SEQUENCE_" + parsed.Status, lockReader: false);
                return;
            }
            if (!parsed.Packet.KnownAlias)
            {
                // MZP5 allows neutral unknown aliases; the operator inspector
                // deliberately FAILS CLOSED for unrecognized message types.
                DisplayFailure("UNKNOWN_MESSAGE_TYPE", lockReader: true);
                return;
            }
            if (parsed.Packet.Branch == MZP5Branch.Request &&
                direction != MZP5Direction.ClientToServer ||
                parsed.Packet.Branch != MZP5Branch.Request &&
                direction != MZP5Direction.ServerToClient)
            {
                DisplayFailure("BRANCH_DIRECTION_MISMATCH", lockReader: true);
                return;
            }
            DisplaySuccess(parsed.Packet);
        }
        catch (FormatException)
        {
            DisplayFailure("HEX_SYNTAX_INVALID", lockReader: true);
        }
        catch
        {
            // Fail-closed: no exception details or potential raw data in UI.
            DisplayFailure("INSPECTION_REJECTED", lockReader: true);
        }
        finally
        {
            if (bytes is not null) CryptographicOperations.ZeroMemory(bytes);
            ScrubInput();
            RefreshAction();
        }
    }

    // Scope is only assigned by Configure on a successful MZP5.Open.
    private MZP5SourceScope? _scopeForCurrentReader;

    private void DisplaySuccess(MZP5DecodedPacket packet)
    {
        _packetType.Text = "Branche : " + packet.Branch;
        _fieldCount.Text = "Champs : " + packet.Payload.Fields.Count;
        int varints = packet.Payload.Fields.Count(f => f.WireType == MZP3WireType.Varint);
        int lengths = packet.Payload.Fields.Count(f => f.WireType == MZP3WireType.LengthDelimited);
        int fixeds = packet.Payload.Fields.Count - varints - lengths;
        _wireSummary.Text = "Wire : V" + varints + " / L" + lengths + " / F" + fixeds;
        _sessionState.Text = "Confiance : structure seule";
        _result.Text = "Forme parsée · type connu dans catalogue fourni · " +
                       "direction et séquence déclarées, non authentifiées. " +
                       "Valeurs, UID, origine réelle et ACK : NON CERTIFIÉS / MASQUÉS.";
        foreach (var field in packet.Payload.Fields.Take(24))
            _fields.Children.Add(new TextBlock
            {
                Text = "#" + field.Number + "    " + field.WireType +
                       "    " + field.DataLength + " octets de contenu (masqués)",
                Foreground = Secondary, FontSize = 11, TextWrapping = TextWrapping.Wrap
            });
        if (packet.Payload.Fields.Count > 24)
            _fields.Children.Add(Text("Autres champs masqués (limite d'affichage 24).", Secondary, 11));
        AppendHistory("ANALYSE_STRUCTURELLE", true);
    }

    private void DisplayFailure(string categoricalReason, bool lockReader)
    {
        if (lockReader)
        {
            _reader = null;
            _scopeForCurrentReader = null;
            _needsNewScope = true;
            _readiness.Text = "Nouvelle configuration explicite requise. Session de parsing bloquée.";
            _readiness.Foreground = Warning;
        }
        _result.Text = "Inspection refusée (" + categoricalReason +
                       "). Aucun type, octet, valeur, identifiant ni ACK publié.";
        _sessionState.Text = "Confiance : aucune";
        AppendHistory(categoricalReason, false);
    }

    private void AppendHistory(string category, bool accepted)
    {
        if (_history.Children.Count == 12)
            _history.Children.RemoveAt(0);
        _history.Children.Add(Text(
            (accepted ? "● CANDIDAT · " : "○ REFUS · ") + category +
            " · source/ACK non vérifiés", accepted ? Success : Warning, 11));
    }

    private void ClearInput()
    {
        ScrubInput();
        _result.Text = "Octets effacés de la saisie. Aucune transmission.";
    }

    private void ScrubInput() => _hex.Text = string.Empty;

    private void ResetReport()
    {
        _fields.Children.Clear();
        _packetType.Text = "Branche : —";
        _fieldCount.Text = "Champs : —";
        _wireSummary.Text = "Wire : —";
        _sessionState.Text = "Confiance : aucune";
    }

    private void RefreshAction() => _inspect.IsEnabled = _reader is not null && !_needsNewScope;

    private static bool IsAsciiHex(char c) =>
        c is >= '0' and <= '9' or >= 'A' and <= 'F' or >= 'a' and <= 'f';

    private static string SafeOpenReason(string reason) => reason switch
    {
        "SCOPE_MISSING_OR_INVALID" => "SCOPE_MISSING_OR_INVALID",
        "VERSION_OR_CATALOGUE_MISMATCH" => "VERSION_OR_CATALOGUE_MISMATCH",
        "INVALID_WIRE_LIMITS" => "INVALID_WIRE_LIMITS",
        "INBOUND_SLOTS_MISSING" => "INBOUND_SLOTS_MISSING",
        "INBOUND_SLOTS_COLLIDE" => "INBOUND_SLOTS_COLLIDE",
        _ => "NOT_ADMITTED"
    };

    private static TextBlock Text(string label, IBrush color, double size, bool bold = false) =>
        new()
        {
            Text = label, Foreground = color, FontSize = size,
            FontWeight = bold ? FontWeight.SemiBold : FontWeight.Normal,
            TextWrapping = TextWrapping.Wrap
        };

    private static Button MakeButton(string name, string label, bool primary) => new()
    {
        Name = name, Content = label, MinHeight = 37, Padding = new Thickness(15, 8),
        Background = primary ? Brush.Parse("#315F83") : ElevatedSurface,
        Foreground = Primary, BorderBrush = Stroke, BorderThickness = new Thickness(1)
    };

    private static Border Section(Control content) => new()
    {
        Padding = new Thickness(15), CornerRadius = new CornerRadius(9),
        BorderBrush = Stroke, BorderThickness = new Thickness(1),
        Background = ElevatedSurface, Child = content
    };

    private static Border Metric(Control content) => new()
    {
        MinWidth = 168, Padding = new Thickness(11),
        Margin = new Thickness(0, 0, 8, 8),
        CornerRadius = new CornerRadius(7),
        BorderBrush = Stroke, BorderThickness = new Thickness(1),
        Background = BackgroundSurface, Child = content
    };
}
