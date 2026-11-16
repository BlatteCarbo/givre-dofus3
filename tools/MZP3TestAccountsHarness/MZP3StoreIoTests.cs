using System.Security.AccessControl;
using System.Security.Principal;
using System.Text.Json;
using GivreClone.App.Services;
using Xunit;

namespace MZP3TestAccountsHarness;

/// <summary>
/// Real, isolated Windows disk IO beneath LOCALAPPDATA/GivreReplica/TestRuns. The
/// test run owns only an opaque GUID-named directory. NEVER targets GivreClone's
/// production %APPDATA% accounts.json or any user-selected test run.
/// </summary>
public sealed class MZP3StoreIoTests : IDisposable
{
    private readonly string _runKey = "mzp3-" + Guid.NewGuid().ToString("N");
    private readonly string _runPath;
    private readonly string _storePath;

    public MZP3StoreIoTests()
    {
        string basePath = Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData), "GivreReplica", "TestRuns");
        _runPath = Path.GetFullPath(Path.Combine(basePath, _runKey));
        _storePath = Path.Combine(_runPath, "mzp3-test-profiles.json");
    }

    public void Dispose()
    {
        // Strictly scoped cleanup of this harness's fresh random test run only.
        var parent = Path.GetFullPath(Path.Combine(Environment.GetFolderPath(
            Environment.SpecialFolder.LocalApplicationData), "GivreReplica", "TestRuns"));
        if (!Path.GetFullPath(_runPath).StartsWith(parent + Path.DirectorySeparatorChar,
                StringComparison.OrdinalIgnoreCase) || !_runKey.StartsWith("mzp3-", StringComparison.Ordinal) ||
            _runKey.Length != 37)
            throw new InvalidOperationException("Unsafe harness cleanup rejected.");
        if (!Directory.Exists(_runPath)) return;
        if (File.GetAttributes(_runPath).HasFlag(FileAttributes.ReparsePoint))
            throw new InvalidOperationException("Refuse to remove reparse-point harness directory.");
        Directory.Delete(_runPath, recursive: true);
    }

    private MZP3TestAccountService Open() => MZP3TestAccountService.Open(_runKey, true);

    [Fact]
    public void Create_list_select_candidate_save_reload_stop_roundtrip_real_io()
    {
        Guid profileId;
        using (var store = Open())
        {
            Assert.Empty(store.Profiles);
            Assert.Equal(MZP3ServiceState.Ready, store.Diagnostics.State);
            var first = store.CreateProfile("Tester Local");
            profileId = first.Id;
            Assert.False(first.IsOfflineCandidate);
            store.SelectProfile(first.Id);
            store.SetOfflineCandidate(first.Id, "Orukam", "Personnage Test", true);
            var second = store.CreateProfile("Second Test");
            store.RenameProfile(second.Id, "Second Renamed");
            Assert.True(store.Diagnostics.HasUnsavedChanges);
            Assert.False(store.Diagnostics.MayPrepareMZP2);
            store.Save();
            Assert.Equal(1, store.Diagnostics.Revision);
            Assert.True(store.Diagnostics.MayPrepareMZP2);
            Assert.Equal(2, store.Profiles.Count);
            Assert.Equal(first.Id, store.SelectedProfileId);
            store.Reload();
            Assert.Equal("Personnage Test", store.Profiles.Single(p => p.Id == first.Id).CharacterCandidate);
            store.Stop();
            Assert.Equal(MZP3ServiceState.Stopped, store.Diagnostics.State);
        }

        Assert.True(File.Exists(_storePath));
        var json = File.ReadAllText(_storePath);
        Assert.Contains("Tester Local", json);
        Assert.DoesNotContain("password", json, StringComparison.OrdinalIgnoreCase);
        using (var reopened = Open())
        {
            Assert.Equal(profileId, reopened.SelectedProfileId);
            Assert.True(reopened.Diagnostics.SelectedOfflineCandidate);
            Assert.Equal(2, reopened.Profiles.Count);
            Assert.Equal(1, reopened.Diagnostics.Revision);
            reopened.ClearOfflineCandidate(profileId);
            reopened.RemoveProfile(reopened.Profiles.Single(p => p.Alias == "Second Renamed").Id);
            reopened.Save();
        }
        using var latest = Open();
        Assert.Single(latest.Profiles);
        Assert.False(latest.Diagnostics.SelectedOfflineCandidate);
        Assert.Equal(2, latest.Diagnostics.Revision);
    }

    [Fact]
    public void Selection_remains_explicit_offline_candidate_and_never_auto_promotes()
    {
        using var store = Open();
        var a = store.CreateProfile("A one");
        var b = store.CreateProfile("B two");
        store.SelectProfile(a.Id);
        store.Save();
        Assert.True(store.Diagnostics.HasSelectedProfile);
        Assert.False(store.Diagnostics.SelectedOfflineCandidate);
        Assert.False(store.Diagnostics.MayPrepareMZP2);
        var ex = Assert.Throws<MZP3StoreException>(() =>
            store.SetOfflineCandidate(a.Id, "Server", "Character", false));
        Assert.Equal(MZP3Failure.CandidateNotExplicit, ex.Failure);
        store.SetOfflineCandidate(b.Id, "Server", "Character", true);
        store.Save();
        Assert.False(store.Diagnostics.SelectedOfflineCandidate); // b isn't selected
        store.SelectProfile(b.Id);
        store.Save();
        Assert.True(store.Diagnostics.SelectedOfflineCandidate);
        Assert.True(store.Diagnostics.MayPrepareMZP2);
    }

    [Fact]
    public void Read_only_profile_list_snapshot_cannot_mutate_internal_state()
    {
        using var store = Open();
        var p = store.CreateProfile("Profile Test");
        store.Save();
        var list = store.Profiles;
        Assert.IsType<MZP3TestProfile[]>(list);
        ((MZP3TestProfile[])list)[0] = p with { Alias = "External" };
        Assert.Equal("Profile Test", store.Profiles.Single().Alias);
    }

    [Fact]
    public void Concurrent_process_lease_blocks_second_owner()
    {
        using var first = Open();
        var error = Assert.Throws<MZP3StoreException>(Open);
        Assert.Equal(MZP3Failure.ConcurrentOwner, error.Failure);
        first.Stop();
        using var second = Open();
        Assert.Empty(second.Profiles);
    }

    [Fact]
    public void Unsaved_edits_block_reload_and_stop_not_silent_loss()
    {
        using var store = Open();
        store.CreateProfile("Need Save");
        Assert.Equal(MZP3Failure.UnsavedChanges,
            Assert.Throws<MZP3StoreException>(store.Reload).Failure);
        Assert.Equal(MZP3Failure.UnsavedChanges,
            Assert.Throws<MZP3StoreException>(store.Stop).Failure);
        Assert.True(store.Diagnostics.ExclusiveOwner);
        store.Save();
        store.Stop();
        Assert.False(store.Diagnostics.ExclusiveOwner);
    }

    [Theory]
    [InlineData("")]
    [InlineData("abc")]
    [InlineData("../GivreClone")]
    [InlineData("C:\\Users\\Other")]
    [InlineData("givre/test")]
    [InlineData("..x")]
    public void Invalid_run_id_is_rejected_before_any_io(string id)
    {
        var ex = Assert.Throws<MZP3StoreException>(() => MZP3TestAccountService.Open(id, true));
        Assert.Equal(MZP3Failure.InvalidRunId, ex.Failure);
    }

    [Fact]
    public void Absent_operator_authorization_never_creates_new_store()
    {
        Assert.Equal(MZP3Failure.ApprovalRequired,
            Assert.Throws<MZP3StoreException>(() =>
                MZP3TestAccountService.Open(_runKey, false)).Failure);
        Assert.False(Directory.Exists(_runPath));
    }

    [Theory]
    [InlineData("pass@word")]
    [InlineData("token:secret")]
    [InlineData("name\npasswd")]
    [InlineData(" email@example.com")]
    public void Alias_is_local_label_only_and_rejects_secret_like_syntax(string alias)
    {
        using var store = Open();
        Assert.Equal(MZP3Failure.IllegalData,
            Assert.Throws<MZP3StoreException>(() => store.CreateProfile(alias)).Failure);
        Assert.Empty(store.Profiles);
    }

    [Fact]
    public void Duplicate_case_insensitive_alias_and_unknown_id_are_refused()
    {
        using var store = Open();
        store.CreateProfile("Local Name");
        Assert.Equal(MZP3Failure.DuplicateAlias,
            Assert.Throws<MZP3StoreException>(() => store.CreateProfile("local name")).Failure);
        Assert.Equal(MZP3Failure.NotFound,
            Assert.Throws<MZP3StoreException>(() => store.SelectProfile(Guid.NewGuid())).Failure);
    }

    [Fact]
    public void Unknown_json_credentials_fields_are_refused_without_migration()
    {
        using (var store = Open()) store.Stop();
        string content = File.ReadAllText(_storePath);
        string changed = content.Replace("\"Sha256\":", "\"password\":\"secret\",\"Sha256\":", StringComparison.Ordinal);
        Assert.NotEqual(content, changed);
        File.WriteAllText(_storePath, changed);
        Assert.Equal(MZP3Failure.MissingOrCorruptStore,
            Assert.Throws<MZP3StoreException>(Open).Failure);
    }

    [Fact]
    public void Unknown_future_format_blocks_open_without_auto_migration()
    {
        using (var store = Open()) store.Stop();
        using var document = JsonDocument.Parse(File.ReadAllBytes(_storePath));
        string json = document.RootElement.GetRawText();
        json = json.Replace("\"FormatVersion\":1", "\"FormatVersion\":999", StringComparison.Ordinal);
        Assert.Contains("\"FormatVersion\":999", json);
        File.WriteAllText(_storePath, json);
        Assert.Equal(MZP3Failure.UnsupportedFormat,
            Assert.Throws<MZP3StoreException>(Open).Failure);
    }

    [Fact]
    public void Torn_tmp_crash_marker_refuses_open_and_no_auto_repair()
    {
        using (var store = Open()) store.Stop();
        File.WriteAllText(_storePath + ".tmp", "interrupted");
        Assert.Equal(MZP3Failure.InterruptedWrite,
            Assert.Throws<MZP3StoreException>(Open).Failure);
        Assert.True(File.Exists(_storePath + ".tmp"));
    }

    [Fact]
    public void Deleted_primary_does_not_become_empty_new_profile_database()
    {
        using (var store = Open()) store.Stop();
        File.Delete(_storePath);
        Assert.Equal(MZP3Failure.MissingOrCorruptStore,
            Assert.Throws<MZP3StoreException>(Open).Failure);
    }

    [Fact]
    public void Store_modified_outside_exclusive_owner_fails_save_instead_of_overwriting()
    {
        using var store = Open();
        store.CreateProfile("Local Profile");
        File.AppendAllText(_storePath, "  ");
        Assert.Equal(MZP3Failure.MissingOrCorruptStore,
            Assert.Throws<MZP3StoreException>(store.Save).Failure);
        Assert.True(store.Diagnostics.HasUnsavedChanges);
    }

    [Fact]
    public void New_test_run_has_owner_only_windows_DACL_on_private_run_folder()
    {
        using var store = Open();
        var info = new DirectoryInfo(_runPath);
        var acl = FileSystemAclExtensions.GetAccessControl(info);
        Assert.True(acl.AreAccessRulesProtected);
        var sid = WindowsIdentity.GetCurrent().User;
        Assert.Equal(sid, acl.GetOwner(typeof(SecurityIdentifier)));
        foreach (FileSystemAccessRule rule in acl.GetAccessRules(true, true, typeof(SecurityIdentifier)))
            if (rule.AccessControlType == AccessControlType.Allow)
                Assert.Equal(sid, rule.IdentityReference);
    }

    [Fact]
    public void Broad_acl_on_existing_test_run_refuses_reopen()
    {
        using (var store = Open()) store.Stop();
        var dir = new DirectoryInfo(_runPath);
        var acl = FileSystemAclExtensions.GetAccessControl(dir);
        acl.SetAccessRuleProtection(isProtected: false, preserveInheritance: true);
        FileSystemAclExtensions.SetAccessControl(dir, acl);
        Assert.Equal(MZP3Failure.UnsafePermissions,
            Assert.Throws<MZP3StoreException>(Open).Failure);
    }

    [Fact]
    public void Symlink_store_or_run_folder_is_rejected_without_following_target()
    {
        using (var store = Open()) store.Stop();
        string target = _storePath + ".external";
        File.Move(_storePath, target);
        try
        {
            File.CreateSymbolicLink(_storePath, target);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or NotSupportedException)
        {
            // Non-admin Windows may disallow symlink creation: IO guard is still
            // separately covered by ACL/format/lock tests. Never enable privileges.
            return;
        }
        Assert.Equal(MZP3Failure.UnsafePath,
            Assert.Throws<MZP3StoreException>(Open).Failure);
    }

    [Fact]
    public void Diagnostics_are_categorical_and_do_not_reveal_profile_names_or_run_path()
    {
        using var store = Open();
        var profile = store.CreateProfile("Locally Selected");
        store.SelectProfile(profile.Id);
        string output = store.Diagnostics.ToString();
        Assert.DoesNotContain("Locally Selected", output);
        Assert.DoesNotContain(_runPath, output);
        Assert.DoesNotContain(profile.Id.ToString(), output);
    }

    [Fact]
    public void Profile_revisions_are_persistent_and_noop_save_is_idempotent()
    {
        using var store = Open();
        var p = store.CreateProfile("Local Account");
        store.Save();
        byte[] initial = File.ReadAllBytes(_storePath);
        store.Save();
        Assert.True(initial.SequenceEqual(File.ReadAllBytes(_storePath)));
        store.RenameProfile(p.Id, "Renamed Account");
        store.Save();
        Assert.Equal(2, store.Diagnostics.Revision);
    }
}
