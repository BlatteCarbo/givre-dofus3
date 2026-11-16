using System.Collections.Immutable;
using System.Text.Json;
using GivreClone.App.Services.Combat;

string? captureTime = null;
int? diagnosticSpell = null;
if (args.Length >= 2 && args[^2] == "--spell")
{
    diagnosticSpell = int.Parse(args[^1]);
    args = args[..^2];
}
if (args.Length >= 2 && args[^2] == "--at")
{
    captureTime = args[^1];
    args = args[..^2];
}
if (args.Length is not (3 or 4 or 5))
{
    Console.Error.WriteLine("Usage: CombatPlannerReplay <transport.log> <pid> <turn> [maxWork] [diagnosticEndCell] [--at captureTime] [--spell diagnosticSpellId]");
    return 2;
}

string marker = $"[combat-player-empty] pid={long.Parse(args[1])} turn={int.Parse(args[2])} offset=";
var fragments = new SortedDictionary<int, string>();
using var stream = new FileStream(args[0], FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
using var reader = new StreamReader(stream);
bool selectedCapture = false;
while (reader.ReadLine() is { } line)
{
    int start = line.IndexOf(marker, StringComparison.Ordinal);
    if (start < 0) continue;
    start += marker.Length;
    int separator = line.IndexOf(' ', start);
    int offset = int.Parse(line[start..separator]);
    if (captureTime is not null)
    {
        if (offset == 0)
        {
            if (selectedCapture) break;
            selectedCapture = line.StartsWith(captureTime + " ", StringComparison.Ordinal);
        }
        if (!selectedCapture) continue;
    }
    fragments.Add(offset, line[(separator + 1)..]);
}
if (fragments.Count == 0) throw new InvalidDataException("No matching diagnostic fragments.");
int expected = 0;
foreach (var fragment in fragments)
{
    if (fragment.Key != expected) throw new InvalidDataException($"Missing fragment at offset {expected}.");
    expected += fragment.Value.Length;
}
string json = string.Concat(fragments.Values);
var snapshot = JsonSerializer.Deserialize<CombatTurnSnapshot>(json)
    ?? throw new InvalidDataException("Diagnostic snapshot is null.");
using var document = JsonDocument.Parse(json);
if (document.RootElement.TryGetProperty("Team", out var team) && team.ValueKind != JsonValueKind.Null)
{
    if (team.GetProperty("Intents").GetArrayLength() != 0)
        throw new NotSupportedException("This replay supports only diagnostics without team intents.");
    snapshot = snapshot with { TeamView = TeamIntentView.Empty };
}
Console.WriteLine("Replay boundary: logged fields only; no live SpellLookup or omitted state-damage catalog.");
if (diagnosticSpell is int spellId)
{
    snapshot = snapshot with { Spells = snapshot.Spells.Where(s => s.Cast.SpellId == spellId).ToImmutableArray() };
    if (snapshot.Spells.Length == 0) throw new ArgumentException("Diagnostic spell absent from capture.");
    Console.WriteLine($"Counterfactual diagnostic: only spell {spellId}; original target and cell constraints preserved.");
}
Console.Error.WriteLine($"Snapshot: style={snapshot.Style}, movement={snapshot.MovementPolicy}, AP={snapshot.Ap}, MP={snapshot.Mp}, maxCasts={snapshot.MaxCasts}");
foreach (var fighter in snapshot.Fighters)
    Console.Error.WriteLine($"Fighter {fighter.Id}: cell={fighter.Cell}, life={fighter.Life}, enemy={fighter.Enemy}, visible={fighter.Visibility}, targetable={fighter.CanTarget}");
foreach (var candidate in snapshot.Spells)
{
    Console.Error.WriteLine($"Spell {candidate.Cast.SpellId}: admitted={CombatTurnPlanner.IsAdmitted(candidate.Cast)}, enemyDamage={CombatTurnPlanner.HasModeledEnemyDamage(candidate.Cast)}, approach={candidate.AllowApproach}, range={candidate.Cast.Range}");
    if (diagnosticSpell is not null) Console.WriteLine("Diagnostic spell constraints: " + JsonSerializer.Serialize(candidate));
}
int maxWork = args.Length >= 4 ? int.Parse(args[3]) : 60_000;
if (maxWork is < 1 or > CombatTurnPlanner.MaxWork)
    throw new ArgumentOutOfRangeException(nameof(maxWork));
var plan = CombatTurnPlanner.Choose(snapshot, maxWork: maxWork);
Console.WriteLine(JsonSerializer.Serialize(new { plan.Actions, plan.Score, plan.Work, plan.BudgetExhausted }));
if (args.Length == 5)
{
    int endCell = int.Parse(args[4]);
    if (!snapshot.Cells.Any(c => c.Id == endCell && c.Walkable))
        throw new ArgumentOutOfRangeException(nameof(endCell));
    var walkOnly = CombatTurnPlanner.Choose(snapshot with
    {
        MaxCasts = 0, EndCells = System.Collections.Immutable.ImmutableHashSet.Create(endCell)
    }, maxWork: maxWork);
    Console.Error.WriteLine("Constrained walk-only diagnostic: " + JsonSerializer.Serialize(new { walkOnly.Actions, walkOnly.Score, walkOnly.Work }));
}
return 0;
