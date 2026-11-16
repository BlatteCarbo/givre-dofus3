using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;

namespace GivreClone.App.Services;

/// <summary>Une ligne d'import de compte : identifiant + mot de passe optionnel. Le mot de passe n'apparaît jamais dans un résumé.</summary>
public sealed record ParsedAccount(string Login, string Password, int Line);

public sealed record AccountImportError(int Line, string Reason);

/// <summary>Une ligne de fichier d'import de superviseur : identifiant, mot de passe, libellé facultatif.</summary>
public sealed record ParsedAccountLine(string Login, string Password, string? Label, int Line);

public sealed record AccountImportResult(IReadOnlyList<ParsedAccount> Valid, IReadOnlyList<AccountImportError> Errors, int Duplicates)
{
    public string Summary
    {
        get
        {
            var parts = new List<string> { $"{Valid.Count} compte(s) valide(s)" };
            if (Duplicates > 0) parts.Add($"{Duplicates} doublon(s)");
            if (Errors.Count > 0) parts.Add($"{Errors.Count} ligne(s) ignorée(s)");
            return string.Join(", ", parts);
        }
    }
}

/// <summary>
/// Logique pure de l'onglet Comptes (MZP23) : import en masse, sélection multiple, règles de nom, couleurs de création.
/// Les NOMS des membres de ViewModel viennent de l'original v1.3.1 ; les règles ci-dessous sont des choix de ce lot (non observés).
/// </summary>
public static class AccountsLogic
{
    // ---------------- Import de comptes ----------------
    /// <summary>
    /// Une entrée par ligne : <c>login:motdepasse</c>, <c>login;motdepasse</c> ou <c>login&lt;TAB&gt;motdepasse</c> (le premier séparateur
    /// rencontré coupe ; le mot de passe peut donc contenir ':'). Un login seul est accepté (mot de passe vide : compte à authentifier
    /// ensuite). Lignes vides et commentaires <c>#</c> ignorés. Doublons de login (insensible à la casse) comptés une fois.
    /// </summary>
    public static AccountImportResult ParseAccounts(string? text)
    {
        var valid = new List<ParsedAccount>();
        var errors = new List<AccountImportError>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int dups = 0, n = 0;
        foreach (var raw in (text ?? "").Split('\n'))
        {
            n++;
            var line = raw.TrimEnd('\r').Trim();
            if (line.Length == 0 || line.StartsWith('#')) continue;
            int cut = line.IndexOfAny(new[] { ':', ';', '\t' });
            string login = (cut < 0 ? line : line[..cut]).Trim();
            string pass = cut < 0 ? "" : line[(cut + 1)..];
            if (login.Length == 0) { errors.Add(new(n, "identifiant vide")); continue; }
            if (login.Any(char.IsWhiteSpace)) { errors.Add(new(n, "identifiant invalide")); continue; }
            if (!seen.Add(login)) { dups++; continue; }
            valid.Add(new(login, pass, n));
        }
        return new(valid, errors, dups);
    }


    /// <summary>
    /// Fichier d'import du superviseur (<c>accounts:importFile</c>) : <c>identifiant:motdepasse</c> ou <c>identifiant:motdepasse:libellé</c>.
    /// Avec un libellé (au moins deux ':'), le libellé est le DERNIER champ et le mot de passe, tout ce qui est entre le premier et le
    /// dernier ':' (il peut donc contenir ':'). Lignes vides et commençant par '#' ou '--' ignorées. Doublons de login comptés une fois.
    /// </summary>
    public static (IReadOnlyList<ParsedAccountLine> Valid, IReadOnlyList<AccountImportError> Errors) ParseAccountFile(string? text)
    {
        var valid = new List<ParsedAccountLine>();
        var errors = new List<AccountImportError>();
        var seen = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        int n = 0;
        foreach (var raw in (text ?? "").Split('\n'))
        {
            n++;
            var line = raw.TrimEnd('\r').Trim();
            if (line.Length == 0 || line.StartsWith('#') || line.StartsWith("--", StringComparison.Ordinal)) continue;
            int first = line.IndexOf(':');
            if (first < 0) { errors.Add(new(n, "format identifiant:motdepasse attendu")); continue; }
            string login = line[..first].Trim();
            string rest = line[(first + 1)..];
            string? label = null;
            int last = rest.LastIndexOf(':');
            string pass = rest;
            if (last >= 0) { pass = rest[..last]; label = rest[(last + 1)..].Trim(); if (label.Length == 0) label = null; }
            if (login.Length == 0 || login.Any(char.IsWhiteSpace)) { errors.Add(new(n, "identifiant invalide")); continue; }
            if (!seen.Add(login)) continue;
            valid.Add(new(login, pass, label, n));
        }
        return (valid, errors);
    }

    /// <summary>Statut d'une entrée importée vis-à-vis des comptes déjà présents.</summary>
    public enum ImportStatus { New, AlreadyExists }

    public static ImportStatus StatusOf(string login, IEnumerable<string> existingLogins)
        => existingLogins.Any(l => l.Equals(login, StringComparison.OrdinalIgnoreCase)) ? ImportStatus.AlreadyExists : ImportStatus.New;

    // ---------------- Sélection multiple ----------------
    /// <summary>Case « tout sélectionner » à trois états : true = tout, false = rien, null = partiel. Liste vide → false.</summary>
    public static bool? AllSelectedState(int selected, int total) => total <= 0 || selected <= 0 ? false : selected >= total ? true : null;

    /// <summary>« 3 / 8 sélectionné(s) » ; rien de sélectionné → vide.</summary>
    public static string SelectionText(int selected, int total) => selected <= 0 ? "" : $"{selected} / {total} sélectionné(s)";

    // ---------------- Noms (personnage / pseudo) ----------------
    public const int NameMin = 3, NameMax = 20;

    /// <summary>
    /// Règle de nom de personnage retenue (déduite de l'usage Dofus, à confirmer sur le serveur) : 3 à 20 caractères, lettres
    /// uniquement avec un seul tiret facultatif au milieu, commence par une lettre. Renvoie le motif du refus, ou null si valide.
    /// </summary>
    public static string? ValidateName(string? name)
    {
        var s = (name ?? "").Trim();
        if (s.Length < NameMin) return $"{NameMin} caractères minimum";
        if (s.Length > NameMax) return $"{NameMax} caractères maximum";
        if (!char.IsLetter(s[0])) return "doit commencer par une lettre";
        if (s.Count(c => c == '-') > 1) return "un seul tiret autorisé";
        if (s[^1] == '-') return "ne peut pas finir par un tiret";
        if (s.Any(c => !char.IsLetter(c) && c != '-')) return "lettres et tiret uniquement";
        return null;
    }

    private static readonly string[] Starts = { "Ba", "Ke", "Lu", "Mo", "Ni", "Or", "Pa", "Ri", "So", "Ta", "Ve", "Zu" };
    private static readonly string[] Mids = { "ra", "li", "no", "mu", "te", "sa", "do", "vi", "ko", "ne" };
    private static readonly string[] Ends = { "n", "x", "r", "l", "s", "th", "k", "d" };

    /// <summary>Nom suggéré, toujours valide selon <see cref="ValidateName"/> (2 à 3 syllabes).</summary>
    public static string SuggestName(Random rng)
    {
        var sb = new System.Text.StringBuilder(Starts[rng.Next(Starts.Length)]);
        for (int i = rng.Next(1, 3); i > 0; i--) sb.Append(Mids[rng.Next(Mids.Length)]);
        sb.Append(Ends[rng.Next(Ends.Length)]);
        return sb.ToString();
    }

    // ---------------- Couleurs de création ----------------
    public const int ColorSlots = 6;   // peau, cheveux, tenue 1-4 (6 varints dans CharacterCreationRequest)
    public const int NoColor = -1;

    /// <summary>Six couleurs 24 bits aléatoires.</summary>
    public static int[] RandomColors(Random rng) => Enumerable.Range(0, ColorSlots).Select(_ => rng.Next(0, 0x1000000)).ToArray();

    /// <summary>Six emplacements « non défini ».</summary>
    public static int[] ResetColors() => Enumerable.Repeat(NoColor, ColorSlots).ToArray();

    /// <summary>« #RRGGBB » ; non défini → « — ».</summary>
    public static string ColorLabel(int rgb) => rgb < 0 ? "—" : "#" + (rgb & 0xFFFFFF).ToString("X6", CultureInfo.InvariantCulture);
}
