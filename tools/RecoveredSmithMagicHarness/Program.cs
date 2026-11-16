using GivreClone.Engine.Recovery;

var line = new SmithTarget(118, 25, 40, 40, 20, 35);
var basic = new SmithRune(1, 118, SmithRuneTier.Basic, 5, 10);
var pa = new SmithRune(2, 118, SmithRuneTier.Pa, 2, 50);
var ra = new SmithRune(3, 118, SmithRuneTier.Ra, 3, 100);
var limits = new SmithMagicLimits(100, 50, 1000, false);
int checks = 0;
void Check(bool condition, string label) { checks++; if (!condition) throw new Exception(label); }
SmithMagicDecision Next(SmithTarget? l = null, SmithMagicLimits? lim = null,
    int total = 0, int onLine = 0, long spent = 0, SmithRune[]? runes = null)
    => SmithMagicPlanner.Next(l ?? line, runes ?? [basic, pa, ra], lim ?? limits, total, onLine, spent);
Check(Next().Rune == pa, "Pa threshold");
Check(Next(line with { Current = 35 }).Rune == ra, "Ra threshold");
Check(Next(line with { Current = 10 }).Rune == basic, "Basic threshold");
Check(!Next(line with { Current = 40 }).Ready, "Reached target");
Check(!Next(total: 100).Ready, "Session rune cap");
Check(!Next(onLine: 50).Ready, "Line rune cap");
Check(!Next(spent: 999).Ready, "Budget");
Check(!Next(runes: [basic]).Ready, "No silent downgrade");
Check(Next(lim: limits with { AllowSmallerRune = true }, runes: [basic]).Rune == basic, "Allowed downgrade");
Check(!Next(runes: [pa with { Quantity = 0 }]).Ready, "Empty inventory");
Check(!Next(runes: [pa with { EffectId = 999 }]).Ready, "Effect identity");
Check(!Next(line with { RaFrom = 1 }).Ready, "Invalid thresholds");
Check(!Next(lim: limits with { MaxKamas = long.MaxValue }, spent: long.MaxValue,
    runes: [pa with { UnitPrice = long.MaxValue }]).Ready, "No budget overflow");
Check(!Next(total: -1).Ready, "Invalid counters");
Console.WriteLine($"PASS: {checks} smithmagic planning checks; no game actions performed.");
