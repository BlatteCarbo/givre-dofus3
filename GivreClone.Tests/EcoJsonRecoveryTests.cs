using System;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;
using GivreClone.App.Services.Eco;
using Xunit;

namespace GivreClone.Tests;

/// <summary>
/// Tests purement disque dans un dossier TEMP propre au test : jamais les données réelles de l'utilisateur.
/// La suite est laissée au prime ; aucune connexion de jeu n'est nécessaire.
/// </summary>
public sealed class EcoJsonRecoveryTests : IDisposable
{
    private readonly string _dir = Path.Combine(Path.GetTempPath(), "givre-ecojson-" + Guid.NewGuid().ToString("N"));

    public EcoJsonRecoveryTests() => Directory.CreateDirectory(_dir);

    public void Dispose()
    {
        try { Directory.Delete(_dir, recursive: true); }
        catch (IOException) { }
        catch (UnauthorizedAccessException) { }
    }

    private string FilePath(string name = "eco.json") => Path.Combine(_dir, name);
    private static Notebook Sample(int number) => new()
    {
        Notes = { new Notebook.Note { Title = "note-" + number, Text = "v" + number } },
        TrackedItems = { number + 1000 }
    };

    private static int Marker(Notebook book) => int.Parse(Assert.Single(book.Notes).Title[5..]);
    // AtomicJson.LoadStore conserve AUSSI le backup lisible à côté de l'original
    // corrompu sous <fichier>.bad-<date>.bak : ce n'est pas une 2e quarantaine.
    private static string SingleBadCopy(string path)
        => Assert.Single(Directory.GetFiles(Path.GetDirectoryName(path)!, Path.GetFileName(path) + ".bad-*")
            .Where(file => !file.EndsWith(".bak", StringComparison.OrdinalIgnoreCase)));

    [Fact]
    public void Missing_roundtrip_and_overwrite_produce_a_verified_previous_backup()
    {
        string path = FilePath();
        Assert.Empty(EcoJson.Load<Notebook>(path).Notes);

        EcoJson.Save(path, Sample(1));
        string previous = File.ReadAllText(path);
        Assert.False(File.Exists(path + ".bak")); // première sauvegarde : aucune ancienne version

        EcoJson.Save(path, Sample(2));
        Assert.Equal(previous, File.ReadAllText(path + ".bak"));
        Assert.Equal(1, Marker(JsonSerializer.Deserialize<Notebook>(File.ReadAllText(path + ".bak"))!));
        Assert.Equal(2, Marker(EcoJson.Load<Notebook>(path)));
        Assert.False(File.Exists(path + ".tmp"));
    }

    [Fact]
    public void Corrupt_file_on_load_is_preserved_before_a_new_save()
    {
        string path = FilePath();
        const string corrupt = "{\"Notes\":[{\"Title\":42},";
        File.WriteAllText(path, corrupt);

        Assert.Empty(EcoJson.Load<Notebook>(path).Notes);
        string bad = SingleBadCopy(path);
        Assert.Equal(corrupt, File.ReadAllText(bad));

        EcoJson.Save(path, Sample(3));
        Assert.Equal(3, Marker(EcoJson.Load<Notebook>(path)));
        Assert.Equal(corrupt, File.ReadAllText(bad));
    }

    [Fact]
    public void Save_without_new_load_does_not_overwrite_newly_corrupt_file()
    {
        string path = FilePath();
        EcoJson.Save(path, Sample(1));
        _ = EcoJson.Load<Notebook>(path);
        const string corrupt = "not valid JSON after an external edit";
        File.WriteAllText(path, corrupt);

        EcoJson.Save(path, Sample(4));

        Assert.Equal(corrupt, File.ReadAllText(SingleBadCopy(path)));
        Assert.Equal(4, Marker(EcoJson.Load<Notebook>(path)));
    }

    [Fact]
    public void Save_on_never_loaded_corrupt_file_preserves_the_original()
    {
        string path = FilePath();
        const string corrupt = "{truncated from earlier run";
        File.WriteAllText(path, corrupt);

        EcoJson.Save(path, Sample(9));

        Assert.Equal(corrupt, File.ReadAllText(SingleBadCopy(path)));
        Assert.Equal(9, Marker(EcoJson.Load<Notebook>(path)));
    }

    [Fact]
    public void Corrupt_current_restores_last_verified_backup_without_destroying_bad_bytes()
    {
        string path = FilePath();
        EcoJson.Save(path, Sample(1));
        EcoJson.Save(path, Sample(2));
        byte[] previousBackupBytes = File.ReadAllBytes(path + ".bak");
        const string corrupt = "{broken current";
        File.WriteAllText(path, corrupt);
        byte[] corruptBytes = File.ReadAllBytes(path);

        Assert.Equal(1, Marker(EcoJson.Load<Notebook>(path))); // .bak issu du premier Save
        string bad = SingleBadCopy(path);
        // Deux fichiers par conception : <path>.bad-T (l'original abîmé) et
        // <path>.bad-T.bak (une COPIE du précédent backup sain, pour la récupération).
        string badBackup = bad + ".bak";
        Assert.Equal(2, Directory.GetFiles(_dir, "eco.json.bad-*").Length);
        Assert.True(File.Exists(badBackup));
        Assert.Equal(corruptBytes, File.ReadAllBytes(bad));
        Assert.Equal(previousBackupBytes, File.ReadAllBytes(badBackup));
        Assert.Equal(previousBackupBytes, File.ReadAllBytes(path + ".bak"));

        EcoJson.Save(path, Sample(5));
        Assert.Equal(5, Marker(EcoJson.Load<Notebook>(path)));
        Assert.Equal(1, Marker(JsonSerializer.Deserialize<Notebook>(File.ReadAllText(path + ".bak"))!));
        Assert.Equal(corruptBytes, File.ReadAllBytes(bad));
        Assert.Equal(previousBackupBytes, File.ReadAllBytes(badBackup));
    }

    [Fact]
    public void Null_json_is_not_mistaken_for_an_empty_valid_store()
    {
        string path = FilePath();
        File.WriteAllText(path, "null");

        Assert.Empty(EcoJson.Load<Notebook>(path).Notes);
        Assert.Equal("null", File.ReadAllText(SingleBadCopy(path)));
    }

    [Theory]
    [InlineData("{\"Notes\":null}")]
    [InlineData("{\"Notes\":[null]}")]
    public void Structurally_invalid_notebook_is_quarantined_not_used(string content)
    {
        string path = FilePath();
        File.WriteAllText(path, content);

        Assert.Empty(EcoJson.Load<Notebook>(path).Notes);
        Assert.Equal(content, File.ReadAllText(SingleBadCopy(path)));
    }

    [Fact]
    public void Corrupt_existing_backup_is_preserved_before_rotation()
    {
        string path = FilePath();
        EcoJson.Save(path, Sample(1));
        EcoJson.Save(path, Sample(2));
        const string corruptBackup = "{\"Notes\": invalid}";
        File.WriteAllText(path + ".bak", corruptBackup);

        EcoJson.Save(path, Sample(3));

        Assert.Equal(corruptBackup, File.ReadAllText(SingleBadCopy(path + ".bak")));
        Assert.Equal(2, Marker(JsonSerializer.Deserialize<Notebook>(File.ReadAllText(path + ".bak"))!));
        Assert.Equal(3, Marker(EcoJson.Load<Notebook>(path)));
    }

    [Fact]
    public void Concurrent_readers_and_writers_share_one_atomic_target_and_backup()
    {
        string path = FilePath();
        EcoJson.Save(path, Sample(0));
        Parallel.For(1, 65, new ParallelOptions { MaxDegreeOfParallelism = 8 }, marker =>
        {
            EcoJson.Save(path, Sample(marker));
            Assert.InRange(Marker(EcoJson.Load<Notebook>(path)), 0, 64);
        });

        Assert.InRange(Marker(EcoJson.Load<Notebook>(path)), 1, 64);
        Assert.InRange(Marker(JsonSerializer.Deserialize<Notebook>(File.ReadAllText(path + ".bak"))!), 0, 64);
        Assert.Empty(Directory.GetFiles(_dir, "*.bad-*"));
        Assert.False(File.Exists(path + ".tmp"));
        Assert.False(File.Exists(path + ".bak.tmp"));
    }

    [Fact]
    public void Unrenamable_corrupt_original_only_writes_recovered_then_recovers_later()
    {
        if (!OperatingSystem.IsWindows()) return; // Windows FileShare.Read bloque le renommage
        string path = FilePath();
        const string corrupt = "{broken and locked";
        File.WriteAllText(path, corrupt);
        using (new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            Assert.Empty(EcoJson.Load<Notebook>(path).Notes);
            EcoJson.Save(path, Sample(7));
            Assert.Equal(corrupt, File.ReadAllText(path));
            Assert.Equal(7, Marker(JsonSerializer.Deserialize<Notebook>(File.ReadAllText(path + ".recovered"))!));
        }

        // Le verrou est levé : Load met maintenant l'original cassé de côté, mais reprend
        // les données les plus récentes de .recovered au lieu de repartir à zéro.
        Assert.Equal(7, Marker(EcoJson.Load<Notebook>(path)));
        Assert.Equal(corrupt, File.ReadAllText(SingleBadCopy(path)));
        EcoJson.Save(path, Sample(8));
        Assert.Equal(8, Marker(EcoJson.Load<Notebook>(path)));
    }

    [Fact]
    public void Locked_backup_aborts_save_before_touching_current_data()
    {
        if (!OperatingSystem.IsWindows()) return;
        string path = FilePath();
        EcoJson.Save(path, Sample(1));
        EcoJson.Save(path, Sample(2));
        string before = File.ReadAllText(path);

        using (new FileStream(path + ".bak", FileMode.Open, FileAccess.Read, FileShare.Read))
        {
            var failure = Record.Exception(() => EcoJson.Save(path, Sample(3)));
            Assert.True(failure is IOException or UnauthorizedAccessException);
            Assert.Equal(before, File.ReadAllText(path));
        }
        Assert.Equal(2, Marker(EcoJson.Load<Notebook>(path)));
    }

    public sealed record NoDefaultConstructor(string Name);

    [Fact]
    public void Save_keeps_its_unrestricted_generic_api_for_types_without_default_constructor()
    {
        string path = FilePath();
        EcoJson.Save(path, new NoDefaultConstructor("saved"));
        Assert.Equal("saved", JsonSerializer.Deserialize<NoDefaultConstructor>(File.ReadAllText(path))?.Name);
    }
}
