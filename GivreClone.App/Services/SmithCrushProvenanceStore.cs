using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GivreClone.App.Services;

public sealed record SmithCrushOwner(string AccountId, int ServerId, long CharacterId);
public sealed record SmithCrushProof(SmithCrushOwner Owner, long Uid, long Gid,
    Guid SessionId, long Cursor, Guid RunId, int BagQuantity, int Remaining);
public sealed record SmithCrushProvenanceFile(int Version, IReadOnlyList<SmithCrushProof> Proofs);

[JsonSerializable(typeof(SmithCrushProvenanceFile))]
internal sealed partial class SmithCrushJsonContext : JsonSerializerContext { }

/// <summary>
/// A placement proves one unit, never a stack. A UID is credited at most once.
/// Retiring before dispatch also covers a crash or an ambiguous crusher response.
/// Unobserved changes across a restart cannot prove ownership, so old credits fail closed.
/// </summary>
public sealed class SmithCrushProvenanceStore
{
    private readonly object _gate = new();
    private readonly string _path;
    private readonly Guid _runId = Guid.NewGuid();
    private SmithCrushProof[] _proofs = [];
    public string? StorageError { get; private set; }

    public SmithCrushProvenanceStore(string path)
    {
        _path = path;
        if (!File.Exists(path)) return;
        try
        {
            var file = JsonSerializer.Deserialize(File.ReadAllText(path), SmithCrushJsonContext.Default.SmithCrushProvenanceFile);
            if (file is not { Version: 1, Proofs: not null }
                || file.Proofs.Any(p => p is null || p.Owner is null || !ValidOwner(p.Owner)
                    || p.Uid <= 0 || p.Gid <= 0 || p.Remaining is < 0 or > 1 || p.BagQuantity < p.Remaining)
                || file.Proofs.GroupBy(p => (p.Owner, p.Uid)).Any(g => g.Count() != 1))
                throw new JsonException("Provenance de brisage invalide.");
            _proofs = file.Proofs.ToArray();
        }
        catch (Exception e) when (e is JsonException or IOException or UnauthorizedAccessException)
        {
            StorageError = "Provenance inconnue : archive de brisage illisible (" + e.GetType().Name + ").";
        }
    }

    public static bool ValidOwner(SmithCrushOwner owner) =>
        !string.IsNullOrWhiteSpace(owner.AccountId) && owner.ServerId > 0 && owner.CharacterId > 0;

    public void Observe(SmithCrushOwner owner, ForgeHistoryEntry entry, long? bagGid, int bagQuantity)
    {
        lock (_gate)
        {
            if (StorageError is not null || !ValidOwner(owner) || entry.AccountId != owner.AccountId
                || entry.Kind != "support" || entry.ItemUid is not > 0 || entry.ItemGid is not > 0
                || _proofs.Any(p => p.Owner == owner && p.Uid == entry.ItemUid)) return;
            int quantity = bagGid == entry.ItemGid && bagQuantity > 0 ? bagQuantity : 0;
            Save([.. _proofs, new(owner, entry.ItemUid.Value, entry.ItemGid.Value,
                entry.SessionId, entry.Cursor, _runId, quantity, quantity > 0 ? 1 : 0)]);
        }
    }

    public int Available(SmithCrushOwner owner, long uid, long gid, int bagQuantity)
    {
        lock (_gate)
        {
            if (StorageError is not null) return 0;
            var proof = _proofs.FirstOrDefault(p => p.Owner == owner && p.Uid == uid);
            return proof is { Remaining: 1 } && proof.RunId == _runId && proof.Gid == gid
                && proof.BagQuantity == bagQuantity ? 1 : 0;
        }
    }

    public void Reconcile(SmithCrushOwner owner, Func<long, (long? Gid, int Quantity)> bag)
    {
        lock (_gate)
        {
            if (StorageError is not null) return;
            var next = _proofs.Select(p =>
            {
                if (p.Owner != owner || p.Remaining == 0 || p.RunId != _runId) return p;
                var current = bag(p.Uid);
                return current.Gid == p.Gid && current.Quantity == p.BagQuantity ? p : p with { Remaining = 0 };
            }).ToArray();
            if (!next.SequenceEqual(_proofs)) Save(next);
        }
    }

    public bool Retire(SmithCrushOwner owner, long uid, long gid)
    {
        lock (_gate)
        {
            if (StorageError is not null || !ValidOwner(owner) || uid <= 0 || gid <= 0) return false;
            var proof = _proofs.FirstOrDefault(p => p.Owner == owner && p.Uid == uid);
            if (proof is { Remaining: 0 }) return true;
            var next = proof is null
                ? [.. _proofs, new SmithCrushProof(owner, uid, gid, Guid.Empty, 0, _runId, 0, 0)]
                : _proofs.Select(p => p == proof ? p with { Remaining = 0 } : p).ToArray();
            return Save(next);
        }
    }

    private bool Save(SmithCrushProof[] next)
    {
        try
        {
            AtomicJson.Write(_path, JsonSerializer.Serialize(new SmithCrushProvenanceFile(1, next),
                SmithCrushJsonContext.Default.SmithCrushProvenanceFile));
            _proofs = next;
            return true;
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException)
        {
            StorageError = "Provenance inconnue : sauvegarde du brisage impossible (" + e.GetType().Name + ").";
            return false;
        }
    }
}
