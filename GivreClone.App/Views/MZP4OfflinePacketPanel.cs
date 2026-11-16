using System;
using System.Linq;
using System.Security.Cryptography;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using GivreClone.Protocol;

namespace GivreClone.App.Views;

/// <summary>
/// In-process, user-supplied OFFLINE hex inspector embedded in the real Bots
/// packet page. No connection to the packet log, proxy, game client or launcher.
/// Inspect structure only; never echo protobuf values/bytes in output.
/// </summary>
public sealed class MZP4OfflinePacketPanel : UserControl
{
    private readonly TextBox _hex = new()
    {
        MinHeight = 72,
        MaxLength = 262144,
        AcceptsReturn = true,
        TextWrapping = TextWrapping.Wrap,
        PlaceholderText = "Octets Protobuf bruts en hex (pas une capture automatique)"
    };
    private readonly TextBlock _output = new()
    {
        Text = "Aucune inspection. Type de message, origine, ACK et session : INCONNUS.",
        TextWrapping = TextWrapping.Wrap,
        Foreground = Brush.Parse("#9BB2C5"),
        FontSize = 12
    };
    private readonly Button _inspect = new() { Content = "Analyser localement", MinHeight = 32 };
    private readonly Button _clear = new() { Content = "Effacer la saisie", MinHeight = 32 };

    public string ResultText => _output.Text ?? string.Empty;
    public bool HasInput => !string.IsNullOrEmpty(_hex.Text);

    public MZP4OfflinePacketPanel()
    {
        var title = new TextBlock
        {
            Text = "Inspecteur Protobuf hors ligne",
            FontWeight = FontWeight.SemiBold, FontSize = 15,
            Foreground = Brush.Parse("#89C2E3")
        };
        var hint = new TextBlock
        {
            Text = "Saisie locale uniquement · champs numérotés, types wire et tailles. " +
                   "Ne collez pas de mots de passe ni de jetons. Aucun envoi, ACK ou décodage métier certifié.",
            Foreground = Brush.Parse("#B4C1CE"),
            FontSize = 11, TextWrapping = TextWrapping.Wrap
        };
        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 10 };
        buttons.Children.Add(_inspect);
        buttons.Children.Add(_clear);
        _inspect.Click += (_, _) => Inspect();
        _clear.Click += (_, _) =>
        {
            _hex.Text = "";
            _output.Text = "Saisie effacée. Aucun octet transmis.";
        };
        var stack = new StackPanel { Spacing = 9 };
        stack.Children.Add(title);
        stack.Children.Add(hint);
        stack.Children.Add(_hex);
        stack.Children.Add(buttons);
        stack.Children.Add(_output);
        Content = new Border
        {
            Padding = new Thickness(15),
            CornerRadius = new CornerRadius(9),
            BorderBrush = Brush.Parse("#2B4057"),
            BorderThickness = new Thickness(1),
            Background = Brush.Parse("#182431"),
            Child = stack
        };
    }

    public void SetInputForTest(string? hex) => _hex.Text = hex;

    private void Inspect()
    {
        byte[]? bytes = null;
        try
        {
            var source = _hex.Text ?? "";
            // Bound characters before creating an additional string.
            if (source.Length is < 2 or > 262144)
            {
                _output.Text = "Saisie vide ou trop grande; traitement refusé.";
                return;
            }
            var compact = new string(source.Where(static c => !char.IsWhiteSpace(c)).ToArray());
            if (compact.Length < 2 || (compact.Length & 1) != 0 || compact.Length > 262144)
            {
                _output.Text = "Longueur hexadécimale invalide. Aucun résultat publié.";
                return;
            }
            bytes = Convert.FromHexString(compact);
            var parsed = MZP3RawProtobuf.TryParse(bytes);
            if (!parsed.Ok)
            {
                _output.Text = $"Structure rejetée ({parsed.Error}). Aucun type ni session inféré.";
                return;
            }
            _output.Text = $"Structure neutre : {parsed.Message!.Fields.Count} champs · " +
                string.Join(", ", parsed.Message.Fields.Take(24)
                    .Select(f => $"#{f.Number} {f.WireType} ({f.DataLength} o)")) +
                (parsed.Message.Fields.Count > 24 ? " …" : "") +
                ". Valeurs masquées · origine/ACK/état de jeu INCONNUS.";
        }
        catch (FormatException)
        {
            _output.Text = "Entrée hexadécimale invalide; aucun message décodé.";
        }
        catch
        {
            _output.Text = "Inspection refusée; données non transmises.";
        }
        finally
        {
            if (bytes is not null) CryptographicOperations.ZeroMemory(bytes);
        }
    }
}
