using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;

namespace GivreClone.App.Services.Combat;

public enum JournalKind { Info, Turn, Hp, Cast, Move, Skip, EndTurn, Challenge, Problem, Result }

/// <summary>Une ligne du replay : horodatage, décalage depuis le début du combat, nature et texte (déjà débarrassé du préfixe « [combat] »).</summary>
public sealed record JournalEntry(DateTime At, double Sec, JournalKind Kind, string Text);

public sealed record TurnStat(int Turn, double StartSec, double EndSec, int Casts, int Moves, int Problems, string Hp);

/// <summary>Bilan d'un combat : chiffres clés, issue, constats lisibles (pourquoi une défaite, où le temps est perdu) et chronologie complète.</summary>
public sealed record FightJournal(string Id, uint Pid, DateTime Start, DateTime End, double DurationSec, string Outcome, int Turns, int Casts, int Moves, int Skips, int Problems,
                                  int FailedInjections, int RefusedMoves, int TurnsWithoutCast, double LongestGapSec, string LongestGapAfter, long? Xp, string Loot,
                                  IReadOnlyList<string> Findings, IReadOnlyList<TurnStat> TurnStats, IReadOnlyList<JournalEntry> Entries,
                                  int CompanionCasts = 0, int CompanionMoves = 0)
{
    public bool Lost => Outcome == "défaite";
    public string OneLine => $"{Start:HH:mm:ss} · {Outcome} · {Turns} tour(s) · {DurationSec:0.#} s · {Casts} sort(s)" + (Problems > 0 ? $" · {Problems} souci(s)" : "") + (Xp is { } x ? $" · {x:N0} XP" : "");
}

/// <summary>Replay de combat. Ne modifie pas le moteur : consomme les lignes « [combat] » qu'il écrit déjà dans le journal de transport (MITM et Socket), découpe les combats,
/// classe chaque décision (cible, sort, déplacement, tour passé…), mesure les durées et déduit des constats. Pur : testable avec des lignes de journal.</summary>
public sealed class CombatJournalRecorder
{
    private static readonly Regex Start = new(@"attaque du groupe|puis attaque|\d+ combattant\(s\) :|^placement|: PRÊT|début de tour : MOI|^TOUR style=|défis proposés|défi reçu|prefightManagement|onPlacement", RegexOptions.Compiled);
    private static readonly Regex RoundRx = new(@"début de tour : MOI \(round (\d+)\)", RegexOptions.Compiled);
    private static readonly Regex ScriptTurnRx = new(@"tour (\d+) (?:pris en charge|repris) par le script", RegexOptions.Compiled);
    private static readonly Regex MoveRx = new(@"move_request_to_server_(?:confirmed|refused|timeout)_ms=", RegexOptions.Compiled);
    private static readonly Regex MoveRequestRx = new(@"request_tx kind=move:(\d+)->(\d+)", RegexOptions.Compiled);
    // Ligne canonique du producteur (propriétaire et round explicites) : seule source de comptage quand elle existe ; la mesure turbo correspondante porte « journal=canonical » et ne compte pas.
    private static readonly Regex CanonicalMoveRx = new(@"^move_result owner=(self|companion) round=(\d+) from=(\d+) target=(\d+) final=-?\d+ outcome=(?:confirmed|refused|timeout)\b", RegexOptions.Compiled);
    // Sort injecté : le producteur ajoute « owner=self|companion round=N » capturé AVANT l'envoi. Sans suffixe (anciens journaux) : acteur du tour en cours. Propriétaire inconnu ou round absent : personne.
    private static readonly Regex CastOwnerRx = new(@"inject=OK owner=(\S*)(?: round=(\d+))?", RegexOptions.Compiled);
    private static readonly Regex MoveResultRx = new(@"move_request_to_server_\w+_ms=\d+ from=(\d+) target=(\d+)", RegexOptions.Compiled);
    private static readonly Regex HpRx = new(@"PV tour (\d+) : moi (\d+)/(\d+)", RegexOptions.Compiled);
    private static readonly Regex IssueRx = new(@"bilan -> issue=(-?\d+) xp=(\S+) butin=\[(.*?)\]", RegexOptions.Compiled);
    private static readonly TimeSpan IdleTimeout = TimeSpan.FromMinutes(12);

    private readonly uint _pid;
    private readonly List<JournalEntry> _entries = new();
    private DateTime _start, _last;
    private bool _active;
    private enum Actor { None, Self, Companion }
    private Actor _actor;
    // Heuristique pour les anciens journaux, sans ligne « move_result » canonique : dernière requête de déplacement (request_tx kind=move:from->to), avec l'acteur et le round de
    // celui qui l'a émise. Un résultat « move_request_to_server_* from= target= » non marqué journal=canonical lui est rattaché par (from, target), même s'il arrive après la fin
    // du tour ou après le début du tour suivant ; une nouvelle requête remplace l'ancienne. Ce n'est qu'une corrélation par cases, sans identifiant : elle ne prouve aucune
    // sérialisation des requêtes d'un pid (annulation, reset de combat). Une ligne canonique « move_result owner= round= » n'en dépend pas : son propriétaire et son round sont explicites.
    private readonly record struct PendingMove(int From, int To, Actor Owner, int Turn);
    private PendingMove? _pendingMove;
    private bool _sawMoveRequest;   // journaux sans lignes request_tx (anciens) : repli sur l'acteur du tour en cours
    private int _selfCasts, _selfMoves, _companionCasts, _companionMoves;
    private int _turn;
    private readonly Dictionary<int, (double Start, double End, int Casts, int Moves, int Problems, string Hp)> _turns = new();

    public CombatJournalRecorder(uint pid) => _pid = pid;

    public bool Active => _active;

    /// <summary>Une ligne du journal de transport. Retourne le combat terminé s'il s'achève sur cette ligne, sinon null.</summary>
    public FightJournal? Feed(DateTime at, string rawLine)
    {
        string line = rawLine;
        if (line.StartsWith("[combat]", StringComparison.Ordinal)) line = line[8..].TrimStart();
        else if (line.StartsWith("[combat-planner]", StringComparison.Ordinal)) line = "[combat-planner] " + line[16..].TrimStart();
        else if (line.StartsWith("[combat-cast]", StringComparison.Ordinal)) line = "message serveur de sort : " + line[13..].TrimStart();
        else if (line.StartsWith("[turbo-metric]", StringComparison.Ordinal)) line = "mesure turbo : " + line[14..].TrimStart();
        else return null;

        FightJournal? finished = null;
        if (_active && at - _last > IdleTimeout) finished = Finish(_last, "inconnu", null, "", "combat sans fin observée (inactivité)");
        if (!_active)
        {
            if (!Start.IsMatch(line)) return finished;
            Begin(at);
        }
        _last = at;
        var kind = Classify(line);
        TrackTurn(line, kind, at);
        _entries.Add(new JournalEntry(at, (at - _start).TotalSeconds, kind, line));

        if (IssueRx.Match(line) is { Success: true } m)
        {
            string outcome = m.Groups[1].Value switch { "2" => "victoire", "1" => "nul", "0" => "défaite", _ => "inconnu" };
            long? xp = long.TryParse(m.Groups[2].Value, out var x) ? x : null;
            return Finish(at, outcome, xp, m.Groups[3].Value, null) ?? finished;
        }
        if (line.Contains("fin de combat déduite", StringComparison.Ordinal)) return Finish(at, "inconnu", null, "", "fin de combat déduite du retour en jeu de rôle (pas de bilan reçu)") ?? finished;
        if (line.Contains("abandon ContextQuitRequest", StringComparison.Ordinal)) return Finish(at, "abandon", null, "", "le bot a quitté le combat") ?? finished;
        return finished;
    }

    /// <summary>Force la clôture d'un combat en cours (arrêt de l'application, déconnexion).</summary>
    public FightJournal? Flush(string reason) => _active ? Finish(_last, "inconnu", null, "", reason) : null;

    private void Begin(DateTime at)
    {
        _active = true; _start = at; _last = at; _turn = 0; _actor = Actor.None; _pendingMove = null; _sawMoveRequest = false;
        _selfCasts = _selfMoves = _companionCasts = _companionMoves = 0;
        _entries.Clear(); _turns.Clear();
    }

    private static JournalKind Classify(string t)
    {
        if (t.StartsWith("[combat-planner]", StringComparison.Ordinal)) return JournalKind.Info;
        if (CanonicalMoveRx.IsMatch(t)) return JournalKind.Move;
        if (t.Contains("TOUR PERDU") || t.Contains("TOUR IGNORÉ") || t.Contains("ANNULÉ") || t.Contains("annulé") || t.Contains("refusé") || t.Contains("ECHEC") || t.Contains("⚠")
            || t.Contains("NON forgée") || t.Contains("pas encore mappé") || t.Contains("en erreur") || t.Contains("périmé") || t.Contains("PÉRIMÉ") || t.Contains("bloqué ce tour")) return JournalKind.Problem;
        if (t.StartsWith("PV tour", StringComparison.Ordinal)) return JournalKind.Hp;
        if (t.Contains("début de tour") || t.StartsWith("TOUR style=", StringComparison.Ordinal) || t.Contains("pris en charge par le script") || t.Contains("repris par le script")) return JournalKind.Turn;
        if (t.Contains("fin du tour") || t.Contains("fin de tour")) return JournalKind.EndTurn;
        if (t.Contains("sauté") || t.Contains("NON lancé") || t.Contains("FILET") || t.Contains("aucun sort castable") || t.Contains("pas de mouvement")) return JournalKind.Skip;
        if (t.Contains("lance le sort", StringComparison.Ordinal) && t.Contains("inject=OK", StringComparison.Ordinal)) return JournalKind.Cast;
        if (MoveRx.IsMatch(t)) return t.Contains("journal=canonical", StringComparison.Ordinal) ? JournalKind.Info : JournalKind.Move;   // doublon marqué d'une ligne move_result canonique
        if (t.Contains("défi") || t.Contains("challenge")) return JournalKind.Challenge;
        if (t.Contains("bilan ->")) return JournalKind.Result;
        return JournalKind.Info;
    }

    private void TrackTurn(string t, JournalKind kind, DateTime at)
    {
        double sec = (at - _start).TotalSeconds;
        if (t.Contains("début de tour : COMPAGNON", StringComparison.Ordinal)) { _actor = Actor.Companion; return; }
        if (RoundRx.Match(t) is { Success: true } r) { _turn = int.Parse(r.Groups[1].Value, CultureInfo.InvariantCulture); _actor = Actor.Self; }
        else if (ScriptTurnRx.Match(t) is { Success: true } s) { _turn = int.Parse(s.Groups[1].Value, CultureInfo.InvariantCulture); _actor = Actor.Self; }
        else if (kind == JournalKind.Turn && t.StartsWith("TOUR style=", StringComparison.Ordinal) && _turn == 0 && _actor != Actor.Companion) { _turn = 1; _actor = Actor.Self; }
        // Une requête de sort / de déplacement appartient à l'acteur dont c'est le tour ; les lignes de décision (cible, case de tir…) ne sont pas des requêtes.
        // Hors tour (Actor.None) sans requête qui le prouve, un sort / déplacement n'est attribué à personne : jamais au perso par défaut.
        if (MoveRequestRx.Match(t) is { Success: true } q)
        {
            _pendingMove = new PendingMove(int.Parse(q.Groups[1].Value, CultureInfo.InvariantCulture), int.Parse(q.Groups[2].Value, CultureInfo.InvariantCulture), _actor, _turn);
            _sawMoveRequest = true;
        }
        bool isMove = kind == JournalKind.Move;
        Actor lineActor = _actor;
        int lineTurn = _turn;
        bool explicitCast = false;
        if (isMove && CanonicalMoveRx.Match(t) is { Success: true } c)
        {
            // Propriétaire et round fournis par le producteur : indépendants des en-têtes de tour déjà passés.
            lineActor = c.Groups[1].Value == "companion" ? Actor.Companion : Actor.Self;
            lineTurn = int.Parse(c.Groups[2].Value, CultureInfo.InvariantCulture);
            if (_pendingMove is { } cp && cp.Owner == lineActor && cp.Turn == lineTurn
                && cp.From == int.Parse(c.Groups[3].Value, CultureInfo.InvariantCulture) && cp.To == int.Parse(c.Groups[4].Value, CultureInfo.InvariantCulture)) _pendingMove = null;
        }
        else if (kind == JournalKind.Cast && CastOwnerRx.Match(t) is { Success: true } co)
        {
            // Propriétaire et round fournis par le producteur : indépendants des en-têtes de tour déjà passés ; un propriétaire inconnu n'est jamais compté pour le perso.
            explicitCast = true;
            string owner = co.Groups[1].Value;
            lineActor = !co.Groups[2].Success ? Actor.None : owner == "companion" ? Actor.Companion : owner == "self" ? Actor.Self : Actor.None;
            if (lineActor != Actor.None) lineTurn = int.Parse(co.Groups[2].Value, CultureInfo.InvariantCulture);
        }
        else if (isMove && _sawMoveRequest)
        {
            // Résultat sans requête correspondante (autres cases, doublon) : on ne devine pas, il n'est attribué à personne.
            lineActor = Actor.None;
            if (_pendingMove is { } p && MoveResultRx.Match(t) is { Success: true } m
                && p.From == int.Parse(m.Groups[1].Value, CultureInfo.InvariantCulture) && p.To == int.Parse(m.Groups[2].Value, CultureInfo.InvariantCulture))
            {
                lineActor = p.Owner; lineTurn = p.Turn; _pendingMove = null;
            }
        }
        if (kind == JournalKind.Cast || isMove)
        {
            if (lineActor == Actor.Companion) { if (isMove) _companionMoves++; else _companionCasts++; }
            else if (lineActor == Actor.Self) { if (isMove) _selfMoves++; else _selfCasts++; }
        }
        bool finishes = kind == JournalKind.EndTurn && t.Contains("FightTurnFinishRequest", StringComparison.Ordinal) && t.Contains("inject=OK", StringComparison.Ordinal);
        if (lineActor == Actor.Companion) { if (finishes) _actor = Actor.None; return; }
        if (lineTurn <= 0 || lineActor != Actor.Self) return;
        bool late = (isMove || explicitCast) && (lineTurn != _turn || _actor != Actor.Self);   // résultat ou sort tardif : il complète le round qui l'a demandé sans en prolonger la durée
        _turns.TryGetValue(lineTurn, out var cur);
        if (cur.End == 0 && cur.Start == 0 && !_turns.ContainsKey(lineTurn)) cur = (sec, sec, 0, 0, 0, "");
        if (!late) cur.End = sec;
        switch (kind)
        {
            case JournalKind.Cast: cur.Casts++; break;
            case JournalKind.Move: cur.Moves++; break;
            case JournalKind.Problem: cur.Problems++; break;
        }
        // Un personnage qui ouvre son propre tour n'est pas mort : « moi 0/N » est une valeur non relue (PV du perso jamais mis à jour), on ne la retient pas.
        if (kind == JournalKind.Hp && HpRx.Match(t) is { Success: true } h && h.Groups[2].Value.TrimStart('0').Length > 0) cur.Hp = $"{h.Groups[2].Value}/{h.Groups[3].Value}";
        _turns[lineTurn] = cur;
        if (finishes) _actor = Actor.None;
    }

    private FightJournal? Finish(DateTime end, string outcome, long? xp, string loot, string? note)
    {
        if (!_active) return null;
        _active = false;
        var entries = _entries.ToList();
        if (entries.Count == 0) return null;
        double duration = Math.Max(0, (end - _start).TotalSeconds);
        var stats = _turns.OrderBy(k => k.Key).Select(k => new TurnStat(k.Key, k.Value.Start, k.Value.End, k.Value.Casts, k.Value.Moves, k.Value.Problems, k.Value.Hp)).ToList();
        int casts = _selfCasts, moves = _selfMoves, skips = entries.Count(e => e.Kind == JournalKind.Skip);
        int problems = entries.Count(e => e.Kind == JournalKind.Problem);
        int failed = entries.Count(e => e.Text.Contains("ECHEC", StringComparison.Ordinal));
        int refused = entries.Count(e => e.Text.Contains("déplacement", StringComparison.Ordinal) && (e.Text.Contains("refusé", StringComparison.Ordinal) || e.Text.Contains("ignoré", StringComparison.Ordinal)));
        int noCast = stats.Count(s => s.Casts == 0);
        double gap = 0; string gapAfter = "";
        for (int i = 1; i < entries.Count; i++)
        {
            double d = entries[i].Sec - entries[i - 1].Sec;
            if (d > gap) { gap = d; gapAfter = entries[i - 1].Text; }
        }
        var findings = Analyse(outcome, stats, entries, casts, noCast, failed, refused, gap, gapAfter, duration, note);
        string id = $"{_start:yyyyMMdd-HHmmss}-{_pid}";
        return new FightJournal(id, _pid, _start, end, duration, outcome, stats.Count > 0 ? stats.Max(s => s.Turn) : 0, casts, moves, skips, problems, failed, refused, noCast, gap,
                                gapAfter.Length > 140 ? gapAfter[..140] + "…" : gapAfter, xp, loot, findings, stats, entries, _companionCasts, _companionMoves);
    }

    private static List<string> Analyse(string outcome, List<TurnStat> stats, List<JournalEntry> entries, int casts, int noCast, int failed, int refused, double gap, string gapAfter, double duration, string? note)
    {
        var f = new List<string>();
        if (note is not null) f.Add(note);
        if (outcome == "défaite") f.Add("DÉFAITE — pistes ci-dessous (dans l'ordre de gravité probable).");
        if (casts == 0 && stats.Count > 0) f.Add("Aucun sort lancé pendant tout le combat : l'IA n'a rien attaqué (voir les lignes « sauté » / « FILET » : portée, PA, ligne de vue ou liste de sorts du profil).");
        else if (noCast >= 2) f.Add($"{noCast} tour(s) sans aucun sort lancé sur {stats.Count} : temps et PV perdus.");
        int filet = entries.Count(e => e.Text.Contains("FILET", StringComparison.Ordinal) || e.Text.Contains("aucun sort castable", StringComparison.Ordinal));
        if (filet > 0) f.Add($"{filet} fois « aucun sort castable » : l'ennemi était hors de portée ou hors de vue (rapprochement insuffisant, PM, tacle).");
        if (refused >= 2) f.Add($"{refused} déplacements refusés ou ignorés : tacle, cases occupées ou obstacle non décodé.");
        if (failed > 0) f.Add($"{failed} envoi(s) en ECHEC (paquet non forgé ou refusé) : voir les lignes marquées ⚠.");
        int lost = entries.Count(e => e.Text.Contains("TOUR PERDU", StringComparison.Ordinal) || e.Text.Contains("TOUR IGNORÉ", StringComparison.Ordinal));
        if (lost > 0) f.Add($"{lost} tour(s) perdu(s) ou ignoré(s) : le début de tour n'a pas été vu à temps ; le bot n'a rien joué.");
        int skippedSpells = entries.Count(e => e.Text.Contains("sauté", StringComparison.Ordinal));
        if (skippedSpells >= 3) f.Add($"{skippedSpells} sorts « sautés » (conditions non remplies : PA, tour, nombre d'ennemis, PV) — vérifie les conditions du profil.");
        // PV : dernière valeur connue et plus bas
        var hp = stats.Where(s => s.Hp.Length > 0).Select(s => (s.Turn, Pair: s.Hp.Split('/'))).Where(x => x.Pair.Length == 2 && int.TryParse(x.Pair[0], out _) && int.TryParse(x.Pair[1], out var m) && m > 0)
                      .Select(x => (x.Turn, Pct: 100.0 * int.Parse(x.Pair[0]) / int.Parse(x.Pair[1]))).ToList();
        if (hp.Count > 0)
        {
            var low = hp.OrderBy(h => h.Pct).First();
            if (low.Pct < 50) f.Add($"PV du perso tombés à {low.Pct:0} % au tour {low.Turn}" + (outcome == "défaite" ? " : les dégâts subis ont dépassé ce que l'IA évite (pas de soin / de fuite / de kite)." : "."));
        }
        // temps perdu
        var slow = stats.Where(s => s.EndSec - s.StartSec > 20).ToList();
        if (slow.Count > 0) f.Add($"{slow.Count} tour(s) de plus de 20 s (le plus long : tour {slow.OrderByDescending(s => s.EndSec - s.StartSec).First().Turn}) : vitesse du profil, attentes de séquence ou barrière Turbo.");
        if (gap >= 5) f.Add($"Plus longue pause : {gap:0.#} s après « {(gapAfter.Length > 90 ? gapAfter[..90] + "…" : gapAfter)} ».");
        if (duration > 180) f.Add($"Combat long ({duration / 60:0.#} min).");
        int ch = entries.Count(e => e.Kind == JournalKind.Challenge && e.Text.Contains("échoué", StringComparison.Ordinal));
        if (ch > 0) f.Add($"{ch} défi(s) échoué(s) : bonus perdu.");
        if (f.Count == 0) f.Add(outcome == "victoire" ? "Rien d'anormal relevé." : "Aucun constat automatique : relis la chronologie.");
        return f;
    }
}

/// <summary>Texte lisible d'un combat (en-tête, constats, tours, chronologie).</summary>
public static class CombatJournalFormat
{
    public static string ToText(FightJournal j)
    {
        var sb = new StringBuilder();
        sb.Append("=== Combat ").Append(j.Id).Append(" — ").Append(j.Outcome.ToUpperInvariant()).Append(" ===\n");
        sb.Append("Début ").Append(j.Start.ToString("yyyy-MM-dd HH:mm:ss", CultureInfo.InvariantCulture)).Append(" · durée ").Append(j.DurationSec.ToString("0.0", CultureInfo.InvariantCulture)).Append(" s · ")
          .Append(j.Turns).Append(" tour(s) · ").Append(j.Casts).Append(" sort(s) · ").Append(j.Moves).Append(" déplacement(s) · ").Append(j.Problems).Append(" souci(s)\n");
        sb.Append("Sorts : envois injectés, pas une preuve de dégâts ; déplacements : demandes tentées, quel que soit le résultat (confirmé, refusé ou sans réponse).\n");
        sb.Append("Totaux et tableau des tours : personnage uniquement ; les compagnons contrôlés sont comptés à part.\n");
        if (j.CompanionCasts + j.CompanionMoves > 0)
            sb.Append("Compagnon : ").Append(j.CompanionCasts).Append(" sort(s) · ").Append(j.CompanionMoves).Append(" déplacement(s)\n");
        if (j.Xp is { } xp) sb.Append("XP ").Append(xp.ToString("N0", CultureInfo.InvariantCulture)).Append(j.Loot.Length > 0 ? " · butin " + j.Loot : "").Append('\n');
        sb.Append("\nConstats :\n");
        foreach (var f in j.Findings) sb.Append("  - ").Append(f).Append('\n');
        if (j.TurnStats.Count > 0)
        {
            sb.Append("\nTours :\n");
            foreach (var t in j.TurnStats)
                sb.Append(FormattableString.Invariant($"  tour {t.Turn,2} : {t.EndSec - t.StartSec,5:0.0} s · {t.Casts} sort(s) · {t.Moves} dépl. · {t.Problems} souci(s)")).Append(t.Hp.Length > 0 ? $" · PV {t.Hp}" : "").Append('\n');
        }
        sb.Append("\nChronologie :\n");
        foreach (var e in j.Entries)
            sb.Append(FormattableString.Invariant($"  +{e.Sec,7:0.000} s  {Tag(e.Kind)}  ")).Append(e.Text).Append('\n');
        return sb.ToString();
    }

    private static string Tag(JournalKind k) => k switch
    {
        JournalKind.Turn => "TOUR ", JournalKind.Hp => "PV   ", JournalKind.Cast => "SORT ", JournalKind.Move => "DEPL.", JournalKind.Skip => "SAUT ", JournalKind.EndTurn => "FIN  ",
        JournalKind.Challenge => "DEFI ", JournalKind.Problem => "!!!  ", JournalKind.Result => "BILAN", _ => "     ",
    };
}
