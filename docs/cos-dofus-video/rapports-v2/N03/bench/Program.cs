using System.Collections.Immutable;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text.Json;
using GivreClone.App.Services.Combat;
using GivreClone.Protocol;

// Standalone offline fixtures. NEVER opens a game, network, account, bot or transport.
// Compiles only in reports-v2/N03/bench and references an already-built RELEASE DLL.
static int Cell(int x) => MapPathfinder.CellFromMapPoint(8 + x, 8);

static PlannerSpell Spell(int id, SpellEffectKind kind, int action, string mask, int amount, int range = 3)
{
    var e = new SpellEffectDescriptor(action, kind, amount, amount, 0, kind == SpellEffectKind.Shield ? 2 : 0,
        mask, 'P', 0, 0);
    var cast = new SpellCastDescriptor(id, 1, 3, 0, range, 0, false, 1, 1, 0, 0, "",
        ImmutableArray.Create(e));
    return new PlannerSpell(cast, 1, 1, ImmutableDictionary<long, int>.Empty,
        ImmutableHashSet.Create(1L, 2L, 3L, 4L, 5L));
}

static CombatTurnSnapshot Board(string which)
{
    var tiles = Enumerable.Range(0, 8).Select(i => new PlannerCell(Cell(i), true, false,
        Enumerable.Range(0, 8).Where(j => Math.Abs(i - j) == 1).Select(Cell).ToImmutableArray()))
        .ToImmutableArray();
    var fighters = ImmutableArray.Create(new PlannerFighter(1, Cell(0), false, 75, 100),
        new PlannerFighter(2, Cell(5), true, 120, 120, MovePoints: 2, AttackRange: 1,
            ExpectedDamage: 45, ThreatKnown: true));
    var spells = which switch
    {
        "no_spells" => Array.Empty<PlannerSpell>(),
        "one_ranged" => new[] { Spell(10, SpellEffectKind.Damage, 98, "A", 38, 6) },
        "two_attacks" => new[] { Spell(10, SpellEffectKind.Damage, 98, "A", 38, 6),
            Spell(11, SpellEffectKind.Damage, 96, "A", 30, 5) },
        "heal_vs_attack" => new[] { Spell(12, SpellEffectKind.Heal, 108, "g", 40, 6),
            Spell(10, SpellEffectKind.Damage, 98, "A", 38, 6) },
        "three_attacks" => new[] { Spell(10, SpellEffectKind.Damage, 98, "A", 38, 6),
            Spell(11, SpellEffectKind.Damage, 96, "A", 30, 5),
            Spell(13, SpellEffectKind.Damage, 99, "A", 45, 3) },
        _ => throw new InvalidOperationException("Unknown fixture")
    };
    if (which == "heal_vs_attack")
        fighters = fighters.Add(new PlannerFighter(3, Cell(2), false, 10, 100));
    if (which == "three_attacks")
        fighters = fighters.Add(new PlannerFighter(4, Cell(7), true, 150, 150,
            MovePoints: 2, AttackRange: 3, ExpectedDamage: 45, ThreatKnown: true));
    return new CombatTurnSnapshot(tiles, fighters, 1, 9, 4, spells.ToImmutableArray(),
        ImmutableDictionary<int, int>.Empty, MaxCasts: 3);
}

var dir = args.Length > 0 ? Path.GetFullPath(args[0]) : Path.GetFullPath("..");
Directory.CreateDirectory(dir);
var opts = new JsonSerializerOptions { WriteIndented = false };
var cases = new[] { "no_spells", "one_ranged", "two_attacks", "heal_vs_attack", "three_attacks" };
var rows = new List<object>();
var meta = new
{
    generatedUtc = DateTimeOffset.UtcNow,
    dotnet = RuntimeInformation.FrameworkDescription,
    os = RuntimeInformation.OSDescription,
    arch = RuntimeInformation.ProcessArchitecture.ToString(),
    processorCount = Environment.ProcessorCount,
    stopwatchFrequency = Stopwatch.Frequency,
    buildFlavor = "Release-built assembly already in GivreClone.Tests/bin/Release/net10.0",
    iterations = 12,
    warmups = 2,
    fixtures = cases,
};
foreach (var name in cases)
{
    var state = Board(name);
    // Warmup/first JIT explicitly excluded from the steady-state rows.
    for (int i = 0; i < 2; i++)
    {
        long t = Stopwatch.GetTimestamp();
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        var p = CombatTurnPlanner.Choose(state);
        rows.Add(new {
            fixture = name, sample = i, phase = "warmup", elapsedMs =
                (Stopwatch.GetTimestamp() - t) * 1000d / Stopwatch.Frequency,
            allocationBytesThread = GC.GetAllocatedBytesForCurrentThread() - allocated,
            p.Work, p.BudgetExhausted, decision = string.Join(";", p.Actions.Select(a => a.ToString())),
            score = p.Score.Total, p.Unmodeled
        });
    }
    for (int i = 0; i < 12; i++)
    {
        long t = Stopwatch.GetTimestamp();
        long allocated = GC.GetAllocatedBytesForCurrentThread();
        var p = CombatTurnPlanner.Choose(state);
        rows.Add(new {
            fixture = name, sample = i, phase = "steady", elapsedMs =
                (Stopwatch.GetTimestamp() - t) * 1000d / Stopwatch.Frequency,
            allocationBytesThread = GC.GetAllocatedBytesForCurrentThread() - allocated,
            p.Work, p.BudgetExhausted, decision = string.Join(";", p.Actions.Select(a => a.ToString())),
            score = p.Score.Total, p.Unmodeled
        });
    }
}
await File.WriteAllTextAsync(Path.Combine(dir, "environment.json"),
    JsonSerializer.Serialize(meta, new JsonSerializerOptions { WriteIndented = true }));
await File.WriteAllLinesAsync(Path.Combine(dir, "measurements.jsonl"),
    rows.Select(x => JsonSerializer.Serialize(x, opts)));
Console.WriteLine($"OFFLINE_BENCH_DONE cases={cases.Length} rows={rows.Count} dir={dir}");
