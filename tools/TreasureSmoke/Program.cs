using GivreClone.Engine.Recovery;

static void Check(bool value, string message)
{
    if (!value) throw new Exception(message);
}
var now = DateTimeOffset.Parse("2026-01-01T00:00:00Z");
var tracker = new TreasureRequestTracker();
Check(!tracker.Receive(100), "Unsolicited response must not count as request completion.");
tracker.Begin("dig", 100, now);
Check(!tracker.Receive(99) && !tracker.Receive(100) && tracker.Pending, "Old receipts must not complete the request.");
try { tracker.Begin("flag", 101, now); throw new Exception("Overlapping request accepted."); }
catch (InvalidOperationException) { }
Check(!tracker.Expire(now.AddSeconds(14), TimeSpan.FromSeconds(15)), "Must not expire early.");
Check(tracker.Receive(101) && !tracker.Pending, "New receipt must end pending state without interpreting result.");
tracker.Begin("flag", 101, now);
Check(tracker.Expire(now.AddSeconds(15), TimeSpan.FromSeconds(15)), "Timeout must permit manual recovery.");
Check(!tracker.Receive(102), "Late receipt must not complete an already timed out request.");
tracker.Begin("giveup", 102, now);
tracker.Clear(); // Changing selected bot or hunt invalidates every pending request.
Check(!tracker.Pending && !tracker.Receive(103), "Selection/hunt reset must discard pending request.");
tracker.Begin("dig", 200, now);
Check(!tracker.Receive(103) && tracker.Receive(201), "An old bot's answer must not complete a later request.");
Check(TreasureRequestTracker.Direction(0) == "→" && TreasureRequestTracker.Direction(2) == "↓"
    && TreasureRequestTracker.Direction(4) == "←" && TreasureRequestTracker.Direction(6) == "↑", "Known direction projection.");
Check(TreasureRequestTracker.Direction(7) == "? (7)", "Unknown direction must remain visibly unknown.");
Console.WriteLine("Treasure bookkeeping: all offline checks passed.");

var service = new GivreClone.App.Services.LaunchService { SnowSeq = 10 };
service.Hunts[1] = new(0, 123, 3, 0, 4, 3, [], [new("poi", 7, 0, 42, 0)], 10);
service.DigAnswers[1] = new(0, 0, 9); // stale cached response
var vm = new GivreClone.App.ViewModels.TreasureViewModel(service);
vm.AttachPid(1);
Check(vm.Hunting && vm.HasSteps && vm.Steps[0].Arrow == "? (7)", "Project actual snapshot with unknown direction preserved.");
Check(!vm.CanFlag, "No flag may be sent without explicit selection.");
vm.SelectedIndex = 0; vm.FlagCommand.Execute(null);
Check(vm.Running && service.Sends.Count == 1 && !vm.CanSend, "Accepted transport send must remain pending.");
service.DigAnswers[1] = new(0, 0, ++service.SnowSeq); vm.Refresh();
Check(vm.Running, "Wrong answer kind cannot acknowledge a flag.");
service.FlagAnswers[1] = new(7, 0, ++service.SnowSeq); vm.Refresh();
Check(!vm.Running && vm.Status.Contains("code 7"), "Display raw server result, never assume success.");
service.SendResult = false; vm.DigCommand.Execute(null);
Check(!vm.Running && vm.Status.Contains("non envoyée"), "Rejected transport send must not begin waiting.");
service.SendResult = true; vm.DigCommand.Execute(null);
Check(vm.Running, "Dig accepted.");
vm.AttachPid(2);
Check(!vm.Hunting && !vm.HasSteps && !vm.Running && vm.SelectedIndex == -1, "Bot selection must clear all stale hunt and request state.");
service.DigAnswers[1] = new(0, 0, ++service.SnowSeq); vm.Refresh();
Check(!vm.Hunting && !vm.Running, "Old bot receipt cannot change selected idle bot.");
vm.AttachPid(1);
service.Hunts.Remove(1); service.Finished[1] = ++service.SnowSeq; vm.Refresh();
Check(!vm.Hunting && !vm.HasSteps && vm.Status.Contains("Fin de chasse"), "Finished hunt clears steps.");
// A later snapshot wins over an older finish event; same PID may begin another hunt.
service.Hunts[1] = new(0, 456, 1, 0, 1, 2, [], [new("dig", 0, 0, 0, 0)], ++service.SnowSeq);
vm.Refresh();
Check(vm.Hunting && vm.Steps[0].Label == "Creuser", "New hunt survives older finished event.");
vm.AttachPid(0);
Check(!vm.CanSend && !vm.HasSteps, "Detaching disables commands.");
Console.WriteLine("Treasure VM: snapshot, selection isolation, command and receipt checks passed.");
