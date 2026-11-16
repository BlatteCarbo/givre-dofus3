using System;
using System.Collections.Generic;
using System.Collections.ObjectModel;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GivreClone.App.Services;

internal sealed record ForgeHistoryFact(ForgeHistoryEntry Entry, ForgeHistorySession Session);

/// <summary>
/// Immutable, cursor-named commits: each atomic file contains one measured event and its session
/// aggregate. Memory eviction never deletes archive files. Uncommitted .tmp files are ignored.
/// Like the other AtomicJson stores, the durability boundary is the atomic rename, not a claim
/// of survival after storage hardware loses power. No dispatcher, background queue or flush needed.
/// </summary>
internal sealed class ForgeHistoryPersistence
{
    private readonly string _directory;

    public ForgeHistoryPersistence(string directory)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(directory);
        _directory = Path.GetFullPath(directory);
    }

    public void Append(ForgeHistoryFact fact)
    {
        string path = Path.Combine(_directory, fact.Entry.Cursor.ToString("D20", CultureInfo.InvariantCulture) + ".json");
        // A stale second writer must never replace an already committed measured fact.
        if (File.Exists(path)) throw new IOException("Forge history cursor is already committed.");
        AtomicJson.Write(path, JsonSerializer.Serialize(fact, ForgeHistoryJson.Default.ForgeHistoryFact));
    }

    public IEnumerable<ForgeHistoryFact> Read(long since = 0)
    {
        if (!Directory.Exists(_directory)) yield break;
        var paths = Directory.EnumerateFiles(_directory, "*.json").OrderBy(p => p, StringComparer.Ordinal).ToArray();
        long expected = 1;
        // Check continuity even for a bounded page, without deserializing every old commit.
        foreach (string path in paths)
        {
            if (Path.GetFileNameWithoutExtension(path) != expected.ToString("D20", CultureInfo.InvariantCulture))
                throw new InvalidDataException("Forge history archive contains a missing or inconsistent commit.");
            expected++;
        }
        expected = Math.Max(0, since) + 1;
        foreach (string path in paths.Skip((int)Math.Min(since, paths.LongLength)))
        {
            var fact = JsonSerializer.Deserialize(File.ReadAllText(path), ForgeHistoryJson.Default.ForgeHistoryFact)
                ?? throw new InvalidDataException("Forge history commit is empty.");
            if (fact.Entry is null || fact.Session is null || fact.Entry.Cursor != expected
                || Path.GetFileNameWithoutExtension(path) != expected.ToString("D20", CultureInfo.InvariantCulture)
                || fact.Entry.SessionId != fact.Session.Id || fact.Entry.Pid != fact.Session.Pid
                || fact.Entry.AccountId != fact.Session.AccountId)
                throw new InvalidDataException("Forge history archive contains a missing or inconsistent commit.");
            expected++;
            // JSON dictionaries are mutable; never expose them through the public snapshot API.
            yield return new ForgeHistoryFact(fact.Entry with
            {
                Stats = fact.Entry.Stats is null ? null : Freeze(fact.Entry.Stats),
                Progress = Freeze(fact.Entry.Progress)
            }, fact.Session with
            {
                InitialProgress = Freeze(fact.Session.InitialProgress),
                LatestProgress = Freeze(fact.Session.LatestProgress)
            });
        }
    }

    private static IReadOnlyDictionary<TKey, TValue> Freeze<TKey, TValue>(IReadOnlyDictionary<TKey, TValue> source)
        where TKey : notnull => new ReadOnlyDictionary<TKey, TValue>(source.ToDictionary(p => p.Key, p => p.Value));
}

[JsonSerializable(typeof(ForgeHistoryFact))]
internal sealed partial class ForgeHistoryJson : JsonSerializerContext { }
