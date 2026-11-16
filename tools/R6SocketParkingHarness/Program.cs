using GivreClone.App.Services.Transport.Socket;

static void Check(bool condition, string message)
{
    if (!condition) throw new InvalidOperationException(message);
}

static async Task Canceled(Task task)
{
    try { await task.WaitAsync(TimeSpan.FromSeconds(3)); }
    catch (OperationCanceledException) { return; }
    throw new InvalidOperationException("Expected cancellation.");
}

var gate = new SocketCharacterSelectionGate();
Check(!gate.TryResume(), "Early resume must fail.");
Check(!gate.Parked.IsCompleted, "Must not claim parked before reaching selection.");
var selection = gate.WaitBeforeSelectionAsync(CancellationToken.None);
await gate.Parked.WaitAsync(TimeSpan.FromSeconds(3));
Check(!selection.IsCompleted, "Selection must remain blocked while parked.");
Check(gate.TryResume(), "Parked session must resume.");
Check(!gate.TryResume(), "Resume must be single-use.");
await selection.WaitAsync(TimeSpan.FromSeconds(3));
try
{
    await gate.WaitBeforeSelectionAsync(CancellationToken.None);
    throw new Exception("Gate reuse was accepted.");
}
catch (InvalidOperationException) { }
gate.EndSession();
Check(!gate.TryResume(), "Ended session must reject resume.");
Console.WriteLine("PASS parking, early/duplicate resume, single-use and completion");

var failed = new SocketCharacterSelectionGate();
failed.EndSession();
failed.EndSession();
await Canceled(failed.Parked);
await Canceled(failed.WaitBeforeSelectionAsync(CancellationToken.None));
Console.WriteLine("PASS failure before parking and idempotent end");

var stopped = new SocketCharacterSelectionGate();
var waiting = stopped.WaitBeforeSelectionAsync(CancellationToken.None);
stopped.EndSession();
await Canceled(waiting);
Check(!stopped.TryResume(), "Stop must prohibit later resume.");
Console.WriteLine("PASS session end releases parked selection");

using var cts = new CancellationTokenSource();
var canceled = new SocketCharacterSelectionGate();
waiting = canceled.WaitBeforeSelectionAsync(cts.Token);
cts.Cancel();
await Canceled(waiting);
canceled.EndSession();
var beforeEntry = new SocketCharacterSelectionGate();
await Canceled(beforeEntry.WaitBeforeSelectionAsync(cts.Token));
Check(!beforeEntry.Parked.IsCompleted, "Canceled entry must not claim parked.");
beforeEntry.EndSession();
await Canceled(beforeEntry.Parked);
Console.WriteLine("PASS cancellation before and during parking");

for (int i = 0; i < 200; i++)
{
    var racing = new SocketCharacterSelectionGate();
    var blocked = racing.WaitBeforeSelectionAsync(CancellationToken.None);
    await Task.WhenAll(Task.Run(() => racing.TryResume()), Task.Run(racing.EndSession));
    try { await blocked.WaitAsync(TimeSpan.FromSeconds(3)); }
    catch (OperationCanceledException) { }
    Check(!racing.TryResume(), "Race must leave session ended.");
}
Console.WriteLine("PASS 200 concurrent resume/end races; no sockets, accounts or replay");
