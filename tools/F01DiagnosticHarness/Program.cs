using GivreClone.App.Services;

// Le même écrivain que l'application, dans un stockage TestRuns unique sans compte.
Environment.SetEnvironmentVariable("GIVRE_CLONE_RUN_NAME", "f01-rotation-" + Guid.NewGuid().ToString("N"));
TransportDiag.Init();
TransportDiag.Emit(101, "F01_ROTATION_FIRST");
TransportDiag.Flush();
string first = TransportDiag.Path_;
string payload = new('x', 65536);
for (int i = 0; i < 420; i++)
{
    TransportDiag.Emit(101, $"F01_ROTATION_FILL_{i:D3} " + payload);
    TransportDiag.Flush();
}
TransportDiag.Emit(202, "F01_ROTATION_LAST");
TransportDiag.Flush();
string current = TransportDiag.Path_;
if (first == current) throw new Exception("Rotation non déclenchée au seuil réel de 25 Mio.");
static string Read(string path)
{
    using var file = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
    using var reader = new StreamReader(file);
    return reader.ReadToEnd();
}
if (!Read(first).Contains("<101> F01_ROTATION_FIRST")) throw new Exception("Premier segment perdu.");
if (!Read(current).Contains("<202> F01_ROTATION_LAST")) throw new Exception("Dernier segment perdu.");
string directory = Path.GetDirectoryName(current)!;
File.SetLastWriteTimeUtc(first, DateTime.UtcNow.AddHours(1));
if (TransportLogFiles.Select(directory, "", current) != current) throw new Exception("Mauvais segment courant.");
if (TransportLogFiles.Select(directory, Path.GetFileName(first), current) != first) throw new Exception("Historique inaccessible.");
var health = TransportDiag.DiskStatus();
if (health.PendingLines != 0 || health.LastErrorType != null || !health.WorkerAlive) throw new Exception("Écrivain en erreur.");
Console.WriteLine("PASS rotation réelle 25 Mio, premier/dernier marqueurs par PID, sélection courante, historique et santé disque.");
Console.WriteLine("Dossier de preuve : " + directory);

// Tester le signal de fin sur l'hôte réel sans ouvrir de connexion de jeu.
// Seuls _runTask et RunFinished sont utilisés : aucun constructeur ni service global.
var host = (GivreLuaHost)System.Runtime.CompilerServices.RuntimeHelpers.GetUninitializedObject(typeof(GivreLuaHost));
var track = typeof(GivreLuaHost).GetMethod("TrackRun", System.Reflection.BindingFlags.Instance | System.Reflection.BindingFlags.NonPublic)!;
var oldRun = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var newRun = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
var firstSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
host.RunFinished += () => firstSignal.TrySetResult(host.Running);
track.Invoke(host, [oldRun.Task]);
track.Invoke(host, [newRun.Task]);
oldRun.SetResult();
if (!await firstSignal.Task.WaitAsync(TimeSpan.FromSeconds(5))) throw new Exception("La fin de l'ancien run masque le nouveau.");
var finalSignal = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
host.RunFinished += () => finalSignal.TrySetResult(host.Running);
newRun.SetResult();
if (await finalSignal.Task.WaitAsync(TimeSpan.FromSeconds(5))) throw new Exception("Le run terminé est encore actif.");
Console.WriteLine("PASS notification après fin effective et remplacement du run sans faux arrêt.");
