using System;
using System.Globalization;
using Avalonia.Data.Converters;
using Avalonia.Media;
using Avalonia.Media.Immutable;

namespace GivreClone.App.Converters;

// Colore une ligne de la Console transport (chaîne brute "HH:mm:ss.fff  [tag] message") selon son PRÉFIXE [tag] et,
// en priorité, selon des mots-clés d'ÉCHEC (ECHEC/REFUSE/impossible/abandon/erreur -> rouge). Purement visuel : le
// modèle reste une chaîne (aucun changement de TransportDiag). Rend un IBrush ; défaut = texte secondaire.
public sealed class TransportLineBrushConverter : IValueConverter
{
    public static readonly TransportLineBrushConverter Instance = new();

    private static readonly IBrush Error = new ImmutableSolidColorBrush(Color.Parse("#FF6B7A"));   // échec / refus
    private static readonly IBrush Combat = new ImmutableSolidColorBrush(Color.Parse("#E8B339"));  // combat (ambre)
    private static readonly IBrush Chat = new ImmutableSolidColorBrush(Color.Parse("#79C0FF"));    // mp / chat (bleu)
    private static readonly IBrush Trade = new ImmutableSolidColorBrush(Color.Parse("#63D3A6"));   // hdv / banque (vert)
    private static readonly IBrush Move = new ImmutableSolidColorBrush(Color.Parse("#8BD5C6"));    // déplacement / carte (teal)
    private static readonly IBrush Muted = new ImmutableSolidColorBrush(Color.Parse("#79828F"));   // stats / diag
    private static readonly IBrush Cmd = new ImmutableSolidColorBrush(Color.Parse("#C7A0FF"));     // commande console /xxx (violet)
    private static readonly IBrush Default = new ImmutableSolidColorBrush(Color.Parse("#B7C0CC"));

    public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
    {
        if (value is not string s || s.Length == 0) return Default;

        // Mots-clés d'échec (n'importe où dans la ligne) -> rouge, quel que soit le tag.
        if (ContainsAny(s, "ECHEC", "REFUSE", "REFUSÉ", "impossible", "abandon", "erreur", "échec"))
            return Error;

        // Tag entre crochets : [combat] / [mp] / [hdv] ...
        int i = s.IndexOf('[');
        int j = i >= 0 ? s.IndexOf(']', i + 1) : -1;
        if (i < 0 || j < 0) return Default;
        var tag = s.Substring(i + 1, j - i - 1).ToLowerInvariant();
        return tag switch
        {
            "cmd" => Cmd,
            "combat" => Combat,
            "mp" or "chat" or "message" => Chat,
            "hdv" or "banque" or "inventaire" => Trade,
            "move" or "trajet" or "carte" or "map" or "dialogue" or "pnj" => Move,
            "stats" or "mapping" or "pkt-dbg" or "inject" => Muted,
            _ => Default,
        };
    }

    private static bool ContainsAny(string s, params string[] needles)
    {
        foreach (var n in needles)
            if (s.Contains(n, StringComparison.OrdinalIgnoreCase)) return true;
        return false;
    }

    public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
        => throw new NotSupportedException();
}
