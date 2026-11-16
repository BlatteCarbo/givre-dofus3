using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GivreClone.App.Services;

public sealed record SmithObjectiveIdentity(string AccountId, int ServerId, long CharacterId, int JobId);
public enum SmithDebitStatus { Reserved, Settled, NotSent }
public sealed record SmithObjectiveDebit(Guid Id, Guid AttemptId, string Kind, long Quote,
    long? ActualExpense, SmithDebitStatus Status, int? ItemUid = null, long? ItemGid = null, int? Quantity = null);
public sealed record SmithObjective(Guid Id, SmithObjectiveIdentity Identity, int TargetLevel,
    long Limit, long WalletFloor, long? OpeningExpense, long HistoricalConfirmedSubtotal,
    long NewExpense, IReadOnlyList<SmithObjectiveDebit> Debits)
{
    public long Reserved => Debits.Where(d => d.Status == SmithDebitStatus.Reserved).Sum(d => d.Quote);
    public bool NeedsReconciliation => OpeningExpense is null || Debits.Any(d => d.Status == SmithDebitStatus.Reserved);
    public long? TotalExpense => OpeningExpense is long opening ? checked(opening + NewExpense) : null;
    public long Available(long wallet) => wallet < 0 || NeedsReconciliation ? 0
        : Math.Max(0, Math.Min(Limit - TotalExpense!.Value, wallet - WalletFloor));
}

public sealed record SmithObjectivesFile(int Version, IReadOnlyList<SmithObjective> Objectives);

[JsonSerializable(typeof(SmithObjectivesFile))]
internal sealed partial class SmithObjectiveJsonContext : JsonSerializerContext { }

/// <summary>Durable authorization, separate from the historical event projection.</summary>
public sealed class SmithObjectiveStore
{
    private readonly object _gate = new();
    private readonly string? _path;
    private readonly Dictionary<Guid, SmithObjective> _objectives = new();
    public string? StorageError { get; }

    public SmithObjectiveStore(string? path = null)
    {
        _path = path;
        if (path is null || !File.Exists(path)) return;
        try
        {
            var file = JsonSerializer.Deserialize(File.ReadAllText(path), SmithObjectiveJsonContext.Default.SmithObjectivesFile);
            if (file is not { Version: 1, Objectives: not null }) throw new JsonException("Version du budget FM inconnue.");
            foreach (var objective in file.Objectives)
            {
                Validate(objective);
                if (_objectives.Values.Any(o => o.Identity == objective.Identity)
                    || !_objectives.TryAdd(objective.Id, Freeze(objective)))
                    throw new JsonException("Objectif FM dupliqué.");
            }
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException or ArgumentException or OverflowException)
        {
            // Never replace an unreadable ledger with an empty spendable budget or an older backup.
            _objectives.Clear();
            StorageError = "Budget FM illisible : rapprochement requis (" + e.GetType().Name + ").";
        }
    }

    public IReadOnlyList<SmithObjective> Read(string? accountId = null)
    {
        lock (_gate) return Array.AsReadOnly(_objectives.Values
            .Where(o => accountId is null || o.Identity.AccountId == accountId).ToArray());
    }

    public SmithObjective Open(SmithObjectiveIdentity identity, int targetLevel, long limit,
        long walletFloor, long? openingExpense = 0, long historicalConfirmedSubtotal = 0)
    {
        lock (_gate)
        {
            RequireStorage();
            var existing = _objectives.Values.FirstOrDefault(o => o.Identity == identity);
            if (existing is not null)
            {
                if (existing.TargetLevel != targetLevel || existing.Limit != limit || existing.WalletFloor != walletFloor)
                    throw new InvalidOperationException("Le contrat de cet objectif FM existe déjà ; aucun budget recréé.");
                return existing;
            }
            var objective = new SmithObjective(Guid.NewGuid(), identity, targetLevel, limit, walletFloor,
                openingExpense, historicalConfirmedSubtotal, 0, Array.Empty<SmithObjectiveDebit>());
            Validate(objective);
            Commit(objective);
            return objective;
        }
    }

    public SmithObjectiveDebit? Reserve(Guid objectiveId, Guid attemptId, string kind, long quote, long wallet,
        int? itemUid = null, long? itemGid = null, int? quantity = null)
    {
        if (attemptId == Guid.Empty || string.IsNullOrWhiteSpace(kind) || quote < 0)
            throw new ArgumentException("Opération FM invalide.");
        lock (_gate)
        {
            RequireStorage();
            var objective = _objectives[objectiveId];
            if (objective.NeedsReconciliation || quote > objective.Available(wallet) || wallet < objective.WalletFloor)
                return null;
            var debit = new SmithObjectiveDebit(Guid.NewGuid(), attemptId, kind, quote, null, SmithDebitStatus.Reserved,
                itemUid, itemGid, quantity);
            Commit(objective with { Debits = objective.Debits.Append(debit).ToArray() });
            return debit;
        }
    }

    public void Settle(Guid objectiveId, Guid operationId, long actualExpense)
    {
        if (actualExpense < 0) throw new ArgumentOutOfRangeException(nameof(actualExpense));
        lock (_gate)
        {
            RequireStorage();
            var objective = _objectives[objectiveId];
            var debit = objective.Debits.Single(d => d.Id == operationId);
            if (debit.Status == SmithDebitStatus.Settled && debit.ActualExpense == actualExpense) return;
            if (debit.Status != SmithDebitStatus.Reserved || actualExpense > debit.Quote)
                throw new InvalidOperationException("Débit FM non rapproché ; réservation conservée.");
            var settled = debit with { ActualExpense = actualExpense, Status = SmithDebitStatus.Settled };
            Commit(objective with
            {
                NewExpense = checked(objective.NewExpense + actualExpense),
                Debits = objective.Debits.Select(d => d.Id == operationId ? settled : d).ToArray()
            });
        }
    }

    public void ConfirmNotSent(Guid objectiveId, Guid operationId)
    {
        lock (_gate)
        {
            RequireStorage();
            var objective = _objectives[objectiveId];
            var debit = objective.Debits.Single(d => d.Id == operationId);
            if (debit.Status == SmithDebitStatus.NotSent) return;
            if (debit.Status != SmithDebitStatus.Reserved) throw new InvalidOperationException("Débit FM déjà confirmé.");
            Commit(objective with
            {
                Debits = objective.Debits.Select(d => d.Id == operationId
                    ? d with { Status = SmithDebitStatus.NotSent } : d).ToArray()
            });
        }
    }

    private void RequireStorage()
    {
        if (StorageError is not null) throw new InvalidOperationException(StorageError);
    }

    private static SmithObjective Freeze(SmithObjective objective) =>
        objective with { Debits = Array.AsReadOnly(objective.Debits.ToArray()) };

    private void Commit(SmithObjective objective)
    {
        Validate(objective);
        objective = Freeze(objective);
        var next = _objectives.Values.Where(o => o.Id != objective.Id).Append(objective).ToArray();
        if (_path is not null)
            AtomicJson.Write(_path, JsonSerializer.Serialize(new SmithObjectivesFile(1, next),
                SmithObjectiveJsonContext.Default.SmithObjectivesFile));
        _objectives[objective.Id] = objective;
    }

    private static void Validate(SmithObjective objective)
    {
        if (objective is null || objective.Id == Guid.Empty || objective.Identity is null
            || string.IsNullOrWhiteSpace(objective.Identity.AccountId) || objective.Identity.ServerId <= 0
            || objective.Identity.CharacterId <= 0 || objective.Identity.JobId <= 0
            || objective.TargetLevel is < 1 or > 200 || objective.Limit <= 0 || objective.WalletFloor < 0
            || objective.OpeningExpense < 0 || objective.HistoricalConfirmedSubtotal < 0 || objective.NewExpense < 0
            || objective.OpeningExpense is long opening && opening < objective.HistoricalConfirmedSubtotal
            || objective.Debits is null)
            throw new JsonException("Objectif FM invalide.");
        foreach (var debit in objective.Debits)
            if (debit.Id == Guid.Empty || debit.AttemptId == Guid.Empty || string.IsNullOrWhiteSpace(debit.Kind)
                || debit.Quote < 0 || !Enum.IsDefined(debit.Status)
                || debit.Status == SmithDebitStatus.Settled && (debit.ActualExpense is null or < 0 || debit.ActualExpense > debit.Quote)
                || debit.Status != SmithDebitStatus.Settled && debit.ActualExpense is not null)
                throw new JsonException("Débit FM invalide.");
        if (objective.Debits.Select(d => d.Id).Distinct().Count() != objective.Debits.Count
            || objective.Debits.Count(d => d.Status == SmithDebitStatus.Reserved) > 1
            || objective.Debits.Where(d => d.Status == SmithDebitStatus.Settled).Sum(d => d.ActualExpense!.Value) != objective.NewExpense)
            throw new JsonException("Comptabilité FM incohérente.");
        _ = objective.TotalExpense;
        _ = objective.Reserved;
    }
}
