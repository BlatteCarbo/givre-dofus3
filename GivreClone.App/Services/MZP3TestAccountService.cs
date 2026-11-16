using System.Security.AccessControl;
using System.Security.Cryptography;
using System.Security.Principal;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GivreClone.App.Services;

/// <summary>Local, non-secret UI label. No credentials, game auth or live identification.</summary>
public sealed record MZP3TestProfile(Guid Id, string Alias, string? ServerCandidate,
    string? CharacterCandidate, bool IsOfflineCandidate);

public enum MZP3ServiceState { Ready, Dirty, Faulted, Stopped }
public enum MZP3Failure
{
    ApprovalRequired, UnsupportedPlatform, InvalidRunId, UnsafePath, UnsafePermissions,
    ConcurrentOwner, InterruptedWrite, MissingOrCorruptStore, UnsupportedFormat,
    IllegalData, TooManyProfiles, DuplicateAlias, NotFound, UnsavedChanges,
    CandidateNotExplicit, NoSelection, InvalidPort, Disposed, IoUnavailable
}

public sealed class MZP3StoreException(MZP3Failure failure)
    : InvalidOperationException($"Isolated test-profile operation denied ({failure}).")
{
    public MZP3Failure Failure { get; } = failure;
}

/// <summary>Only categorical UI capabilities, never profile names, paths or account IDs.</summary>
public sealed record MZP3Diagnostics(MZP3ServiceState State, int ProfileCount,
    bool HasSelectedProfile, bool SelectedOfflineCandidate, bool HasUnsavedChanges,
    long Revision, bool ExclusiveOwner, bool MayPrepareMZP2);

internal sealed record MZP3StoreBody(int FormatVersion, long Revision,
    Guid? SelectedProfileId, List<MZP3TestProfile> Profiles);
internal sealed record MZP3StoreEnvelope(MZP3StoreBody Body, string Sha256);

/// <summary>
/// An owner-operated offline TEST-profile store. Its data model deliberately has
/// no token/password/login field; unknown JSON properties fail closed. It never
/// opens GivreClone.Engine.Accounts.AccountManager or real GivreClone account files.
/// One exclusive writer per test-run file, with a durable per-run generation.
/// </summary>
public sealed partial class MZP3TestAccountService : IDisposable
{
    private const int FormatVersion = 1;
    private const int MaxProfiles = 64;
    private const int MaxFileBytes = 131072;
    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = false,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        AllowTrailingCommas = false,
        ReadCommentHandling = JsonCommentHandling.Disallow,
        WriteIndented = false
    };

    private readonly object _gate = new();
    private readonly string _localAppData;
    private readonly string _testRunsRoot;
    private readonly string _runDirectory;
    private readonly string _storePath;
    private readonly string _leasePath;
    private readonly string _temporaryPath;
    private readonly string _externalAccountStorePath;
    private readonly string _recoveryDirectory;
    private FileStream? _lease;
    private List<MZP3TestProfile> _profiles = new();
    private Guid? _selectedProfileId;
    private byte[]? _diskDigest;
    private long _revision;
    private MZP3ServiceState _state = MZP3ServiceState.Ready;
    private bool _dirty;
    private bool _disposed;

    private MZP3TestAccountService(string runId)
    {
        _localAppData = Path.GetFullPath(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData));
        _testRunsRoot = Path.Combine(_localAppData, "GivreReplica", "TestRuns");
        _runDirectory = Path.Combine(_testRunsRoot, runId);
        _storePath = Path.Combine(_runDirectory, "mzp3-test-profiles.json");
        _leasePath = _storePath + ".lck";
        _temporaryPath = _storePath + ".tmp";
        _externalAccountStorePath = Path.Combine(_runDirectory, "accounts.json");
        _recoveryDirectory = Path.Combine(_runDirectory, "recovery");
    }

    /// <summary>
    /// Opening is explicitly initiated by the test owner. The run key is a short
    /// opaque local identifier, NOT an account name or a path supplied by UI.
    /// Rejects non-Windows because strict Windows owner-only DACL is mandatory.
    /// </summary>
    public static MZP3TestAccountService Open(string testRunId, bool explicitOperatorApproval)
    {
        if (!explicitOperatorApproval) throw new MZP3StoreException(MZP3Failure.ApprovalRequired);
        if (!OperatingSystem.IsWindows()) throw new MZP3StoreException(MZP3Failure.UnsupportedPlatform);
        if (!IsRunKeyValid(testRunId)) throw new MZP3StoreException(MZP3Failure.InvalidRunId);

        var service = new MZP3TestAccountService(testRunId);
        try { service.Initialize(); return service; }
        catch { service.Dispose(); throw; }
    }

    public IReadOnlyList<MZP3TestProfile> Profiles
    {
        get { lock (_gate) { EnsureActive(); return _profiles.ToArray(); } }
    }

    public Guid? SelectedProfileId
    {
        get { lock (_gate) { EnsureActive(); return _selectedProfileId; } }
    }

    public MZP3Diagnostics Diagnostics
    {
        get
        {
            lock (_gate)
            {
                bool selected = _selectedProfileId.HasValue &&
                    _profiles.Any(p => p.Id == _selectedProfileId.Value);
                bool candidate = selected && _profiles.Single(p => p.Id == _selectedProfileId).IsOfflineCandidate;
                return new(_state, _profiles.Count, selected, candidate, _dirty,
                    _revision, _lease is not null && !_disposed,
                    _state == MZP3ServiceState.Ready && !_dirty && candidate);
            }
        }
    }

    public MZP3TestProfile CreateProfile(string alias)
    {
        lock (_gate)
        {
            EnsureActive();
            ValidateLabel(alias, 48);
            if (_profiles.Count >= MaxProfiles) throw new MZP3StoreException(MZP3Failure.TooManyProfiles);
            AssertUniqueAlias(alias, null);
            var profile = new MZP3TestProfile(Guid.NewGuid(), alias.Trim(), null, null, false);
            _profiles.Add(profile);
            _state = MZP3ServiceState.Dirty;
            _dirty = true;
            return profile;
        }
    }

    public void RenameProfile(Guid id, string alias)
    {
        lock (_gate)
        {
            EnsureActive(); ValidateLabel(alias, 48);
            int index = IndexFor(id); AssertUniqueAlias(alias, id);
            _profiles[index] = _profiles[index] with { Alias = alias.Trim() };
            MarkDirty();
        }
    }

    public void SelectProfile(Guid id)
    {
        lock (_gate)
        {
            EnsureActive(); IndexFor(id);
            if (_selectedProfileId == id) return;
            _selectedProfileId = id;
            MarkDirty();
        }
    }

    /// <summary>Candidate labels only. This cannot claim a server or character is real.</summary>
    public void SetOfflineCandidate(Guid id, string server, string character,
        bool explicitOfflineSelection)
    {
        lock (_gate)
        {
            EnsureActive();
            if (!explicitOfflineSelection)
                throw new MZP3StoreException(MZP3Failure.CandidateNotExplicit);
            ValidateLabel(server, 64); ValidateLabel(character, 64);
            int index = IndexFor(id);
            _profiles[index] = _profiles[index] with
            {
                ServerCandidate = server.Trim(), CharacterCandidate = character.Trim(),
                IsOfflineCandidate = true
            };
            MarkDirty();
        }
    }

    public void ClearOfflineCandidate(Guid id)
    {
        lock (_gate)
        {
            EnsureActive(); int index = IndexFor(id);
            _profiles[index] = _profiles[index] with
                { ServerCandidate = null, CharacterCandidate = null, IsOfflineCandidate = false };
            MarkDirty();
        }
    }

    public void RemoveProfile(Guid id)
    {
        lock (_gate)
        {
            EnsureActive(); _profiles.RemoveAt(IndexFor(id));
            if (_selectedProfileId == id) _selectedProfileId = null;
            MarkDirty();
        }
    }

    public void Save()
    {
        lock (_gate)
        {
            EnsureActive();
            if (!_dirty) return;
            if (_revision == long.MaxValue) throw new MZP3StoreException(MZP3Failure.IllegalData);
            var next = new MZP3StoreBody(FormatVersion, _revision + 1,
                _selectedProfileId, _profiles.ToList());
            WriteSnapshot(next);
            _revision = next.Revision;
            _dirty = false;
            _state = MZP3ServiceState.Ready;
        }
    }

    /// <summary>Never silently discards UI edits or restores an invalid/migrated file.</summary>
    public void Reload()
    {
        lock (_gate)
        {
            EnsureActive();
            if (_dirty) throw new MZP3StoreException(MZP3Failure.UnsavedChanges);
            try { ReadSnapshot(); }
            catch { _state = MZP3ServiceState.Faulted; throw; }
        }
    }

    public void Stop()
    {
        lock (_gate)
        {
            if (_state == MZP3ServiceState.Stopped) return;
            EnsureActive();
            if (_dirty) throw new MZP3StoreException(MZP3Failure.UnsavedChanges);
            _lease?.Dispose();
            _lease = null;
            _state = MZP3ServiceState.Stopped;
        }
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            _lease?.Dispose();
            _lease = null;
            _disposed = true;
            _state = MZP3ServiceState.Stopped;
            _profiles.Clear();
            _selectedProfileId = null;
            _diskDigest = null;
        }
    }

    private static bool IsRunKeyValid(string? key)
    {
        if (key is null || key.Length is < 4 or > 40 || !char.IsAsciiLetterOrDigit(key[0]))
            return false;
        return key.All(c => char.IsAsciiLetterOrDigit(c) || c is '-' or '_');
    }

    private void Initialize()
    {
        EnsurePathsAndPermissions(createRun: true);
        bool existed = File.Exists(_leasePath);
        CheckNoReparse(_leasePath);
        try
        {
            _lease = new FileStream(_leasePath, FileMode.OpenOrCreate, FileAccess.ReadWrite,
                FileShare.None);
        }
        catch (IOException) { throw new MZP3StoreException(MZP3Failure.ConcurrentOwner); }
        catch (UnauthorizedAccessException) { throw new MZP3StoreException(MZP3Failure.UnsafePermissions); }
        EnsurePathsAndPermissions();
        if (File.Exists(_temporaryPath)) throw new MZP3StoreException(MZP3Failure.InterruptedWrite);
        if (!File.Exists(_storePath))
        {
            if (existed) throw new MZP3StoreException(MZP3Failure.MissingOrCorruptStore);
            WriteSnapshot(new MZP3StoreBody(FormatVersion, 0, null, new()));
        }
        ReadSnapshot();
    }

    private void ReadSnapshot()
    {
        EnsurePathsAndPermissions();
        if (File.Exists(_temporaryPath)) throw new MZP3StoreException(MZP3Failure.InterruptedWrite);
        byte[] bytes = ReadBoundedStore();
        MZP3StoreEnvelope? envelope;
        try { envelope = JsonSerializer.Deserialize<MZP3StoreEnvelope>(bytes, JsonOptions); }
        catch (JsonException) { throw new MZP3StoreException(MZP3Failure.MissingOrCorruptStore); }
        if (envelope?.Body is null || envelope.Sha256 is null)
            throw new MZP3StoreException(MZP3Failure.MissingOrCorruptStore);
        if (envelope.Body.FormatVersion != FormatVersion)
            throw new MZP3StoreException(MZP3Failure.UnsupportedFormat);
        if (envelope.Sha256.Length != 64 || !envelope.Sha256.All(Uri.IsHexDigit))
            throw new MZP3StoreException(MZP3Failure.MissingOrCorruptStore);
        var expected = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(envelope.Body, JsonOptions));
        if (!CryptographicOperations.FixedTimeEquals(expected,
                Convert.FromHexString(envelope.Sha256)))
            throw new MZP3StoreException(MZP3Failure.MissingOrCorruptStore);
        ValidateBody(envelope.Body);
        _profiles = envelope.Body.Profiles.ToList();
        _selectedProfileId = envelope.Body.SelectedProfileId;
        _revision = envelope.Body.Revision;
        _diskDigest = SHA256.HashData(bytes);
        _dirty = false;
        _state = MZP3ServiceState.Ready;
    }

    private void WriteSnapshot(MZP3StoreBody body)
    {
        EnsurePathsAndPermissions();
        if (File.Exists(_temporaryPath)) throw new MZP3StoreException(MZP3Failure.InterruptedWrite);
        if (_diskDigest is not null &&
            !CryptographicOperations.FixedTimeEquals(SHA256.HashData(ReadBoundedStore()), _diskDigest))
            throw new MZP3StoreException(MZP3Failure.MissingOrCorruptStore);
        ValidateBody(body);
        byte[] digest = SHA256.HashData(JsonSerializer.SerializeToUtf8Bytes(body, JsonOptions));
        byte[] serialized = JsonSerializer.SerializeToUtf8Bytes(
            new MZP3StoreEnvelope(body, Convert.ToHexString(digest)), JsonOptions);
        if (serialized.Length > MaxFileBytes) throw new MZP3StoreException(MZP3Failure.IllegalData);
        try
        {
            using (var f = new FileStream(_temporaryPath, FileMode.CreateNew, FileAccess.Write,
                       FileShare.None, 4096, FileOptions.WriteThrough))
            {
                f.Write(serialized);
                f.Flush(flushToDisk: true);
            }
            CheckFileAcl(_temporaryPath);
            File.Move(_temporaryPath, _storePath, overwrite: true);
            CheckFileAcl(_storePath);
            _diskDigest = SHA256.HashData(serialized);
        }
        catch (MZP3StoreException) { _state = MZP3ServiceState.Faulted; throw; }
        catch { _state = MZP3ServiceState.Faulted; throw new MZP3StoreException(MZP3Failure.IoUnavailable); }
    }

    private byte[] ReadBoundedStore()
    {
        CheckNoReparse(_storePath);
        if (!File.Exists(_storePath)) throw new MZP3StoreException(MZP3Failure.MissingOrCorruptStore);
        CheckFileAcl(_storePath);
        using var stream = new FileStream(_storePath, FileMode.Open, FileAccess.Read, FileShare.Read);
        if (stream.Length is < 20 or > MaxFileBytes)
            throw new MZP3StoreException(MZP3Failure.MissingOrCorruptStore);
        using var ms = new MemoryStream();
        stream.CopyTo(ms);
        if (ms.Length > MaxFileBytes) throw new MZP3StoreException(MZP3Failure.MissingOrCorruptStore);
        return ms.ToArray();
    }

    private void EnsurePathsAndPermissions(bool createRun = false)
    {
        CheckDirectoryChain(_localAppData, _testRunsRoot);
        if (createRun)
        {
            Directory.CreateDirectory(_testRunsRoot);
            CheckDirectoryChain(_localAppData, _testRunsRoot);
            if (!Directory.Exists(_runDirectory))
                FileSystemAclExtensions.CreateDirectory(PrivateDirectoryAcl(), _runDirectory);
        }
        CheckDirectoryChain(_testRunsRoot, _runDirectory);
        if (!Directory.Exists(_runDirectory)) throw new MZP3StoreException(MZP3Failure.UnsafePath);
        CheckPrivateDirectoryAcl();
        CheckNoReparse(_storePath);
        CheckNoReparse(_leasePath);
        CheckNoReparse(_temporaryPath);
        CheckNoReparse(_externalAccountStorePath); // never read/write this MZP2-only account store
    }

    private static void CheckDirectoryChain(string anchor, string end)
    {
        if (!end.StartsWith(anchor + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase)
            && !string.Equals(anchor, end, StringComparison.OrdinalIgnoreCase))
            throw new MZP3StoreException(MZP3Failure.UnsafePath);
        for (string? segment = end; segment is not null &&
             segment.Length >= anchor.Length; segment = Path.GetDirectoryName(segment))
        {
            CheckNoReparse(segment);
            if (string.Equals(segment, anchor, StringComparison.OrdinalIgnoreCase)) break;
        }
    }

    private static void CheckNoReparse(string path)
    {
        try
        {
            if ((File.GetAttributes(path) & FileAttributes.ReparsePoint) != 0)
                throw new MZP3StoreException(MZP3Failure.UnsafePath);
        }
        catch (FileNotFoundException) { }
        catch (DirectoryNotFoundException) { }
    }

    private static SecurityIdentifier CurrentUserSid()
        => WindowsIdentity.GetCurrent().User ??
           throw new MZP3StoreException(MZP3Failure.UnsafePermissions);

    private static DirectorySecurity PrivateDirectoryAcl()
    {
        var sid = CurrentUserSid();
        var acl = new DirectorySecurity();
        acl.SetAccessRuleProtection(isProtected: true, preserveInheritance: false);
        acl.SetOwner(sid);
        acl.AddAccessRule(new FileSystemAccessRule(sid, FileSystemRights.FullControl,
            InheritanceFlags.ContainerInherit | InheritanceFlags.ObjectInherit,
            PropagationFlags.None, AccessControlType.Allow));
        return acl;
    }

    private void CheckPrivateDirectoryAcl()
    {
        try
        {
            var security = FileSystemAclExtensions.GetAccessControl(new DirectoryInfo(_runDirectory));
            if (!security.AreAccessRulesProtected) throw new MZP3StoreException(MZP3Failure.UnsafePermissions);
            ValidateAccessRules(security);
        }
        catch (MZP3StoreException) { throw; }
        catch { throw new MZP3StoreException(MZP3Failure.UnsafePermissions); }
    }

    private static void CheckFileAcl(string path)
    {
        try
        {
            if (!File.Exists(path)) return;
            ValidateAccessRules(FileSystemAclExtensions.GetAccessControl(new FileInfo(path)));
        }
        catch (MZP3StoreException) { throw; }
        catch { throw new MZP3StoreException(MZP3Failure.UnsafePermissions); }
    }

    private static void ValidateAccessRules(FileSystemSecurity security)
    {
        var current = CurrentUserSid();
        if (!current.Equals(security.GetOwner(typeof(SecurityIdentifier))))
            throw new MZP3StoreException(MZP3Failure.UnsafePermissions);
        var rules = security.GetAccessRules(true, true, typeof(SecurityIdentifier));
        foreach (FileSystemAccessRule rule in rules)
        {
            // Only the current local user may read or modify test-profile metadata.
            // A separately writable Everyone/Users or inherited ACL is never trusted.
            if (rule.AccessControlType == AccessControlType.Allow &&
                !current.Equals(rule.IdentityReference))
                throw new MZP3StoreException(MZP3Failure.UnsafePermissions);
        }
    }

    private static void ValidateBody(MZP3StoreBody body)
    {
        if (body.Revision < 0 || body.Profiles is null || body.Profiles.Count > MaxProfiles)
            throw new MZP3StoreException(MZP3Failure.IllegalData);
        var ids = new HashSet<Guid>();
        var aliases = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        foreach (var p in body.Profiles)
        {
            if (p is null || p.Id == Guid.Empty || !ids.Add(p.Id))
                throw new MZP3StoreException(MZP3Failure.IllegalData);
            ValidateLabel(p.Alias, 48);
            if (!aliases.Add(p.Alias)) throw new MZP3StoreException(MZP3Failure.DuplicateAlias);
            if (p.IsOfflineCandidate)
            {
                ValidateLabel(p.ServerCandidate, 64);
                ValidateLabel(p.CharacterCandidate, 64);
            }
            else if (p.ServerCandidate is not null || p.CharacterCandidate is not null)
                throw new MZP3StoreException(MZP3Failure.IllegalData);
        }
        if (body.SelectedProfileId is { } selected && !ids.Contains(selected))
            throw new MZP3StoreException(MZP3Failure.IllegalData);
    }

    private static void ValidateLabel(string? value, int max)
    {
        if (string.IsNullOrWhiteSpace(value) || value.Length > max ||
            !string.Equals(value, value.Trim(), StringComparison.Ordinal) ||
            !value.All(c => char.IsLetterOrDigit(c) || c is ' ' or '-' or '_' or '.' or '\''))
            throw new MZP3StoreException(MZP3Failure.IllegalData);
    }

    private void AssertUniqueAlias(string alias, Guid? excluding)
    {
        if (_profiles.Any(p => p.Id != excluding && string.Equals(p.Alias, alias.Trim(),
                StringComparison.OrdinalIgnoreCase)))
            throw new MZP3StoreException(MZP3Failure.DuplicateAlias);
    }

    private int IndexFor(Guid id)
    {
        int index = _profiles.FindIndex(p => p.Id == id);
        if (index < 0) throw new MZP3StoreException(MZP3Failure.NotFound);
        return index;
    }

    private void MarkDirty() { _dirty = true; _state = MZP3ServiceState.Dirty; }

    private void EnsureActive()
    {
        if (_disposed || _state == MZP3ServiceState.Stopped)
            throw new MZP3StoreException(MZP3Failure.Disposed);
        if (_state == MZP3ServiceState.Faulted)
            throw new MZP3StoreException(MZP3Failure.MissingOrCorruptStore);
        if (_lease is null) throw new MZP3StoreException(MZP3Failure.ConcurrentOwner);
    }
}
