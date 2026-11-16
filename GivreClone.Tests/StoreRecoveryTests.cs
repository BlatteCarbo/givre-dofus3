using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GivreClone.App.Services;
using Xunit;

namespace GivreClone.Tests;

/// <summary>Chargement protégé des stores JSON (<see cref="AtomicJson.LoadStore{T}"/>) : un planning.json / combat.json /
/// bot-settings.json / hdv.json illisible n'est plus avalé puis ÉCRASÉ au Save suivant (perte totale) — il est mis de côté
/// (.bad-…) ou, s'il ne peut pas l'être, jamais réécrit (Save -> .recovered). Dossier temporaire, jamais %APPDATA%.</summary>
public sealed class StoreRecoveryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "givre-store-tests-" + Guid.NewGuid().ToString("N"));
    private static readonly DateTime Stamp = new(2026, 9, 26, 14, 38, 5);

    public StoreRecoveryTests() => Directory.CreateDirectory(_dir);
    public void Dispose() { try { Directory.Delete(_dir, recursive: true); } catch { /* best-effort */ } }

    private static PlanningFile? Parse(string json)
        => (PlanningFile?)JsonSerializer.Deserialize(json, typeof(PlanningFile), PlanningTestJsonContext.Default);

    private static string Serialize(PlanningFile f) => JsonSerializer.Serialize(f, typeof(PlanningFile), PlanningTestJsonContext.Default);

    private static PlanningFile Sample()
    {
        var f = new PlanningFile();
        var cp = new CharPlanning { Enabled = true };
        cp.Scripts.Add(new PlanningScript { Name = "Otomai", Body = "-- o" });
        cp.Slots.Add(new PlanningSlot { Name = "Otomai", Script = "Otomai", Days = 32, StartMin = 390, EndMin = 540 });
        f.Plannings["Persoun"] = cp;
        return f;
    }

    private static void Drain() { while (AtomicJson.StoreNotices.TryDequeue(out _)) { } }

    [Fact]
    public void Missing_file_loads_empty_and_writes_in_place()
    {
        string path = Path.Combine(_dir, "planning.json");
        var r = AtomicJson.LoadStore(path, Parse, () => new PlanningFile());
        Assert.Equal(StoreLoad.Missing, r.Outcome);
        Assert.Empty(r.Data.Plannings);
        Assert.False(r.WriteAside);
        Assert.Equal(path, AtomicJson.StorePath(path, r.WriteAside));
    }

    [Fact]
    public void Readable_file_loads()
    {
        string path = Path.Combine(_dir, "planning.json");
        File.WriteAllText(path, Serialize(Sample()));
        var r = AtomicJson.LoadStore(path, Parse, () => new PlanningFile());
        Assert.Equal(StoreLoad.Loaded, r.Outcome);
        Assert.Equal("Otomai", r.Data.Plannings["Persoun"].Slots[0].Script);
        Assert.True(File.Exists(path));
    }

    [Fact]
    public void Corrupt_file_is_set_aside_never_overwritten()
    {
        Drain();
        string path = Path.Combine(_dir, "planning.json");
        const string corrupt = "{ \"Plannings\": { \"Persoun\": { \"Slots\": [ { \"Days\": \"oops\" ";   // tronqué + type faux
        File.WriteAllText(path, corrupt);
        var logs = new List<string>();
        var r = AtomicJson.LoadStore(path, Parse, () => new PlanningFile(), logs.Add, Stamp);

        Assert.Equal(StoreLoad.Quarantined, r.Outcome);
        Assert.Empty(r.Data.Plannings);
        Assert.False(r.WriteAside);
        string bad = path + ".bad-20260926-143805";
        Assert.Equal(bad, r.BadCopy);
        Assert.True(File.Exists(bad));
        Assert.Equal(corrupt, File.ReadAllText(bad));                        // contenu d'origine intact
        Assert.False(File.Exists(path));
        Assert.Single(logs);
        Assert.Contains("planning.json", logs[0]);
        Assert.DoesNotContain(_dir, logs[0]);                                 // ligne assainie : jamais le chemin complet
        Assert.True(AtomicJson.StoreNotices.TryDequeue(out var n));
        Assert.Equal(("planning.json", Path.GetFileName(bad), false), n);

        // Le Save suivant écrit un fichier sain à sa place ; la copie mise de côté ne bouge pas.
        AtomicJson.Write(AtomicJson.StorePath(path, r.WriteAside), Serialize(r.Data), keepBackup: true);
        Assert.True(File.Exists(path));
        Assert.Equal(corrupt, File.ReadAllText(bad));
    }

    [Fact]
    public void Two_failures_in_the_same_second_get_distinct_copies()
    {
        Drain();
        string path = Path.Combine(_dir, "combat.json");
        File.WriteAllText(path, "not json");
        var a = AtomicJson.LoadStore(path, Parse, () => new PlanningFile(), null, Stamp);
        File.WriteAllText(path, "still not json");
        var b = AtomicJson.LoadStore(path, Parse, () => new PlanningFile(), null, Stamp);
        Assert.Equal(StoreLoad.Quarantined, a.Outcome);
        Assert.Equal(StoreLoad.Quarantined, b.Outcome);
        Assert.NotEqual(a.BadCopy, b.BadCopy);
        Assert.Equal("not json", File.ReadAllText(a.BadCopy!));
        Assert.Equal("still not json", File.ReadAllText(b.BadCopy!));
        Drain();
    }

    [Fact]
    public void Unrenamable_corrupt_file_is_kept_and_saves_go_aside()
    {
        Drain();
        string path = Path.Combine(_dir, "hdv.json");
        File.WriteAllText(path, "{ broken");
        var logs = new List<string>();
        StoreLoadResult<PlanningFile> r;
        // Fichier verrouillé (partage lecture seule, sans suppression) : lecture OK mais contenu cassé, renommage refusé.
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            r = AtomicJson.LoadStore(path, Parse, () => new PlanningFile(), logs.Add, Stamp);

        Assert.Equal(StoreLoad.Unreadable, r.Outcome);
        Assert.True(r.WriteAside);
        Assert.Null(r.BadCopy);
        Assert.Single(logs);
        string aside = AtomicJson.StorePath(path, r.WriteAside);
        Assert.Equal(path + ".recovered", aside);
        AtomicJson.Write(aside, Serialize(Sample()), keepBackup: true);
        Assert.Equal("{ broken", File.ReadAllText(path));                     // original JAMAIS écrasé
        Assert.Equal("Otomai", Parse(File.ReadAllText(aside))!.Plannings["Persoun"].Slots[0].Script);
        Assert.True(AtomicJson.StoreNotices.TryDequeue(out var n));
        Assert.Equal(("hdv.json", (string?)null, false), n);
    }

    [Fact]
    public void Write_with_backup_keeps_exactly_the_previous_version()
    {
        string path = Path.Combine(_dir, "bot-settings.json");
        AtomicJson.Write(path, "v1", keepBackup: true);
        Assert.False(File.Exists(path + ".bak"));                             // rien à garder au 1er écrit
        AtomicJson.Write(path, "v2", keepBackup: true);
        Assert.Equal("v2", File.ReadAllText(path));
        Assert.Equal("v1", File.ReadAllText(path + ".bak"));
        AtomicJson.Write(path, "v3", keepBackup: true);
        Assert.Equal("v3", File.ReadAllText(path));
        Assert.Equal("v2", File.ReadAllText(path + ".bak"));                  // une seule rotation
        Assert.False(File.Exists(path + ".tmp"));
        AtomicJson.Write(path, "v4");                                         // sans sauvegarde : .bak inchangé
        Assert.Equal("v2", File.ReadAllText(path + ".bak"));
        Assert.Equal(new[] { "bot-settings.json", "bot-settings.json.bak" },
                     Directory.GetFiles(_dir).Select(Path.GetFileName).OrderBy(x => x, StringComparer.Ordinal).ToArray());
    }

    [Fact]
    public void Json_null_literal_loads_as_empty()
    {
        string path = Path.Combine(_dir, "planning.json");
        File.WriteAllText(path, "null");
        var r = AtomicJson.LoadStore(path, Parse, () => new PlanningFile());
        Assert.Equal(StoreLoad.Loaded, r.Outcome);
        Assert.Empty(r.Data.Plannings);
    }

    [Fact]
    public void Corrupt_file_restarts_from_the_backup_and_the_backup_survives_later_saves()
    {
        Drain();
        string path = Path.Combine(_dir, "planning.json");
        AtomicJson.Write(path, Serialize(Sample()), keepBackup: true);         // v1 (bonne)
        AtomicJson.Write(path, Serialize(Sample()), keepBackup: true);         // v2 : v1 -> .bak
        File.WriteAllText(path, "{ tronqué");                                  // le fichier principal s'abîme
        var r = AtomicJson.LoadStore(path, Parse, () => new PlanningFile(), null, Stamp);

        Assert.Equal(StoreLoad.Restored, r.Outcome);
        Assert.False(r.WriteAside);
        Assert.Equal("Otomai", r.Data.Plannings["Persoun"].Slots[0].Script);  // repris de la copie de secours
        string bad = path + ".bad-20260926-143805";
        Assert.Equal("{ tronqué", File.ReadAllText(bad));
        Assert.True(File.Exists(bad + ".bak"));                               // copie de secours gardée à côté du .bad
        Assert.True(AtomicJson.StoreNotices.TryDequeue(out var n));
        Assert.Equal(("planning.json", Path.GetFileName(bad), true), n);

        // Deux Save ensuite (le 2e fait tourner .bak) : la copie de secours d'origine reste lisible à côté du .bad.
        var empty = new PlanningFile();
        AtomicJson.Write(path, Serialize(empty), keepBackup: true);
        AtomicJson.Write(path, Serialize(empty), keepBackup: true);
        Assert.Equal("Otomai", Parse(File.ReadAllText(bad + ".bak"))!.Plannings["Persoun"].Slots[0].Script);
    }

    [Fact]
    public void Unrenamable_corrupt_file_restarts_from_the_previous_recovered_copy()
    {
        Drain();
        string path = Path.Combine(_dir, "hdv.json");
        File.WriteAllText(path, "{ broken");
        File.WriteAllText(path + ".recovered", Serialize(Sample()));           // session précédente, déjà écrite à côté
        StoreLoadResult<PlanningFile> r;
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
            r = AtomicJson.LoadStore(path, Parse, () => new PlanningFile(), null, Stamp);

        Assert.Equal(StoreLoad.Unreadable, r.Outcome);
        Assert.True(r.WriteAside);
        Assert.Equal("Otomai", r.Data.Plannings["Persoun"].Slots[0].Script);  // jamais repartir à vide par-dessus .recovered
        Assert.True(AtomicJson.StoreNotices.TryDequeue(out var n));
        Assert.Equal(("hdv.json", (string?)null, true), n);
    }
}
