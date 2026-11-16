using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Threading;
using System.Threading.Tasks;

namespace GivreClone.App.Services.Metamob;

public sealed record MetamobSyncRequest(
    string QuestSlug,
    int ServerId,
    int QuestServerId,
    SoulScope Scope,
    SoulHoldingsSnapshot Holdings,
    int PageSize = MetamobLimits.MaxPage,
    int BatchSize = MetamobLimits.MaxBatch);

public sealed record MetamobSyncReport(bool SentWrite, string Status, string? CaptureNeeded, IReadOnlyList<MetamobSyncRow> Rows);

public static class MetamobSync
{
    /// <summary>Chemin du bouton. Refuse un stock incomplet ou des contenus inconnus avant toute requête.
    /// Une absence dans un recensement connu vaut 0. Les quantités hors 0-30 ne sont pas envoyées.</summary>
    public static async Task<MetamobSyncReport> ExecuteAsync(MetamobClient client, MetamobSyncRequest request, SoulSyncStop stop, CancellationToken cancellationToken = default)
    {
        if (client is null) throw new ArgumentNullException(nameof(client));
        if (request is null) throw new ArgumentNullException(nameof(request));
        stop ??= new SoulSyncStop();
        var decision = SoulStock.Evaluate(request.Scope, request.Holdings);
        if (!decision.IsKnown)
            return new MetamobSyncReport(false, decision.Reason ?? SoulStoneContents.UnknownReason, decision.CaptureNeeded, Array.Empty<MetamobSyncRow>());
        if (!MetamobText.IsSlug(request.QuestSlug))
            return new MetamobSyncReport(false, SoulStock.InvalidQuest, null, Array.Empty<MetamobSyncRow>());
        if (request.ServerId <= 0 || request.QuestServerId <= 0 || request.QuestServerId != request.ServerId)
            return new MetamobSyncReport(false, SoulStock.WrongServer, null, Array.Empty<MetamobSyncRow>());
        return await PushAbsoluteAsync(client, request.QuestSlug, decision.Quantities, stop, request.PageSize, request.BatchSize, cancellationToken).ConfigureAwait(false);
    }

    /// <summary>Envoie un recensement déjà prouvé complet. L'absence d'un monstre vaut 0.
    /// Le bouton passe par <see cref="ExecuteAsync"/>, qui n'appelle ceci que si les contenus sont connus.</summary>
    public static async Task<MetamobSyncReport> PushAbsoluteAsync(
        MetamobClient client, string questSlug, IReadOnlyDictionary<int, int> absolute, SoulSyncStop stop,
        int pageSize = MetamobLimits.MaxPage, int batchSize = MetamobLimits.MaxBatch, CancellationToken cancellationToken = default)
    {
        if (client is null) throw new ArgumentNullException(nameof(client));
        absolute ??= new Dictionary<int, int>();
        stop ??= new SoulSyncStop();
        pageSize = Clamp(pageSize);
        batchSize = Clamp(batchSize);
        if (!MetamobText.IsSlug(questSlug))
            return new MetamobSyncReport(false, SoulStock.InvalidQuest, null, Array.Empty<MetamobSyncRow>());
        if (stop.Requested || cancellationToken.IsCancellationRequested)
            return new MetamobSyncReport(false, "Arrêté avant l'envoi. Aucune écriture.", null, Array.Empty<MetamobSyncRow>());

        Dictionary<int, RemoteMonster> remote;
        try
        {
            var fetched = await FetchRemoteAsync(client, questSlug, pageSize, stop, cancellationToken).ConfigureAwait(false);
            if (fetched.Error is not null)
                return new MetamobSyncReport(false, WithNoWrite(fetched.Error), null, Array.Empty<MetamobSyncRow>());
            remote = fetched.Monsters!;
        }
        catch (MetamobCallException ex)
        {
            return new MetamobSyncReport(false, WithNoWrite(ex.Message), null, Array.Empty<MetamobSyncRow>());
        }

        var skipped = new List<MetamobSyncRow>();
        var changes = Diff(absolute, remote, skipped);
        var updated = new List<MetamobSyncRow>();
        var failed = new List<MetamobSyncRow>();
        var sent = false;
        string? note = null;
        var index = 0;
        while (index < changes.Count)
        {
            if (stop.Requested || cancellationToken.IsCancellationRequested)
            {
                SkipRest(skipped, changes, index, "Arrêté avant le lot suivant.");
                note = "Arrêté avant le lot suivant.";
                break;
            }
            var count = Math.Min(batchSize, changes.Count - index);
            var batch = changes.GetRange(index, count);
            try
            {
                var acks = await client.UpdateQuantitiesAsync(questSlug, batch, cancellationToken).ConfigureAwait(false);
                sent = true;
                ApplyAcks(updated, failed, batch, acks);
                index += count;
            }
            catch (MetamobCallException ex)
            {
                if (ex.RequestSent) sent = true;
                FailAll(failed, batch, ex.Message);
                SkipRest(skipped, changes, index + count, "Non envoyé : le lot précédent a échoué.");
                note = "Envoi arrêté après un lot en échec.";
                break;
            }
            catch (OperationCanceledException)
            {
                sent = true;
                FailAll(failed, batch, "Interrompu.");
                SkipRest(skipped, changes, index + count, "Non envoyé : le lot précédent a échoué.");
                note = "Envoi arrêté après un lot en échec.";
                break;
            }
        }

        var rows = failed.Concat(updated).Concat(skipped)
            .OrderBy(row => row.Outcome)
            .ThenBy(row => row.MonsterId)
            .ToList();
        return new MetamobSyncReport(sent, Summary(rows, note), null, rows);
    }

    private static async Task<(string? Error, Dictionary<int, RemoteMonster>? Monsters)> FetchRemoteAsync(
        MetamobClient client, string questSlug, int pageSize, SoulSyncStop stop, CancellationToken cancellationToken)
    {
        var remote = new Dictionary<int, RemoteMonster>();
        var offset = 0;
        var finished = false;
        for (var pageIndex = 0; pageIndex < 50; pageIndex++)
        {
            if (stop.Requested || cancellationToken.IsCancellationRequested)
                return ("Arrêté avant l'envoi. Aucune écriture.", null);
            var page = await client.GetQuestMonstersAsync(questSlug, pageSize, offset, cancellationToken).ConfigureAwait(false);
            if (page.Monsters.Count > pageSize)
                return ("Liste des monstres illisible. Aucune écriture.", null);
            foreach (var monster in page.Monsters)
            {
                if (!remote.TryAdd(monster.Id, monster))
                    return ("Liste des monstres illisible. Aucune écriture.", null);
            }
            offset += page.Monsters.Count;
            if (page.Total is int total)
            {
                if (total < offset) return ("Liste des monstres illisible. Aucune écriture.", null);
                if (offset >= total) { finished = true; break; }
                if (page.Monsters.Count < pageSize) return ("Liste des monstres incomplète. Aucune écriture.", null);
                continue;
            }
            if (page.Monsters.Count < pageSize) { finished = true; break; }
        }
        return finished
            ? (null, remote)
            : ("Liste des monstres incomplète. Aucune écriture.", null);
    }

    private static List<QuantityChange> Diff(IReadOnlyDictionary<int, int> absolute, Dictionary<int, RemoteMonster> remote, List<MetamobSyncRow> skipped)
    {
        var changes = new List<QuantityChange>();
        foreach (var monster in remote.Values.OrderBy(item => item.Id))
        {
            var local = absolute.TryGetValue(monster.Id, out var qty) ? qty : 0;
            var name = monster.Name.Length == 0 ? monster.Id.ToString(CultureInfo.InvariantCulture) : monster.Name;
            if (local < 0 || local > MetamobLimits.MaxQuantity)
            {
                skipped.Add(Row(SoulSyncOutcome.Skipped, monster.Id, name, monster.Quantity, local, OutOfRange(local)));
                continue;
            }
            if (local == monster.Quantity)
            {
                skipped.Add(Row(SoulSyncOutcome.Skipped, monster.Id, name, monster.Quantity, local, "déjà " + local.ToString(CultureInfo.InvariantCulture)));
                continue;
            }
            changes.Add(new QuantityChange(monster.Id, name, monster.Quantity, local));
        }
        foreach (var pair in absolute.OrderBy(pair => pair.Key))
        {
            if (remote.ContainsKey(pair.Key) || pair.Value == 0) continue;
            var name = pair.Key.ToString(CultureInfo.InvariantCulture);
            if (pair.Value < 0 || pair.Value > MetamobLimits.MaxQuantity)
                skipped.Add(Row(SoulSyncOutcome.Skipped, pair.Key, name, 0, pair.Value, OutOfRange(pair.Value)));
            else if (pair.Value > 0)
                skipped.Add(Row(SoulSyncOutcome.Skipped, pair.Key, name, 0, pair.Value, "absent de la quête, non envoyé"));
        }
        return changes;
    }

    private static void ApplyAcks(List<MetamobSyncRow> updated, List<MetamobSyncRow> failed, IReadOnlyList<QuantityChange> batch, IReadOnlyList<MonsterAck> acks)
    {
        var seen = new HashSet<int>();
        var wanted = batch.ToDictionary(change => change.MonsterId);
        foreach (var ack in acks)
        {
            if (!wanted.TryGetValue(ack.MonsterId, out var change) || !seen.Add(ack.MonsterId)) continue;
            if (ack.Quantity == change.LocalQuantity)
                updated.Add(Row(SoulSyncOutcome.Updated, change.MonsterId, change.Name, change.RemoteQuantity, change.LocalQuantity,
                    change.RemoteQuantity.ToString(CultureInfo.InvariantCulture) + " → " + change.LocalQuantity.ToString(CultureInfo.InvariantCulture)));
            else
                failed.Add(Row(SoulSyncOutcome.Failed, change.MonsterId, change.Name, change.RemoteQuantity, change.LocalQuantity,
                    "accusé " + ack.Quantity.ToString(CultureInfo.InvariantCulture) + ", attendu " + change.LocalQuantity.ToString(CultureInfo.InvariantCulture)));
        }
        foreach (var change in batch)
        {
            if (seen.Contains(change.MonsterId)) continue;
            failed.Add(Row(SoulSyncOutcome.Failed, change.MonsterId, change.Name, change.RemoteQuantity, change.LocalQuantity, "absent de la réponse"));
        }
    }

    private static void FailAll(List<MetamobSyncRow> failed, IReadOnlyList<QuantityChange> batch, string detail)
    {
        foreach (var change in batch)
            failed.Add(Row(SoulSyncOutcome.Failed, change.MonsterId, change.Name, change.RemoteQuantity, change.LocalQuantity, detail));
    }

    private static void SkipRest(List<MetamobSyncRow> skipped, IReadOnlyList<QuantityChange> changes, int index, string detail)
    {
        for (var i = index; i < changes.Count; i++)
        {
            var change = changes[i];
            skipped.Add(Row(SoulSyncOutcome.Skipped, change.MonsterId, change.Name, change.RemoteQuantity, change.LocalQuantity, detail));
        }
    }

    private static MetamobSyncRow Row(SoulSyncOutcome outcome, int id, string name, int remote, int local, string detail)
        => new(outcome, id, name, remote, local, detail);

    private static string OutOfRange(int quantity)
        => "quantité " + quantity.ToString(CultureInfo.InvariantCulture) + " hors limite 0-" + MetamobLimits.MaxQuantity.ToString(CultureInfo.InvariantCulture) + ", non envoyée";

    private static string Summary(IReadOnlyList<MetamobSyncRow> rows, string? note)
    {
        var updated = 0;
        var skipped = 0;
        var failed = 0;
        foreach (var row in rows)
        {
            if (row.Outcome == SoulSyncOutcome.Updated) updated++;
            else if (row.Outcome == SoulSyncOutcome.Skipped) skipped++;
            else failed++;
        }
        var text = "Mis à jour : " + updated.ToString(CultureInfo.InvariantCulture)
            + ". Ignorés : " + skipped.ToString(CultureInfo.InvariantCulture)
            + ". Échecs : " + failed.ToString(CultureInfo.InvariantCulture) + ".";
        return string.IsNullOrEmpty(note) ? text : note + " " + text;
    }

    private static string WithNoWrite(string message)
        => message.Contains("Aucune écriture", StringComparison.Ordinal) ? message : message + " Aucune écriture.";

    private static int Clamp(int value)
    {
        if (value < 1) return 1;
        return value > MetamobLimits.MaxPage ? MetamobLimits.MaxPage : value;
    }
}
