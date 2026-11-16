using System.Security.Cryptography;
using System.Text;
using GivreClone.Engine;
using GivreClone.Engine.Recovery;
using GivreClone.Protocol;
using GivreCore.Mitm;

namespace GivreClone.App.Services;

/// <summary>
/// Production boundary for one BotHostService instance. The default adapter owns a
/// NEW service; Stop must never be called against the running global GivreClone host.
/// </summary>
public interface IMZP2OwnedHostPort
{
    bool IsRunning { get; }
    /// <summary>
    /// Return true ONLY if these concrete catalog+sink ports are proven to belong
    /// to this precise, owned host instance and connection. Never attest a caller-
    /// supplied IFrameSink by type or name. Current BotHostService cannot prove it.
    /// </summary>
    bool CanBindProtectedEngine(MZP1OwnedClientPermit ownedClient,
        MZP0SessionIdentity identity, MZP2TrustedEnginePorts enginePorts,
        MZP2IsolatedTestHostConfiguration testConfiguration);
    void Start(MZP1OwnedClientPermit ownedClient,
        Func<CapturedMessage, MZP1GuardedEngineWiring> guardedFactory,
        MZP2IsolatedTestHostConfiguration testConfiguration);
    void Stop();
}

/// <summary>A dedicated BotHostService, never a reference to the application's live host.</summary>
public sealed class MZP2DedicatedBotHostPort : IMZP2OwnedHostPort
{
    private readonly BotHostService _service = new();
    public bool IsRunning => _service.IsRunning;
    // Legacy BotHostService.Start receives only (permit, callback, test configuration),
    // so it still cannot attest a caller-supplied IFrameSink or ProtocolCatalog.
    // StartBound now exposes genuine host-bound ports, but has NOT been integrated
    // with an independently proven source/positive-response authority in MZP2.
    // Keep the default denied until that trust boundary is genuinely connected.
    public bool CanBindProtectedEngine(MZP1OwnedClientPermit ownedClient,
        MZP0SessionIdentity identity, MZP2TrustedEnginePorts enginePorts,
        MZP2IsolatedTestHostConfiguration testConfiguration) => false;
    public void Start(MZP1OwnedClientPermit ownedClient,
        Func<CapturedMessage, MZP1GuardedEngineWiring> guardedFactory,
        MZP2IsolatedTestHostConfiguration testConfiguration)
        => _service.Start(ownedClient, guardedFactory, testConfiguration.TestRelayPort,
            testConfiguration.TestAccountStorePath);
    public void Stop() => _service.Stop();
}

/// <summary>
/// Dependencies supplied by an independently authenticated host/session integration.
/// These are real Engine interfaces. This type is NOT a provider and never invents
/// catalog, transport, PID birth or server response evidence.
/// </summary>
public sealed record MZP2TrustedEnginePorts(
    ProtocolCatalog Catalog,
    IFrameSink ConnectionBoundSink,
    IPacer Pacer,
    IMZP0AuthenticatedSessionSource IdentityAuthority,
    IMZP0PositiveResponseVerifier ResponseAuthority);

/// <summary>
/// Selected by the operator for this test run; never inferred from the production
/// GivreClone port or %APPDATA% account store. This only describes a location and
/// does not create or read any account file.
/// </summary>
public sealed record MZP2IsolatedTestHostConfiguration(int TestRelayPort,
    string TestAccountStorePath, bool ExplicitOperatorApproval);

/// <summary>
/// Future host-originated factory signature. Only BotHost/BotOrchestrator may supply
/// their own ProtocolCatalog and connection-bound IFrameSink. A caller-provided sink
/// does NOT prove ownership; this delegate alone grants no admission capability.
/// BotHostService.StartBound exposes the matching four-argument producer seam;
/// MZP2 does NOT activate it without genuine separate origin/response authorities.
/// </summary>
public delegate MZP1GuardedEngineWiring MZP2HostBoundSessionFactory(
    CapturedMessage captured, ProtocolCatalog catalogFromOwnedHost,
    IFrameSink connectionSinkFromOwnedHost, IPacer pacerFromOwnedHost);

/// <summary>
/// Requires an explicitly owned test-client permit and an absolute, dedicated journal
/// directory. No account, character, raw frame or credential is accepted or written.
/// A caller supplying a sink must prove it belongs to the same guarded host connection.
/// </summary>
public sealed record MZP2TestClientAdmissionRequest(
    bool ExplicitOperatorApproval,
    MZP1OwnedClientPermit OwnedClient,
    MZP2TrustedEnginePorts? TrustedPorts,
    string JournalDirectory,
    MZP2IsolatedTestHostConfiguration? IsolatedTestHost = null);

public enum MZP2AdmissionState
{
    Dormant,
    Denied,
    Starting,
    AwaitingOwnedCapture,
    SessionAdmitted,
    ReconciliationRequired,
    Faulted,
    Stopped
}

public enum MZP2AdmissionReason
{
    None,
    ExplicitApprovalMissing,
    PermitMissing,
    PermitMalformed,
    EnginePortsMissing,
    AuthoritiesNotIndependent,
    IdentityUnverified,
    IdentityMismatch,
    JournalLocationInvalid,
    JournalUnavailable,
    UnresolvedEarlierOperations,
    IsolatedTestHostApprovalMissing,
    IsolatedTestRelayPortInvalid,
    IsolatedTestAccountStoreInvalid,
    HostTransportBindingUnavailable,
    AlreadyRunning,
    CaptureMismatch,
    ProtectedEngineRejected,
    HostFailed,
    SessionRevoked
}

/// <summary>Sanitized onboarding/diagnostic state; does not contain paths, PIDs or payloads.</summary>
public sealed record MZP2AdmissionDiagnostics(
    MZP2AdmissionState State,
    MZP2AdmissionReason Reason,
    bool HostOwned,
    bool ProtectedSessionReady,
    int UnresolvedOperations,
    long JournalRevision,
    int? SelectedTestRelayPort = null,
    bool IsolatedAccountStoreSelected = false);

public sealed class MZP2AdmissionException(MZP2AdmissionReason reason)
    : InvalidOperationException($"Owned test-client admission refused ({reason}).")
{
    public MZP2AdmissionReason Reason { get; } = reason;
}

/// <summary>
/// An explicit onboarding coordinator, NOT an automatic watcher. It can start and stop
/// only its own dedicated BotHostService and refuses journal replay on restart. There
/// are no built-in live origin/response providers; callers must supply independent
/// trusted Engine ports. A missing source blocks BEFORE calling BotHostService.Start.
/// </summary>
public sealed class MZP2OwnedHostAdmission : IDisposable
{
    private readonly object _gate = new();
    private readonly IMZP2OwnedHostPort _host;
    private MZP2AdmissionState _state = MZP2AdmissionState.Dormant;
    private MZP2AdmissionReason _reason;
    private MZP0RecoveryJournal? _journal;
    private MZP2TrustedEnginePorts? _ports;
    private MZP1OwnedClientPermit? _permit;
    private MZP2IsolatedTestHostConfiguration? _testConfiguration;
    private MZP0SessionIdentity _expectedIdentity;
    private MZP1GuardedEngineWiring? _wiring;
    private bool _ownsHost;
    private bool _disposed;
    private int _lastUnresolved;
    private long _lastRevision;

    public MZP2OwnedHostAdmission() : this(new MZP2DedicatedBotHostPort()) { }

    /// <summary>
    /// Provided for composition with a dedicated test-host port; never pass the
    /// already-running GivreClone application service. Production defaults to its own.
    /// </summary>
    public MZP2OwnedHostAdmission(IMZP2OwnedHostPort ownedHost)
        => _host = ownedHost ?? throw new ArgumentNullException(nameof(ownedHost));

    public MZP2AdmissionDiagnostics Diagnostics
    {
        get
        {
            lock (_gate)
            {
                if (_wiring is not null && _state == MZP2AdmissionState.SessionAdmitted &&
                    _wiring.Admission != MZP1Admission.Ready)
                {
                    _state = MZP2AdmissionState.Faulted;
                    _reason = MZP2AdmissionReason.SessionRevoked;
                }
                if (_journal is not null)
                {
                    try { SnapshotJournal(); }
                    catch
                    {
                        _state = MZP2AdmissionState.Faulted;
                        _reason = MZP2AdmissionReason.JournalUnavailable;
                        _wiring?.Revoke();
                    }
                }
                return new MZP2AdmissionDiagnostics(_state, _reason, _ownsHost,
                    _state == MZP2AdmissionState.SessionAdmitted &&
                    _wiring?.Admission == MZP1Admission.Ready,
                    _lastUnresolved, _lastRevision,
                    _testConfiguration?.TestRelayPort, _testConfiguration is not null);
            }
        }
    }

    /// <summary>
    /// Only this method can start the new dedicated test host. It will not call
    /// Start() unless every dependency and independent session identity is valid
    /// and the durable journal has no previous unresolved operations.
    /// </summary>
    public MZP2AdmissionDiagnostics StartOwnedTestHost(MZP2TestClientAdmissionRequest request)
    {
        ArgumentNullException.ThrowIfNull(request);
        lock (_gate)
        {
            ObjectDisposedException.ThrowIf(_disposed, this);
            if (_ownsHost || _host.IsRunning ||
                _state is MZP2AdmissionState.Starting or MZP2AdmissionState.AwaitingOwnedCapture or
                    MZP2AdmissionState.SessionAdmitted)
                return _ownsHost
                    ? new MZP2AdmissionDiagnostics(_state, MZP2AdmissionReason.AlreadyRunning,
                        true, _state == MZP2AdmissionState.SessionAdmitted &&
                        _wiring?.Admission == MZP1Admission.Ready, _lastUnresolved, _lastRevision)
                    : Deny(MZP2AdmissionReason.AlreadyRunning);

            CloseOwnedResources();
            _reason = MZP2AdmissionReason.None;
            _state = MZP2AdmissionState.Dormant;
            _lastUnresolved = 0;
            _lastRevision = 0;

            var preflight = CheckRequest(request, out var identity);
            if (preflight != MZP2AdmissionReason.None) return Deny(preflight);
            // A caller-supplied sink cannot authenticate itself. StartBound exists
            // in BotHostService, but this port does not yet verify its origin and
            // independent positive-response proof. Never silently use legacy Start.
            bool hostBindingVerified;
            try
            {
                hostBindingVerified = _host.CanBindProtectedEngine(
                    request.OwnedClient, identity, request.TrustedPorts!, request.IsolatedTestHost!);
            }
            catch { hostBindingVerified = false; }
            if (!hostBindingVerified)
                return Deny(MZP2AdmissionReason.HostTransportBindingUnavailable);

            // Stable path per owner birth and connection, without account/character
            // names. A new PID generation cannot silently inherit another session.
            string journalPath;
            try { journalPath = ComputeJournalPath(request, identity); }
            catch { return Deny(MZP2AdmissionReason.JournalLocationInvalid); }

            try
            {
                _journal = new MZP0RecoveryJournal(journalPath);
                SnapshotJournal();
                if (_lastUnresolved != 0)
                {
                    _state = MZP2AdmissionState.ReconciliationRequired;
                    _reason = MZP2AdmissionReason.UnresolvedEarlierOperations;
                    CloseOwnedResources();
                    return Diagnostics;
                }
            }
            catch
            {
                CloseOwnedResources();
                return Deny(MZP2AdmissionReason.JournalUnavailable);
            }

            _permit = request.OwnedClient;
            _ports = request.TrustedPorts;
            _testConfiguration = request.IsolatedTestHost;
            _expectedIdentity = identity;
            _state = MZP2AdmissionState.Starting;
            try
            {
                // Owner port contract forwards the explicit isolated relay/store
                // selection; never parameterless Start or a default GivreClone store.
                _host.Start(_permit, AdmitCapturedSession, _testConfiguration!);
                _ownsHost = _host.IsRunning;
                if (!_ownsHost)
                {
                    CloseOwnedResources();
                    return Deny(MZP2AdmissionReason.HostFailed);
                }
                if (_state == MZP2AdmissionState.Starting)
                    _state = MZP2AdmissionState.AwaitingOwnedCapture;
            }
            catch
            {
                // If a host partly started then threw, stop ONLY this owned port.
                bool stillRunning = false;
                try
                {
                    stillRunning = _host.IsRunning;
                    if (stillRunning)
                    {
                        _ownsHost = true;
                        _host.Stop();
                        stillRunning = _host.IsRunning;
                    }
                }
                catch { stillRunning = true; }
                if (stillRunning)
                {
                    // Do NOT dispose the journal or claim release while the
                    // dedicated host may still be capturing. Explicit Stop must
                    // succeed later before this lease is released or reused.
                    _ownsHost = true;
                    _wiring?.Revoke();
                    _state = MZP2AdmissionState.Faulted;
                    _reason = MZP2AdmissionReason.HostFailed;
                    return Diagnostics;
                }
                _ownsHost = false;
                CloseOwnedResources();
                return Deny(MZP2AdmissionReason.HostFailed);
            }
            return Diagnostics;
        }
    }

    /// <summary>
    /// Safe, targeted stop: acts only after this instance started its private host.
    /// A rejected onboarding never invokes Stop on another owner's BotHostService.
    /// </summary>
    public MZP2AdmissionDiagnostics StopOwnedTestHost()
    {
        lock (_gate)
        {
            if (_ownsHost)
            {
                try { _host.Stop(); }
                catch
                {
                    _wiring?.Revoke();
                    _state = MZP2AdmissionState.Faulted;
                    _reason = MZP2AdmissionReason.HostFailed;
                    return Diagnostics; // do not release journal/permit while host may run
                }
                if (_host.IsRunning)
                {
                    _wiring?.Revoke();
                    _state = MZP2AdmissionState.Faulted;
                    _reason = MZP2AdmissionReason.HostFailed;
                    return Diagnostics;
                }
                _ownsHost = false;
            }
            CloseOwnedResources();
            _state = MZP2AdmissionState.Stopped;
            _reason = MZP2AdmissionReason.None;
            return Diagnostics;
        }
    }

    private MZP1GuardedEngineWiring AdmitCapturedSession(CapturedMessage captured)
    {
        lock (_gate)
        {
            if (_state is not (MZP2AdmissionState.Starting or
                    MZP2AdmissionState.AwaitingOwnedCapture) ||
                _wiring is not null || _journal is null || _ports is null || _permit is null)
                throw new MZP2AdmissionException(MZP2AdmissionReason.ProtectedEngineRejected);

            if (captured is null || captured.ConnectionId != _expectedIdentity.ConnectionId ||
                captured.ProcessId == 0 || captured.ProcessId != (uint)_permit.Pid ||
                !TryReauthenticate(_ports.IdentityAuthority, _expectedIdentity))
            {
                _state = MZP2AdmissionState.Faulted;
                _reason = MZP2AdmissionReason.CaptureMismatch;
                throw new MZP2AdmissionException(MZP2AdmissionReason.CaptureMismatch);
            }

            MZP1GuardedEngineWiring created;
            try
            {
                created = MZP1GuardedEngineWiringFactory.Create(
                    _expectedIdentity.ConnectionId, _permit.ClientBuild,
                    _ports.Catalog, _ports.ConnectionBoundSink, _ports.Pacer,
                    _journal, _ports.IdentityAuthority, _ports.ResponseAuthority);
                if (created.Admission != MZP1Admission.Ready)
                {
                    created.Revoke();
                    throw new MZP2AdmissionException(MZP2AdmissionReason.ProtectedEngineRejected);
                }
                // This is an authenticated Engine session, not proof of a real ACK.
                _wiring = created;
                _state = MZP2AdmissionState.SessionAdmitted;
                SnapshotJournal();
                return created;
            }
            catch
            {
                _state = MZP2AdmissionState.Faulted;
                _reason = MZP2AdmissionReason.ProtectedEngineRejected;
                throw new MZP2AdmissionException(MZP2AdmissionReason.ProtectedEngineRejected);
            }
        }
    }

    private static MZP2AdmissionReason CheckRequest(MZP2TestClientAdmissionRequest request,
        out MZP0SessionIdentity identity)
    {
        identity = default;
        if (!request.ExplicitOperatorApproval) return MZP2AdmissionReason.ExplicitApprovalMissing;
        if (request.OwnedClient is null) return MZP2AdmissionReason.PermitMissing;
        if (!request.OwnedClient.IsWellFormed) return MZP2AdmissionReason.PermitMalformed;
        if (request.TrustedPorts is not { Catalog: not null, ConnectionBoundSink: not null,
            Pacer: not null, IdentityAuthority: not null, ResponseAuthority: not null } ports)
            return MZP2AdmissionReason.EnginePortsMissing;
        if (ReferenceEquals(ports.IdentityAuthority, ports.ResponseAuthority))
            return MZP2AdmissionReason.AuthoritiesNotIndependent;
        if (!TryGetIdentity(ports.IdentityAuthority, out identity))
            return MZP2AdmissionReason.IdentityUnverified;
        if (identity.ProcessId != request.OwnedClient.Pid ||
            identity.ProcessStartUtcTicks != request.OwnedClient.ProcessStartUtcTicks ||
            identity.ConnectionId <= 0 ||
            !string.Equals(identity.ProtocolVersion, request.OwnedClient.ClientBuild, StringComparison.Ordinal))
            return MZP2AdmissionReason.IdentityMismatch;
        if (!IsValidDedicatedDirectory(request.JournalDirectory))
            return MZP2AdmissionReason.JournalLocationInvalid;
        if (request.IsolatedTestHost?.ExplicitOperatorApproval != true)
            return MZP2AdmissionReason.IsolatedTestHostApprovalMissing;
        if (request.IsolatedTestHost?.TestRelayPort is not (>= 1024 and <= 65535) or 5657 or 5658)
            return MZP2AdmissionReason.IsolatedTestRelayPortInvalid;
        if (!IsValidIsolatedTestAccountStore(request.IsolatedTestHost.TestAccountStorePath))
            return MZP2AdmissionReason.IsolatedTestAccountStoreInvalid;
        return MZP2AdmissionReason.None;
    }

    /// <summary>
    /// Mirrors BotHostService's isolation gate, without reading account contents.
    /// A later host-level check remains authoritative and can still reject the path.
    /// </summary>
    private static bool IsValidIsolatedTestAccountStore(string? candidate)
    {
        if (string.IsNullOrWhiteSpace(candidate) || !Path.IsPathFullyQualified(candidate)) return false;
        try
        {
            var root = Path.GetFullPath(Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                "GivreReplica", "TestRuns"));
            var file = Path.GetFullPath(candidate);
            if (!file.StartsWith(root + Path.DirectorySeparatorChar,
                    StringComparison.OrdinalIgnoreCase) ||
                !file.EndsWith(".json", StringComparison.OrdinalIgnoreCase) ||
                string.Equals(file, root, StringComparison.OrdinalIgnoreCase))
                return false;
            for (var dir = new DirectoryInfo(Path.GetDirectoryName(file)!);
                 dir is not null; dir = dir.Parent)
                if (dir.Exists && dir.Attributes.HasFlag(FileAttributes.ReparsePoint))
                    return false;
            if (File.Exists(file) && File.GetAttributes(file).HasFlag(FileAttributes.ReparsePoint))
                return false;
            return true;
        }
        catch { return false; }
    }

    private static bool TryGetIdentity(IMZP0AuthenticatedSessionSource source,
        out MZP0SessionIdentity identity)
    {
        identity = default;
        try { return source.TryGetAuthenticatedIdentity(out identity); }
        catch { return false; }
    }

    private static bool TryReauthenticate(IMZP0AuthenticatedSessionSource source,
        MZP0SessionIdentity expected)
        => TryGetIdentity(source, out var current) && current == expected;

    private static bool IsValidDedicatedDirectory(string? directory)
    {
        if (string.IsNullOrWhiteSpace(directory) || !Path.IsPathFullyQualified(directory))
            return false;
        try
        {
            string full = Path.GetFullPath(directory);
            string root = Path.GetPathRoot(full)!;
            return !string.Equals(full.TrimEnd(Path.DirectorySeparatorChar),
                root.TrimEnd(Path.DirectorySeparatorChar), StringComparison.OrdinalIgnoreCase);
        }
        catch { return false; }
    }

    private static string ComputeJournalPath(MZP2TestClientAdmissionRequest request,
        MZP0SessionIdentity identity)
    {
        string scope = string.Concat(request.OwnedClient.Pid, ":",
            request.OwnedClient.ProcessStartUtcTicks, ":", identity.ConnectionId, ":",
            request.OwnedClient.ClientBuild, ":", request.OwnedClient.ExecutableSha256);
        byte[] hash = SHA256.HashData(Encoding.UTF8.GetBytes(scope));
        return Path.Combine(Path.GetFullPath(request.JournalDirectory),
            "mzp2-" + Convert.ToHexString(hash) + ".journal.json");
    }

    private void SnapshotJournal()
    {
        if (_journal is null) return;
        _lastRevision = _journal.Revision;
        _lastUnresolved = _journal.Records.Count(r => r.State is
            MZP0DeliveryState.Prepared or MZP0DeliveryState.DispatchWindowOpened or
            MZP0DeliveryState.Uncertain);
    }

    private MZP2AdmissionDiagnostics Deny(MZP2AdmissionReason reason)
    {
        _state = MZP2AdmissionState.Denied;
        _reason = reason;
        return new MZP2AdmissionDiagnostics(_state, _reason, _ownsHost,
            false, _lastUnresolved, _lastRevision);
    }

    private void CloseOwnedResources()
    {
        _wiring?.Revoke();
        _wiring = null;
        if (_journal is not null)
        {
            try { SnapshotJournal(); } catch { }
            _journal.Dispose();
            _journal = null;
        }
        _ports = null;
        _permit = null;
        _testConfiguration = null;
        _expectedIdentity = default;
    }

    public void Dispose()
    {
        lock (_gate)
        {
            if (_disposed) return;
            // Stop attempts only this coordinator's host; if Stop fails, retain
            // the journal lease and do not silently claim a safe shutdown.
            var result = StopOwnedTestHost();
            if (result.HostOwned) throw new InvalidOperationException("Owned test host could not be stopped.");
            _disposed = true;
        }
    }
}
