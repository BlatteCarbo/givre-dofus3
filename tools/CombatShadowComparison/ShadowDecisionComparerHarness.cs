// Standalone deterministic tests. No game, network, native DLL, xUnit or shared csproj.
using System;
using System.Collections.Generic;
using GivreClone.Tools.CombatShadowComparison;

internal static class ShadowDecisionComparerHarness
{
    private const string Version = "fixture-build-A";
    private static readonly ShadowTurnIdentity Identity = new(5, 12, 2, 18);
    private static int _assertions;

    private static ShadowBoard Board() => new(Version, Identity, 560);
    private static ShadowAdvice Advice() =>
        new(Version, Identity, ShadowAdviceStatus.Planned,
            500, ShadowTarget.Actor(20), 100, 128, new[] { 100 });
    private static ObservedCombatCast Observed() =>
        new(Version, Identity, 500, ShadowTarget.Actor(20), 100, 128,
            CastConfirmed: true, CompleteObservation: true);

    private static void Check(bool condition, string test)
    {
        _assertions++;
        if (!condition) throw new InvalidOperationException($"FAIL {_assertions}: {test}");
    }

    private static ShadowComparison Compare(
        ShadowBoard? b = null, ShadowAdvice? a = null, ObservedCombatCast? o = null,
        string? version = Version)
        => ShadowDecisionComparer.Compare(b ?? Board(), a ?? Advice(), o ?? Observed(), version);

    private static void Kind(ShadowComparisonKind expected, ShadowComparison actual, string test)
        => Check(actual.Kind == expected, $"{test}: expected {expected}, got {actual.Kind}");

    private static void Run()
    {
        var agreement = Compare();
        Kind(ShadowComparisonKind.Agree, agreement, "same confirmed cast");
        Check(agreement.CanCompare && agreement.Differences.Count == 0, "no differences");
        Check(agreement.SpellAgrees == true && agreement.TargetAgrees == true
              && agreement.OriginAgrees == true && agreement.AimAgrees == true, "all agree");
        Check(agreement.Identity == Identity, "exact identity echoed");

        var spell = Compare(o: Observed() with { SpellId = 501 });
        Kind(ShadowComparisonKind.DifferentSpell, spell, "spell mismatch");
        Check(spell.Differences.Count == 1 && spell.Differences[0] == ShadowDifference.Spell,
              "spell typed difference");
        Check(spell.SpellAgrees == false && spell.TargetAgrees == true, "independent flags");
        Kind(ShadowComparisonKind.DifferentTarget,
            Compare(o: Observed() with { Target = ShadowTarget.Actor(21) }), "target mismatch");
        Kind(ShadowComparisonKind.DifferentOrigin,
            Compare(o: Observed() with { FromCell = 114 }), "origin mismatch");
        Kind(ShadowComparisonKind.DifferentAim,
            Compare(o: Observed() with { AimCell = 114 }), "aim mismatch");

        var all = Compare(o: Observed() with
        {
            SpellId = 501, Target = ShadowTarget.Actor(21), FromCell = 114, AimCell = 115
        });
        Kind(ShadowComparisonKind.DifferentSpell, all, "primary difference is first");
        Check(all.Differences.Count == 4 && all.Differences[0] == ShadowDifference.Spell
              && all.Differences[1] == ShadowDifference.Target
              && all.Differences[2] == ShadowDifference.Origin
              && all.Differences[3] == ShadowDifference.Aim, "all typed differences retained");
        Check(all.TargetAgrees == false && all.OriginAgrees == false
              && all.AimAgrees == false, "multi-axis flags");

        var groundAdvice = Advice() with { Target = ShadowTarget.GroundCell, AimCell = 120 };
        var groundObserved = Observed() with { Target = ShadowTarget.GroundCell, AimCell = 120 };
        Kind(ShadowComparisonKind.Agree,
            Compare(a: groundAdvice, o: groundObserved), "ground-target portal/glyph/bomb cast");
        Kind(ShadowComparisonKind.DifferentAim,
            Compare(a: groundAdvice, o: groundObserved with { AimCell = 121 }),
            "different placement cell");
        Kind(ShadowComparisonKind.DifferentTarget,
            Compare(a: groundAdvice, o: groundObserved with { Target = ShadowTarget.NoActor }),
            "ground target not silently equal no actor");
        Kind(ShadowComparisonKind.Agree,
            Compare(a: Advice() with { Target = ShadowTarget.NoActor },
                    o: Observed() with { Target = ShadowTarget.NoActor }),
            "explicit no-actor action");
        Kind(ShadowComparisonKind.InsufficientEvidence,
            Compare(o: Observed() with { Target = new ShadowTarget(ShadowTargetKind.Unknown) }),
            "unknown target is not no actor");
        Kind(ShadowComparisonKind.InsufficientEvidence,
            Compare(o: Observed() with { Target = ShadowTarget.Actor(0) }), "zero actor id");
        Kind(ShadowComparisonKind.InsufficientEvidence,
            Compare(a: Advice() with { Target = new ShadowTarget(ShadowTargetKind.GroundCell, 20) }),
            "invalid ground target payload");

        Kind(ShadowComparisonKind.InsufficientEvidence,
            Compare(o: Observed() with { CastConfirmed = false }), "unacknowledged intent");
        Kind(ShadowComparisonKind.InsufficientEvidence,
            Compare(o: Observed() with { CompleteObservation = false }), "partial observation");
        Kind(ShadowComparisonKind.InsufficientEvidence,
            Compare(o: Observed() with { SpellId = 0 }), "observed spell missing");
        Kind(ShadowComparisonKind.InsufficientEvidence,
            Compare(o: Observed() with { AimCell = 560 }), "observed aim beyond geometry");
        Kind(ShadowComparisonKind.InsufficientEvidence,
            Compare(o: Observed() with { FromCell = -1 }), "negative origin");
        Kind(ShadowComparisonKind.InsufficientEvidence,
            Compare(b: Board() with { CellCount = 0 }), "missing geometry");
        Kind(ShadowComparisonKind.Agree,
            Compare(b: Board() with { CellCount = 700 },
                    a: Advice() with { AimCell = 600 },
                    o: Observed() with { AimCell = 600 }),
            "fixture supplied a larger geometry; no hardcoded 560-cell limit");
        Kind(ShadowComparisonKind.InsufficientEvidence,
            Compare(a: Advice() with { AimCell = 600 },
                    o: Observed() with { AimCell = 600 }),
            "fixture supplied a smaller geometry");
        Kind(ShadowComparisonKind.InsufficientEvidence,
            Compare(b: Board() with { Identity = Identity with { FightGeneration = 0 } }),
            "invalid identity");
        Kind(ShadowComparisonKind.InsufficientEvidence,
            ShadowDecisionComparer.Compare(null, Advice(), Observed(), Version), "missing board");
        Kind(ShadowComparisonKind.InsufficientEvidence,
            ShadowDecisionComparer.Compare(Board(), null, Observed(), Version), "missing advice");
        Kind(ShadowComparisonKind.InsufficientEvidence,
            ShadowDecisionComparer.Compare(Board(), Advice(), null, Version), "missing observation");
        Kind(ShadowComparisonKind.InsufficientEvidence,
            Compare(version: ""), "missing expected version");
        Kind(ShadowComparisonKind.InsufficientEvidence,
            Compare(b: Board() with { ClientVersion = "" }), "missing board version");
        Check(Compare(o: Observed() with { CastConfirmed = false }).SpellAgrees == null,
              "non-comparable flags remain unknown");

        Kind(ShadowComparisonKind.VersionMismatch,
            Compare(o: Observed() with { ClientVersion = "fixture-build-B" }),
            "observed version mismatched");
        Kind(ShadowComparisonKind.VersionMismatch,
            Compare(a: Advice() with { ClientVersion = "fixture-build-B" }),
            "advice version mismatched");
        Kind(ShadowComparisonKind.VersionMismatch,
            Compare(b: Board() with { ClientVersion = "fixture-build-B" }),
            "board version mismatched");
        Kind(ShadowComparisonKind.VersionMismatch,
            Compare(version: "fixture-build-B"), "expected version mismatched");

        Kind(ShadowComparisonKind.StaleTurn,
            Compare(o: Observed() with { Identity = Identity with { FightGeneration = 6 } }),
            "observed fight changed");
        Kind(ShadowComparisonKind.StaleTurn,
            Compare(o: Observed() with { Identity = Identity with { TurnGeneration = 13 } }),
            "observed turn changed");
        Kind(ShadowComparisonKind.StaleTurn,
            Compare(o: Observed() with { Identity = Identity with { Round = 3 } }),
            "observed round changed");
        Kind(ShadowComparisonKind.StaleTurn,
            Compare(o: Observed() with { Identity = Identity with { SnapshotRevision = 19 } }),
            "observed snapshot changed");
        Kind(ShadowComparisonKind.StaleTurn,
            Compare(a: Advice() with { Identity = Identity with { SnapshotRevision = 19 } }),
            "advice bound to different snapshot");
        Kind(ShadowComparisonKind.StaleTurn,
            Compare(b: Board() with { Identity = Identity with { SnapshotRevision = 19 } }),
            "board newer than advice and cast");

        foreach (var status in new[]
        {
            ShadowAdviceStatus.Unknown, ShadowAdviceStatus.NoLegalAction,
            ShadowAdviceStatus.IncompleteSnapshot, ShadowAdviceStatus.InvalidSnapshot,
            ShadowAdviceStatus.SearchLimit, ShadowAdviceStatus.TimeLimit
        })
            Kind(ShadowComparisonKind.NoAdvice,
                Compare(a: Advice() with { Status = status }), $"planner status {status}");

        Kind(ShadowComparisonKind.InsufficientEvidence,
            Compare(a: Advice() with { SpellId = -1 }), "invalid advised spell");
        Kind(ShadowComparisonKind.InsufficientEvidence,
            Compare(a: Advice() with { Path = null }), "missing advised path");
        Kind(ShadowComparisonKind.InsufficientEvidence,
            Compare(a: Advice() with { Path = Array.Empty<int>() }), "empty advised path");
        Kind(ShadowComparisonKind.InsufficientEvidence,
            Compare(a: Advice() with { Path = new[] { 114 } }), "advised path wrong final cell");
        Kind(ShadowComparisonKind.InsufficientEvidence,
            Compare(a: Advice() with { Path = new[] { 560, 100 } }),
            "advised path invalid intermediate cell");
        Kind(ShadowComparisonKind.InsufficientEvidence,
            Compare(a: Advice() with { FromCell = 560, Path = new[] { 560 } }),
            "advised origin out of range");
        Kind(ShadowComparisonKind.InsufficientEvidence,
            Compare(a: Advice() with { AimCell = -1 }), "advised aim out of range");
        Kind(ShadowComparisonKind.InsufficientEvidence,
            Compare(a: Advice() with { Target = new ShadowTarget(ShadowTargetKind.Unknown) }),
            "advice target not observed");

        var path = new[] { 99, 100 };
        var advice = Advice() with { Path = path };
        var board = Board();
        var observed = Observed();
        Kind(ShadowComparisonKind.Agree, Compare(board, advice, observed), "advised movement origin");
        Check(path[0] == 99 && path[1] == 100, "no mutation of plan");
        Check(board.CellCount == 560 && observed.SpellId == 500, "no mutation of evidence");

        // A version discrepancy or an unconfirmed cast is never scored as a tactical error.
        Kind(ShadowComparisonKind.InsufficientEvidence,
            Compare(o: Observed() with { CastConfirmed = false, SpellId = 501 }),
            "no false divergence on uncertain send");
        Kind(ShadowComparisonKind.VersionMismatch,
            Compare(o: Observed() with { ClientVersion = "fixture-build-B", SpellId = 501 }),
            "no false divergence across builds");
        Kind(ShadowComparisonKind.StaleTurn,
            Compare(o: Observed() with
            {
                Identity = Identity with { TurnGeneration = 100 }, SpellId = 501
            }), "no false divergence across turns");
    }

    public static int Main()
    {
        try
        {
            Run();
            Console.WriteLine($"PASS CombatShadowComparison: {_assertions} executable assertions; offline only");
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine(ex.Message);
            return 1;
        }
    }
}
