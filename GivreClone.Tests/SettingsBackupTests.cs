using System.IO;
using System.IO.Compression;
using System.Linq;
using GivreClone.App.Services;
using Xunit;

namespace GivreClone.Tests;

public class SettingsBackupTests
{
    [Fact]
    public void Export_without_secrets_excludes_rotated_and_quarantined_credentials()
    {
        string root = Temp();
        try
        {
            string[] secrets = ["accounts.json.bak", "accounts.json.bad-20261024.bak",
                "proxies.json.recovered", "DEVICE-IDS.JSON.bak", "accounts.json"];
            foreach (string name in secrets) File.WriteAllText(Path.Combine(root, name), "synthetic");
            File.WriteAllText(Path.Combine(root, "combat.json.bak"), "{}");
            Assert.Equal(new[] { "combat.json.bak" }, SettingsBackup.Collect(root, false));
            Assert.Equal(6, SettingsBackup.Collect(root, true).Count);
            string zip = Path.Combine(root, "export.zip");
            Assert.Equal(1, SettingsBackup.Export(root, zip, false));
            using var archive = ZipFile.OpenRead(zip);
            Assert.Equal("combat.json.bak", Assert.Single(archive.Entries).FullName);
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Reexport_inside_data_directory_does_not_archive_its_own_previous_output()
    {
        string root = Temp();
        try
        {
            File.WriteAllText(Path.Combine(root, "combat.json"), "{}");
            string zip = Path.Combine(root, "settings.zip");
            SettingsBackup.Export(root, zip, false);
            Assert.Equal(1, SettingsBackup.Export(root, zip, false));
            using var archive = ZipFile.OpenRead(zip);
            Assert.Equal("combat.json", Assert.Single(archive.Entries).FullName);
        }
        finally { Directory.Delete(root, true); }
    }

    private static string Temp() { var d = Path.Combine(Path.GetTempPath(), "sb-" + Path.GetRandomFileName()); Directory.CreateDirectory(d); return d; }

    [Fact]
    public void Export_skips_caches_logs_and_secrets_unless_asked()
    {
        string root = Temp();
        try
        {
            File.WriteAllText(Path.Combine(root, "combat.json"), "{}");
            File.WriteAllText(Path.Combine(root, "accounts.json"), "secret");
            File.WriteAllText(Path.Combine(root, "transport.log"), "x");
            Directory.CreateDirectory(Path.Combine(root, "scripts")); File.WriteAllText(Path.Combine(root, "scripts", "a.lua"), "print(1)");
            Directory.CreateDirectory(Path.Combine(root, "mapcache")); File.WriteAllText(Path.Combine(root, "mapcache", "m.bin"), "x");
            Assert.Equal(new[] { "combat.json", Path.Combine("scripts", "a.lua") }, SettingsBackup.Collect(root, false).OrderBy(x => x).ToArray());
            Assert.Contains("accounts.json", SettingsBackup.Collect(root, true));
        }
        finally { Directory.Delete(root, true); }
    }

    [Fact]
    public void Import_restores_files_and_backs_up_what_it_replaces()
    {
        string src = Temp(), dst = Temp();
        try
        {
            File.WriteAllText(Path.Combine(src, "combat.json"), "new");
            Directory.CreateDirectory(Path.Combine(src, "scripts")); File.WriteAllText(Path.Combine(src, "scripts", "a.lua"), "lua");
            string zip = Path.Combine(src, "..", Path.GetRandomFileName() + ".zip");
            Assert.Equal(2, SettingsBackup.Export(src, zip, false));
            File.WriteAllText(Path.Combine(dst, "combat.json"), "old");
            var (n, backup) = SettingsBackup.Import(zip, dst);
            Assert.Equal(2, n);
            Assert.Equal("new", File.ReadAllText(Path.Combine(dst, "combat.json")));
            Assert.Equal("lua", File.ReadAllText(Path.Combine(dst, "scripts", "a.lua")));
            Assert.NotNull(backup);
            using var b = ZipFile.OpenRead(backup!);
            Assert.Equal("old", new StreamReader(b.GetEntry("combat.json")!.Open()).ReadToEnd());
            File.Delete(zip);
        }
        finally { Directory.Delete(src, true); Directory.Delete(dst, true); }
    }

    [Fact]
    public void Staged_restore_is_applied_once_at_next_start()
    {
        string src = Temp(), dst = Temp();
        try
        {
            File.WriteAllText(Path.Combine(src, "planning.json"), "restored");
            string zip = Path.Combine(src, "..", Path.GetRandomFileName() + ".zip");
            SettingsBackup.Export(src, zip, false);
            Assert.Null(SettingsBackup.ApplyPending(dst));
            SettingsBackup.Stage(zip, dst);
            Assert.StartsWith("Réglages restaurés : 1", SettingsBackup.ApplyPending(dst));
            Assert.Equal("restored", File.ReadAllText(Path.Combine(dst, "planning.json")));
            Assert.Null(SettingsBackup.ApplyPending(dst));   // consommé
            File.Delete(zip);
        }
        finally { Directory.Delete(src, true); Directory.Delete(dst, true); }
    }

    [Fact]
    public void Import_refuses_entries_that_escape_the_folder()
    {
        string dst = Temp();
        string zip = Path.Combine(Path.GetTempPath(), Path.GetRandomFileName() + ".zip");
        try
        {
            using (var z = ZipFile.Open(zip, ZipArchiveMode.Create))
                using (var w = new StreamWriter(z.CreateEntry("../evil.txt").Open())) w.Write("x");
            Assert.Throws<InvalidDataException>(() => SettingsBackup.Import(zip, dst));
        }
        finally { File.Delete(zip); Directory.Delete(dst, true); }
    }
}
