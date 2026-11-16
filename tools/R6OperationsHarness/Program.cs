using GivreClone.App.Services;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

var monday = new DateTime(2026, 10, 19);
var valid = new PlanningSlot { Days = 1, StartMin = 600, EndMin = 720 };
var invalid = new[]
{
    new PlanningSlot { Days = 128, StartMin = 600, EndMin = 720 },
    new PlanningSlot { Days = -1, StartMin = 600, EndMin = 720 },
    new PlanningSlot { Days = 129, StartMin = 600, EndMin = 720 },
    new PlanningSlot { Days = 0, StartMin = 600, EndMin = 720 },
    new PlanningSlot { Days = 1, StartMin = -1, EndMin = 720 },
    new PlanningSlot { Days = 1, StartMin = 1440, EndMin = 720 },
    new PlanningSlot { Days = 1, StartMin = 600, EndMin = -1 },
    new PlanningSlot { Days = 1, StartMin = 600, EndMin = 1440 },
    new PlanningSlot { Days = 1, StartMin = int.MinValue, EndMin = int.MaxValue }
};

foreach (var slot in invalid)
{
    var plan = new CharPlanning { Enabled = true, AutoConnect = true, Slots = { slot } };
    Check(!PlanningEval.HasUsableSlot(plan), $"Unusable slot accepted: days={slot.Days}, start={slot.StartMin}, end={slot.EndMin}");
    Check(!PlanningEval.Overlaps(slot, valid) && !PlanningEval.Overlaps(valid, slot), "Invalid slot must not overlap.");
    for (int m = 0; m < 7 * 1440; m++)
    {
        var now = monday.AddMinutes(m);
        Check(PlanningEval.ActiveSlotIndex(plan, now, "Fixture") == -1, "Invalid slot became active.");
        Check(PlanningEval.ActiveOccurrence(plan, now, "Fixture") is null, "Invalid occurrence was produced.");
    }
    Check(PlanningEval.NextStart(plan, monday, "Fixture") is null, "Invalid next start was produced.");
    Check(!PlanningEval.StartsWithin(plan, monday.AddHours(9), "Fixture", 120), "Invalid slot retained a connection.");
}
Console.WriteLine("PASS 9 invalid slot cases, each checked across a full week");

var mixed = new CharPlanning { Enabled = true, Slots = { invalid[0], valid } };
Check(PlanningEval.HasUsableSlot(mixed), "Valid slot must remain usable.");
Check(PlanningEval.ActiveSlotIndex(mixed, monday.AddHours(10), "Fixture") == 1, "Valid slot index changed.");
Check(PlanningEval.NextStart(mixed, monday.AddHours(9), "Fixture") == monday.AddHours(10), "Valid next start changed.");
Check(PlanningEval.StartsWithin(mixed, monday.AddHours(9).AddMinutes(59), "Fixture", 1), "Valid near start changed.");
Check(!PlanningEval.IsActiveNow(mixed, monday.AddHours(12), "Fixture"), "End must remain exclusive.");
Console.WriteLine("PASS mixed valid/invalid slots and daytime boundaries");

var night = new CharPlanning { Slots = { new PlanningSlot { Days = 64, StartMin = 1320, EndMin = 360 } } };
Check(PlanningEval.IsActiveNow(night, monday.AddHours(1), "Fixture"), "Sunday overnight lost Monday continuation.");
Check(PlanningEval.Overlaps(night.Slots[0], new PlanningSlot { Days = 1, StartMin = 0, EndMin = 120 }), "Week wrap overlap lost.");
Check(!PlanningEval.IsActiveNow(night, monday.AddHours(6), "Fixture"), "Overnight end must be exclusive.");
var allDay = new CharPlanning { Slots = { new PlanningSlot { Days = 127, StartMin = 0, EndMin = 0 } } };
Check(PlanningEval.HasUsableSlot(allDay), "Equal bounds must still mean 24 hours.");
for (int m = 0; m < 7 * 1440; m++)
    Check(PlanningEval.IsActiveNow(allDay, monday.AddMinutes(m), "Fixture"), "24/7 coverage regressed.");
Console.WriteLine("PASS overnight, Sunday/Monday wrap and all 10,080 minutes of 24/7 coverage");
