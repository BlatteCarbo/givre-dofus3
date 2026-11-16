using System;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

namespace GivreClone.App.Services.Production;

public enum GuidedActivityProductionState
{
    Absent,
    Ready,
    AwaitingObservation,
    Blocked,
    Uncertain
}

/// <summary>
/// Operator-authored target and provenance, never a receipt. GID is the
/// requested CRAFT RESULT; this record makes no claim that its ingredients
/// have been harvested, stored, purchased or consumed.
/// </summary>
public sealed record GuidedActivityProductionGoal(
    int ServerId, long CharacterId, long SourceMap, long SourceMapGeneration,
    int Cell, int ResultGid, int Quantity, int MaxPods, bool Gather)
{
    public bool Valid =>
        ServerId > 0 && CharacterId > 0 &&
        SourceMap is > 0 and <= int.MaxValue && SourceMapGeneration > 0 &&
        (uint)Cell < GivreClone.Protocol.MapPathfinder.CellCount &&
        ResultGid > 0 && Quantity is > 0 and <= 100_000 &&
        MaxPods is >= 1 and <= 100;
}

public sealed record GuidedActivityProductionStatus(
    GuidedActivityProductionState State, GuidedActivityProductionGoal? Goal, string Reason)
{
    public bool MayPrepareProduction => State == GuidedActivityProductionState.Ready && Goal is not null;
}

/// <summary>
/// Cross-session hand-off between the existing Lua one-shot route and the
/// EXISTING ItemCraftPlannerViewModel. This store never dispatches gameplay.
/// A checksum+anchor pair deliberately fails closed after a torn write.
/// The ActivityRecovery durable marker (not this document) owns send rights.
/// </summary>
public sealed class GuidedActivityProductionStore
{
    private const string Header = "GIVRE-GUIDED-ACTIVITY-PRODUCTION-v11\n";
    private const string AnchorHeader = "GIVRE-GUIDED-ACTIVITY-ANCHOR-v11|";
    private static readonly UTF8Encoding StrictUtf8 = new(false, true);
    private readonly string _root;

    public GuidedActivityProductionStore(string root)
    {
        if (string.IsNullOrWhiteSpace(root))
            throw new ArgumentException("Durable objective root required", nameof(root));
        _root = Path.GetFullPath(root);
    }

    public static string DefaultRoot() =>
        Path.Combine(MZP19LocalClone.DataRoot, "production", "guided-activity-v11");

    private string FilePath(int server, long character) =>
        Path.Combine(_root, "server-" + server.ToString(CultureInfo.InvariantCulture),
            "character-" + character.ToString(CultureInfo.InvariantCulture) + ".objective");

    private static bool StorageError(Exception e) =>
        e is IOException or UnauthorizedAccessException or ArgumentException or
            NotSupportedException or DecoderFallbackException or JsonException;

    private static string Digest(string value) =>
        Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(value)));

    public GuidedActivityProductionStatus Read(int server, long character)
    {
        if (server <= 0 || character <= 0)
            return new(GuidedActivityProductionState.Blocked, null,
                "identité serveur/personnage manquante");
        try
        {
            string path = FilePath(server, character);
            string text;
            try { text = File.ReadAllText(path, StrictUtf8); }
            catch (FileNotFoundException)
            {
                return File.Exists(path + ".anchor")
                    ? new(GuidedActivityProductionState.Uncertain, null, "ancre sans objectif : réparation manuelle requise")
                    : new(GuidedActivityProductionState.Absent, null, "aucun objectif sauvegardé");
            }
            catch (DirectoryNotFoundException)
            {
                // A new server has no per-server folder. An existing server
                // folder unexpectedly missing its path/file is not a clean
                // status; retain the fail-closed result in that case.
                if (Directory.Exists(Path.GetDirectoryName(path)!))
                    return new(GuidedActivityProductionState.Uncertain, null,
                        "fichier objectif inaccessible sur un profil existant");
                return new(GuidedActivityProductionState.Absent, null, "aucun objectif encore sauvegardé");
            }
            if (text.Length > 8192 || !text.StartsWith(Header, StringComparison.Ordinal)
                || !text.EndsWith('\n'))
                return new(GuidedActivityProductionState.Uncertain, null, "document durable tronqué ou inconnu");
            string[] pieces = text[Header.Length..].TrimEnd('\n').Split('\n');
            if (pieces.Length != 2 || pieces[1].Length != 64 ||
                !string.Equals(Digest(pieces[0]), pieces[1], StringComparison.Ordinal))
                return new(GuidedActivityProductionState.Uncertain, null, "hash de l'objectif invalide");
            string anchor = File.ReadAllText(path + ".anchor", StrictUtf8);
            if (anchor != AnchorHeader + pieces[1] + "\n")
                return new(GuidedActivityProductionState.Uncertain, null, "ancre de l'objectif incompatible");
            byte[] bytes = Convert.FromBase64String(pieces[0]);
            GuidedActivityProductionGoal? goal =
                JsonSerializer.Deserialize<GuidedActivityProductionGoal>(bytes);
            if (goal is not { Valid: true } || goal.ServerId != server || goal.CharacterId != character)
                return new(GuidedActivityProductionState.Uncertain, null, "profil ou champs incompatibles");
            return new(GuidedActivityProductionState.Ready, goal,
                "objectif utilisateur authentifié localement ; aucun reçu serveur ou stock n'est certifié");
        }
        catch (Exception e) when (StorageError(e) || e is FormatException)
        {
            return new(GuidedActivityProductionState.Uncertain, null,
                "objectif inaccessible ou corrompu (" + e.GetType().Name + ")");
        }
    }

    /// <summary>
    /// Explicit operator replacement only, after the current session and
    /// ActivityRecovery marker have been rechecked at the UI entry. This
    /// writes a durable objective but neither reserves a gameplay action nor
    /// clears an uncertain recovery attempt. Corrupt goals are immutable.
    /// </summary>
    public bool TrySave(GuidedActivityProductionGoal goal)
    {
        if (goal is not { Valid: true }) return false;
        try
        {
            string path = FilePath(goal.ServerId, goal.CharacterId);
            Directory.CreateDirectory(Path.GetDirectoryName(path)!);
            // Only one process can replace this character's objective at
            // once. A concurrent operator must see the latest durable
            // document AFTER winning the lock, not a stale pre-lock read.
            using var ownership = new FileStream(path + ".writer-lock",
                FileMode.OpenOrCreate, FileAccess.ReadWrite, FileShare.None);
            var existing = Read(goal.ServerId, goal.CharacterId);
            if (existing.State is GuidedActivityProductionState.Uncertain or
                GuidedActivityProductionState.Blocked) return false;
            string payload = Convert.ToBase64String(JsonSerializer.SerializeToUtf8Bytes(goal));
            string hash = Digest(payload);
            string temp = path + ".staging-" + Guid.NewGuid().ToString("N");
            string anchorTemp = path + ".anchor.staging-" + Guid.NewGuid().ToString("N");
            static void Persist(string file, string text)
            {
                using var stream = new FileStream(file, FileMode.CreateNew,
                    FileAccess.Write, FileShare.None, 4096, FileOptions.WriteThrough);
                byte[] bytes = Encoding.UTF8.GetBytes(text);
                stream.Write(bytes);
                stream.Flush(flushToDisk: true);
            }
            Persist(temp, Header + payload + "\n" + hash + "\n");
            Persist(anchorTemp, AnchorHeader + hash + "\n");
            // Torn replacement fails closed on next read: the old anchor
            // doesn't match a new document, nor is an old document accepted
            // with a new anchor. No automatic recovery/retry on exception.
            File.Move(temp, path, overwrite: true);
            File.Move(anchorTemp, path + ".anchor", overwrite: true);
            return true;
        }
        catch (Exception e) when (StorageError(e)) { return false; }
    }

    public GuidedActivityProductionStatus InspectWithRecovery(int server, long character,
        ActivityRecoveryDurableStatus activity)
    {
        var saved = Read(server, character);
        if (saved.State != GuidedActivityProductionState.Ready) return saved;
        if (activity.Scope.ServerId != server || activity.Scope.CharacterId != character)
            return new(GuidedActivityProductionState.Blocked, saved.Goal,
                "provenance de la tentative d'activité incompatible");
        return activity.State switch
        {
            ActivityRecoveryDurableState.Clear => saved,
            ActivityRecoveryDurableState.Pending =>
                new(GuidedActivityProductionState.AwaitingObservation, saved.Goal,
                    "tentative activité durable non résolue : aucun ACK causal ; pas de production automatique"),
            _ => new(GuidedActivityProductionState.Blocked, saved.Goal,
                "activité non vérifiable : " + activity.State + " ; quarantaine maintenue")
        };
    }

    /// <summary>
    /// The manual atelier is not exempt from an outstanding irreversible
    /// route/gather attempt. This check is invoked at the ACTUAL
    /// ItemCraftProductionLaunch action port, even when the guided objective
    /// was detached or the process restarted. Clear is not an ACK; it only
    /// states that no ambiguous local ActivityRecovery marker was found.
    /// </summary>
    public static bool MayUseManualProduction(
        ActivityRecoveryDurableStatus activity, int server, long character)
        => server > 0 && character > 0 &&
           activity.Scope.ServerId == server &&
           activity.Scope.CharacterId == character &&
           activity.State == ActivityRecoveryDurableState.Clear;
}
