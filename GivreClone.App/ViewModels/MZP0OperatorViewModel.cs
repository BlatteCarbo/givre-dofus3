using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.Linq;
using System.Text.RegularExpressions;
using System.Threading;
using Avalonia.Media;
using Avalonia.Threading;

namespace GivreClone.App.ViewModels;

public enum MZP0JournalOutcome { Attributed, Rejected, System }

/// <summary>One metadata-only line. No server payload or account data is kept in the journal.</summary>
public sealed record MZP0OperatorJournalLine(
    DateTimeOffset UtcTime, MZP0JournalOutcome Outcome, string Code, string Detail)
{
    private static readonly IBrush Green = new SolidColorBrush(Color.Parse("#6BCDA5"));
    private static readonly IBrush Amber = new SolidColorBrush(Color.Parse("#E6B65C"));
    private static readonly IBrush Blue = new SolidColorBrush(Color.Parse("#86A9E6"));

    public string Time => UtcTime.LocalDateTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    public string Label => Outcome switch
    {
        MZP0JournalOutcome.Attributed => "ATTRIBUÉ",
        MZP0JournalOutcome.Rejected => "REFUSÉ",
        _ => "SYSTÈME",
    };
    public IBrush Accent => Outcome switch
    {
        MZP0JournalOutcome.Attributed => Green,
        MZP0JournalOutcome.Rejected => Amber,
        _ => Blue,
    };
}

/// <summary>Erreurs identiques (même code + même diagnostic) regroupées : nombre d'occurrences, première et dernière.</summary>
public sealed class MZP0ErrorGroup : CommunityToolkit.Mvvm.ComponentModel.ObservableObject
{
    public MZP0ErrorGroup(string code, string detail, DateTimeOffset firstUtc)
    {
        Code = code; Detail = detail; FirstUtc = firstUtc; LastUtc = firstUtc;
    }

    public string Code { get; }
    public string Detail { get; }
    public DateTimeOffset FirstUtc { get; }
    public DateTimeOffset LastUtc { get; private set; }
    public int Count { get; private set; } = 1;
    public string CountText => "×" + Count.ToString(CultureInfo.InvariantCulture);
    public string FirstText => FirstUtc.LocalDateTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture);
    public string LastText => LastUtc.LocalDateTime.ToString("HH:mm:ss", CultureInfo.InvariantCulture);

    internal void Add(DateTimeOffset atUtc)
    {
        Count++;
        if (atUtc > LastUtc) LastUtc = atUtc;
        OnPropertyChanged(nameof(Count)); OnPropertyChanged(nameof(CountText));
        OnPropertyChanged(nameof(LastUtc)); OnPropertyChanged(nameof(LastText));
    }
}

/// <summary>
/// Read-only operator panel. Identity is pinned by the caller and verified independently for
/// every observation. A claimed message, UID or event is never enough to promote an ACK.
/// Event handlers post UI mutations to Avalonia's dispatcher; no I/O, injection or game calls.
/// </summary>
public sealed class MZP0OperatorViewModel : ViewModelBase, IDisposable
{
    private const int MaxJournalLines = 200;
    private const int MaxQueuedUiSignals = 128;
    private const int MaxTokenLength = 128;
    private static readonly TimeSpan MaxObservationAge = TimeSpan.FromSeconds(15);
    private static readonly TimeSpan MaxProofHorizon = TimeSpan.FromSeconds(30);
    private static readonly Regex Sha256Pattern = new("^[0-9a-fA-F]{64}$", RegexOptions.CultureInvariant);
    private static readonly Regex BuildPattern = new(@"^[0-9]+(?:\.[0-9]+){2,3}$", RegexOptions.CultureInvariant);

    private readonly List<MZP0OperatorJournalLine> _history = new();
    private IMZP0OperatorObservationSource? _source;
    private EventHandler<MZP0ObservationEventArgs>? _sourceHandler;
    private EventHandler? _unavailableHandler;
    private long _subscriptionEpoch;
    private int _pendingUiSignals;
    private int _overloadNoticeQueued;
    private IMZP0OperatorObservationAuthority? _authority;
    private MZP0SessionIdentity? _pinned;
    private string? _approvedSource;
    private DateTimeOffset _validUntilUtc = DateTimeOffset.MinValue;
    private long _lastSequence;
    private MZP0SessionIdentity? _lastAcceptedIdentity;
    private string? _lastAcceptedSource;
    private bool _disposed;

    public ObservableCollection<MZP0OperatorJournalLine> VisibleJournal { get; } = new();

    /// <summary>Refus regroupés (le plus récent en tête). Comptés indépendamment du plafond du journal : aucune occurrence
    /// n'est perdue quand les lignes les plus anciennes sortent du journal.</summary>
    public ObservableCollection<MZP0ErrorGroup> ErrorGroups { get; } = new();
    private readonly Dictionary<(string Code, string Detail), MZP0ErrorGroup> _errorGroups = new();
    public bool HasErrorGroups => ErrorGroups.Count > 0;
    public int ErrorOccurrenceCount => _errorGroups.Values.Sum(g => g.Count);

    private string _sessionStatus = "NON ATTESTÉE";
    public string SessionStatus { get => _sessionStatus; private set => SetProperty(ref _sessionStatus, value); }

    private string _sessionDetail = "Aucune source vérifiée n'est raccordée.";
    public string SessionDetail { get => _sessionDetail; private set => SetProperty(ref _sessionDetail, value); }

    private string _evidenceStatus = "AUCUNE OBSERVATION";
    public string EvidenceStatus { get => _evidenceStatus; private set => SetProperty(ref _evidenceStatus, value); }

    private string _evidenceDetail = "En attente d'une observation attribuée.";
    public string EvidenceDetail { get => _evidenceDetail; private set => SetProperty(ref _evidenceDetail, value); }

    private string _ackStatus = "INCONNU";
    public string AckStatus { get => _ackStatus; private set => SetProperty(ref _ackStatus, value); }

    private string _ackDetail = "Aucune réponse serveur corrélée et attestée.";
    public string AckDetail { get => _ackDetail; private set => SetProperty(ref _ackDetail, value); }

    private string _diagnostic = "Source et vérificateur indépendants requis.";
    public string Diagnostic { get => _diagnostic; private set => SetProperty(ref _diagnostic, value); }

    private string _buildLabel = "CLIENT INCONNU";
    public string BuildLabel { get => _buildLabel; private set => SetProperty(ref _buildLabel, value); }

    private string _lastObservation = "Aucune";
    public string LastObservation { get => _lastObservation; private set => SetProperty(ref _lastObservation, value); }

    private int _attributedCount;
    public int AttributedCount { get => _attributedCount; private set => SetProperty(ref _attributedCount, value); }

    private int _rejectedCount;
    public int RejectedCount { get => _rejectedCount; private set => SetProperty(ref _rejectedCount, value); }

    private string _searchText = string.Empty;
    public string SearchText
    {
        get => _searchText;
        set { if (SetProperty(ref _searchText, value ?? string.Empty)) ApplyFilter(); }
    }

    private int _journalFilterIndex;
    /// <summary>0 all, 1 attributed, 2 refused, 3 system.</summary>
    public int JournalFilterIndex
    {
        get => _journalFilterIndex;
        set { if (SetProperty(ref _journalFilterIndex, Math.Clamp(value, 0, 3))) ApplyFilter(); }
    }

    private bool _hasVisibleJournal;
    public bool HasVisibleJournal { get => _hasVisibleJournal; private set => SetProperty(ref _hasVisibleJournal, value); }

    public int JournalLimit => MaxJournalLines;

    /// <summary>
    /// Explicit composition-root handoff. `authority` must be independent of `source`, backed by
    /// the host's real provenance and correlation evidence, and must not trust candidate flags.
    /// Providing both interfaces is necessary, not proof in itself.
    /// </summary>
    public void Bind(IMZP0OperatorObservationSource source, IMZP0OperatorObservationAuthority authority,
        MZP0SessionIdentity pinnedIdentity, string approvedSourceId)
    {
        ObjectDisposedException.ThrowIf(_disposed, this);
        ArgumentNullException.ThrowIfNull(source);
        ArgumentNullException.ThrowIfNull(authority);
        UnbindCore();
        ResetObservedState("En attente de vérification de la source.");
        BuildLabel = "CLIENT INCONNU";

        // A rebind to the same PID/birth/generation/source must not replay older
        // observations simply because the UI page was temporarily detached.
        if (pinnedIdentity != _lastAcceptedIdentity || approvedSourceId != _lastAcceptedSource)
            _lastSequence = 0;

        if (ReferenceEquals(source, authority))
        {
            Reject("SELF_ATTESTATION", "Producteur et autorité doivent être distincts.");
            return;
        }
        if (!ValidIdentity(pinnedIdentity) || !ValidToken(approvedSourceId))
        {
            Reject("INVALID_PIN", "Identité de session ou provenance non épinglée.");
            return;
        }

        _source = source;
        _authority = authority;
        _pinned = pinnedIdentity;
        _approvedSource = approvedSourceId;
        BuildLabel = $"CLIENT {pinnedIdentity.ClientBuild} · PIN LOCAL";
        SessionDetail = "Source fournie ; attribution encore inconnue.";
        Diagnostic = "Vérification indépendante requise pour chaque événement.";
        long epoch = Interlocked.Read(ref _subscriptionEpoch);
        _sourceHandler = (_, args) => OnReceived(epoch, args);
        _unavailableHandler = (_, _) => OnUnavailable(epoch);
        _source.Received += _sourceHandler;
        _source.Unavailable += _unavailableHandler;
        Journal(MZP0JournalOutcome.System, "SOURCE_BOUND", "Port fourni ; aucune session attestée.");
    }

    /// <summary>Explicit loss notification from the hosting integration or view lifecycle.</summary>
    public void Unbind()
    {
        if (_disposed) return;
        UnbindCore();
        ResetObservedState("Port détaché ; état et ACK redevenus inconnus.");
        BuildLabel = "CLIENT INCONNU";
        Journal(MZP0JournalOutcome.System, "SOURCE_UNBOUND", "Source indisponible.");
    }

    /// <summary>Called by view's lightweight UI expiry timer; does not inspect processes.</summary>
    public void RecheckExpiry()
        => RecheckExpiryAt(DateTimeOffset.UtcNow);

    /// <summary>Deterministic expiry seam for unit tests; no process access.</summary>
    internal void RecheckExpiryAt(DateTimeOffset nowUtc)
    {
        if (_disposed || _validUntilUtc == DateTimeOffset.MinValue || nowUtc < _validUntilUtc)
            return;
        ResetObservedState("Preuve expirée ; réattribution nécessaire.");
        Journal(MZP0JournalOutcome.System, "PROOF_EXPIRED", "Session / ACK invalidés à expiration.");
    }

    /// <summary>UI-thread ingestion; strictly refuses missing, stale, mismatched or invented authority.</summary>
    public bool Accept(MZP0CandidateObservation? candidate)
    {
        if (_disposed) return false;
        RecheckExpiry();
        if (_source is null || _authority is null || _pinned is null || _approvedSource is null)
        {
            Reject("NO_SOURCE", "Source et autorité indépendantes indisponibles.");
            return false;
        }

        var now = DateTimeOffset.UtcNow;
        if (candidate is null || candidate.Identity != _pinned || candidate.SourceId != _approvedSource)
        {
            RejectAndInvalidate("IDENTITY_MISMATCH", "PID, naissance, génération, build, SHA ou origine divergents.");
            return false;
        }
        if (!ValidIdentity(candidate.Identity) || !ValidToken(candidate.ObservationId)
            || !ValidToken(candidate.SourceId) || candidate.Sequence <= 0
            || candidate.Sequence <= _lastSequence || !ValidOptionalToken(candidate.CorrelationId))
        {
            Reject("STALE_OR_INVALID", "Identifiant, ordre ou séquence non recevable.");
            return false;
        }
        if (candidate.ObservedAtUtc > now.AddSeconds(2) || now - candidate.ObservedAtUtc > MaxObservationAge)
        {
            RejectAndInvalidate("OBSERVATION_EXPIRED", "Observation hors de la fenêtre de validité.");
            return false;
        }
        if (candidate.Direction is MZP0Direction.Unknown || candidate.Kind is MZP0SignalKind.Unclassified)
        {
            Reject("UNKNOWN_SEMANTICS", "Direction ou catégorie non prouvée.");
            return false;
        }

        MZP0AuthorityProof? proof;
        try
        {
            if (!_authority.IsCurrentOwnedSession(_pinned))
            {
                RejectAndInvalidate("OWNERSHIP_LOST", "Session propriétaire non attestée par le host.");
                return false;
            }
            proof = _authority.Verify(candidate);
        }
        catch (Exception)
        {
            RejectAndInvalidate("AUTHORITY_UNAVAILABLE", "Vérificateur d'attribution indisponible.");
            return false;
        }
        if (proof is null || proof.Identity != _pinned || proof.SourceId != candidate.SourceId
            || proof.ObservationId != candidate.ObservationId || proof.Sequence != candidate.Sequence
            || proof.ObservedAtUtc != candidate.ObservedAtUtc || proof.Direction != candidate.Direction
            || proof.Kind != candidate.Kind || proof.CorrelationId != candidate.CorrelationId
            || proof.ExpiresAtUtc <= now || proof.ExpiresAtUtc > now + MaxProofHorizon
            || proof.ExpiresAtUtc < proof.ObservedAtUtc)
        {
            RejectAndInvalidate("PROOF_MISMATCH", "Preuve indépendante absente, incohérente ou expirée.");
            return false;
        }
        if (proof.IsCorrelatedServerAck && (candidate.Kind != MZP0SignalKind.ServerResponse
            || candidate.Direction != MZP0Direction.ServerToClient
            || string.IsNullOrWhiteSpace(candidate.CorrelationId)))
        {
            RejectAndInvalidate("ACK_WITHOUT_CORRELATION", "ACK refusé : réponse entrante corrélée non démontrée.");
            return false;
        }

        _lastSequence = candidate.Sequence;
        _lastAcceptedIdentity = _pinned;
        _lastAcceptedSource = _approvedSource;
        _validUntilUtc = proof.ExpiresAtUtc;
        AttributedCount++;
        SessionStatus = "SOURCE ATTRIBUÉE";
        SessionDetail = "PID + naissance + génération + SHA validés par autorité externe.";
        EvidenceStatus = "OBSERVATION ATTRIBUÉE";
        EvidenceDetail = $"{SignalLabel(candidate.Kind)} · {DirectionLabel(candidate.Direction)}";
        LastObservation = now.ToLocalTime().ToString("HH:mm:ss", CultureInfo.InvariantCulture);
        Diagnostic = "Dernière observation vérifiée ; validité temporaire.";

        if (proof.IsCorrelatedServerAck)
        {
            AckStatus = "ACK CORRÉLÉ";
            AckDetail = "Preuve serveur entrante corrélée fournie par l'autorité externe.";
        }
        else
        {
            AckStatus = "INCONNU";
            AckDetail = "Aucune attestation d'ACK pour cette observation.";
        }
        Journal(MZP0JournalOutcome.Attributed, "OBSERVATION", EvidenceDetail);
        return true;
    }

    private void OnReceived(long epoch, MZP0ObservationEventArgs args)
    {
        if (_disposed || epoch != Interlocked.Read(ref _subscriptionEpoch)) return;
        void Consume()
        {
            if (epoch == Interlocked.Read(ref _subscriptionEpoch)) Accept(args.Candidate);
        }
        if (Dispatcher.UIThread.CheckAccess()) { Consume(); return; }
        if (Interlocked.Increment(ref _pendingUiSignals) > MaxQueuedUiSignals)
        {
            Interlocked.Decrement(ref _pendingUiSignals);
            if (Interlocked.Exchange(ref _overloadNoticeQueued, 1) == 0)
                Dispatcher.UIThread.Post(() =>
                {
                    try
                    {
                        if (epoch == Interlocked.Read(ref _subscriptionEpoch))
                            RejectAndInvalidate("INGRESS_OVERFLOW", "Surcharge UI : flux non attribuable ; état invalidé.");
                    }
                    finally { Interlocked.Exchange(ref _overloadNoticeQueued, 0); }
                });
            return;
        }
        Dispatcher.UIThread.Post(() =>
        {
            try { Consume(); }
            finally { Interlocked.Decrement(ref _pendingUiSignals); }
        });
    }

    private void OnUnavailable(long epoch)
    {
        if (_disposed || epoch != Interlocked.Read(ref _subscriptionEpoch)) return;
        void Detach()
        {
            if (epoch == Interlocked.Read(ref _subscriptionEpoch)) Unbind();
        }
        if (Dispatcher.UIThread.CheckAccess()) Detach();
        else Dispatcher.UIThread.Post(Detach);
    }

    private static bool ValidIdentity(MZP0SessionIdentity? identity)
        => identity is not null && ValidToken(identity.SessionId)
           && identity.ProcessId > 0 && identity.Generation > 0
           && identity.ProcessBirthUtc > DateTimeOffset.UnixEpoch
           && identity.ProcessBirthUtc <= DateTimeOffset.UtcNow
           && !string.IsNullOrEmpty(identity.ClientBuild)
           && BuildPattern.IsMatch(identity.ClientBuild)
           && !string.IsNullOrEmpty(identity.ClientSha256)
           && Sha256Pattern.IsMatch(identity.ClientSha256);

    private static bool ValidToken(string? value)
        => !string.IsNullOrWhiteSpace(value) && value.Length <= MaxTokenLength
           && value.All(c => c >= '!' && c <= '~');

    private static bool ValidOptionalToken(string? value)
        => value is null || ValidToken(value);

    private static string SignalLabel(MZP0SignalKind kind) => kind switch
    {
        MZP0SignalKind.Session => "Session",
        MZP0SignalKind.Mapping => "Mapping",
        MZP0SignalKind.Decoder => "Décodage",
        MZP0SignalKind.Map => "Carte",
        MZP0SignalKind.Combat => "Combat",
        MZP0SignalKind.ServerResponse => "Réponse serveur",
        _ => "Inconnu",
    };

    private static string DirectionLabel(MZP0Direction direction) => direction switch
    {
        MZP0Direction.ClientToServer => "sortant",
        MZP0Direction.ServerToClient => "entrant",
        MZP0Direction.Local => "local",
        _ => "inconnu",
    };

    private void RejectAndInvalidate(string code, string message)
    {
        ResetObservedState(message);
        Reject(code, message);
    }

    private void Reject(string code, string message)
    {
        RejectedCount++;
        Diagnostic = message;
        Journal(MZP0JournalOutcome.Rejected, code, message);
    }

    private void ResetObservedState(string message)
    {
        _validUntilUtc = DateTimeOffset.MinValue;
        // Keep the high watermark after expiry/rejection: stale frames cannot become
        // acceptable just because the presentation returned to an unknown state.
        SessionStatus = "NON ATTESTÉE";
        SessionDetail = message;
        EvidenceStatus = "AUCUNE PREUVE ACTIVE";
        EvidenceDetail = "Direction, contenu et session incertains.";
        AckStatus = "INCONNU";
        AckDetail = "Aucune réponse serveur corrélée et attestée.";
        LastObservation = "Aucune preuve active";
        Diagnostic = message;
    }

    private void Journal(MZP0JournalOutcome outcome, string code, string detail)
    {
        var line = new MZP0OperatorJournalLine(DateTimeOffset.UtcNow, outcome, code, detail);
        if (outcome == MZP0JournalOutcome.Rejected) GroupError(line);
        _history.Insert(0, line);
        if (MatchesFilter(line)) VisibleJournal.Insert(0, line);
        if (_history.Count > MaxJournalLines)
        {
            var oldest = _history[^1];
            _history.RemoveAt(_history.Count - 1);
            VisibleJournal.Remove(oldest);
        }
        HasVisibleJournal = VisibleJournal.Count > 0;
    }

    private void GroupError(MZP0OperatorJournalLine line)
    {
        if (_errorGroups.TryGetValue((line.Code, line.Detail), out var group))
        {
            group.Add(line.UtcTime);
            int at = ErrorGroups.IndexOf(group);
            if (at > 0) ErrorGroups.Move(at, 0);   // dernier vu en tête
        }
        else
        {
            group = new MZP0ErrorGroup(line.Code, line.Detail, line.UtcTime);
            _errorGroups[(line.Code, line.Detail)] = group;
            ErrorGroups.Insert(0, group);
            OnPropertyChanged(nameof(HasErrorGroups));
        }
        OnPropertyChanged(nameof(ErrorOccurrenceCount));
    }

    private bool MatchesFilter(MZP0OperatorJournalLine item)
    {
        if (JournalFilterIndex is 1 && item.Outcome != MZP0JournalOutcome.Attributed
            || JournalFilterIndex is 2 && item.Outcome != MZP0JournalOutcome.Rejected
            || JournalFilterIndex is 3 && item.Outcome != MZP0JournalOutcome.System)
            return false;
        return SearchText.Length == 0 || item.Code.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
            || item.Detail.Contains(SearchText, StringComparison.OrdinalIgnoreCase)
            || item.Label.Contains(SearchText, StringComparison.OrdinalIgnoreCase);
    }

    private void ApplyFilter()
    {
        VisibleJournal.Clear();
        foreach (var line in _history.Where(MatchesFilter)) VisibleJournal.Add(line);
        HasVisibleJournal = VisibleJournal.Count > 0;
    }

    private void UnbindCore()
    {
        Interlocked.Increment(ref _subscriptionEpoch);
        if (_source is not null)
        {
            if (_sourceHandler is not null) _source.Received -= _sourceHandler;
            if (_unavailableHandler is not null) _source.Unavailable -= _unavailableHandler;
        }
        _source = null;
        _sourceHandler = null;
        _unavailableHandler = null;
        _authority = null;
        _pinned = null;
        _approvedSource = null;
    }

    public void Dispose()
    {
        if (_disposed) return;
        UnbindCore();
        _disposed = true;
    }
}
