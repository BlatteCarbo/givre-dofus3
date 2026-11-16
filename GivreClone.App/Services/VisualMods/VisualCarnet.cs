using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GivreClone.App.Services.VisualMods;

/// <summary>Coordinates and hints are user-entered; none is an observed game state.</summary>
public sealed record VisualPosition(Guid Id, string Label, int X, int Y, string Note, bool IsBookmark, DateTimeOffset CreatedUtc);

public sealed class VisualCarnetDocument
{
    [JsonRequired]
    public int Version { get; init; } = VisualCarnetStore.CurrentVersion;
    [JsonRequired]
    public List<VisualPosition> Positions { get; init; } = new();
}

public sealed record VisualCarnetLoad(VisualCarnetDocument Document, bool ReadOnly, string Message);

[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(VisualCarnetDocument))]
internal sealed partial class VisualCarnetJsonContext : JsonSerializerContext { }

/// <summary>Bounded, local, versioned, atomic carnet. A damaged or newer store is never overwritten.</summary>
public sealed class VisualCarnetStore
{
    public const int CurrentVersion = 1;
    public const int MaxPositions = 250;
    public const int MaxCoordinate = 10000;
    public const int MaxFileBytes = 512 * 1024;
    private const int MaxLabelLength = 60;
    private const int MaxNoteLength = 800;

    public string FilePath { get; }

    public VisualCarnetStore(string? filePath = null)
    {
        FilePath = Path.GetFullPath(filePath ?? Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
            "GivreClone", "visual-mods", "carnet-v1.json"));
        if (string.IsNullOrWhiteSpace(FilePath)) throw new ArgumentException("Chemin de stockage manquant.", nameof(filePath));
    }

    public VisualCarnetLoad Load()
    {
        try
        {
            EnsureRegularPath();
            if (!File.Exists(FilePath)) return new(new VisualCarnetDocument(), false, "Carnet local prêt.");
            var info = new FileInfo(FilePath);
            if (info.Length is < 1 or > MaxFileBytes)
                throw new InvalidDataException("Taille du carnet hors limites.");
            string json = File.ReadAllText(FilePath);
            var doc = JsonSerializer.Deserialize(json, VisualCarnetJsonContext.Default.VisualCarnetDocument)
                ?? throw new InvalidDataException("Carnet vide.");
            Validate(doc);
            return new(doc, false, $"Carnet chargé : {doc.Positions.Count} position(s) locale(s).");
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or JsonException or InvalidDataException or ArgumentException)
        {
            return new(new VisualCarnetDocument(), true,
                "Carnet existant illisible ou incompatible : édition bloquée pour préserver le fichier original.");
        }
    }

    public void Save(VisualCarnetDocument document)
    {
        Validate(document);
        var json = JsonSerializer.Serialize(document, VisualCarnetJsonContext.Default.VisualCarnetDocument);
        if (System.Text.Encoding.UTF8.GetByteCount(json) > MaxFileBytes)
            throw new InvalidDataException("Carnet trop volumineux.");
        EnsureRegularPath();
        // Save is safe even if called directly (without the ViewModel): an existing
        // unknown/corrupt document must never be overwritten with an empty snapshot.
        if (File.Exists(FilePath))
        {
            if (new FileInfo(FilePath).Length is < 1 or > MaxFileBytes)
                throw new InvalidDataException("Carnet sur disque invalide : écriture refusée.");
            var previous = JsonSerializer.Deserialize(File.ReadAllText(FilePath),
                VisualCarnetJsonContext.Default.VisualCarnetDocument)
                ?? throw new InvalidDataException("Carnet sur disque vide : écriture refusée.");
            Validate(previous);
        }
        // Existing app utility: atomic replacement plus the previous version in .bak.
        AtomicJson.Write(FilePath, json, keepBackup: true);
    }

    /// <summary>Reject linked destinations, temp/backup files and parent junctions before any disk I/O.
    /// This also protects the path when the target has not yet been created.</summary>
    private void EnsureRegularPath()
    {
        string? parent = Path.GetDirectoryName(FilePath);
        while (parent is not null)
        {
            if (Directory.Exists(parent) &&
                (File.GetAttributes(parent) & FileAttributes.ReparsePoint) != 0)
                throw new InvalidDataException("Dossier de carnet redirigé : accès refusé.");
            parent = Path.GetDirectoryName(parent);
        }
        foreach (string candidate in new[] { FilePath, FilePath + ".tmp", FilePath + ".bak" })
        {
            // LinkTarget also catches an existing dangling symbolic link that File.Exists does not report.
            if (Directory.Exists(candidate) || new FileInfo(candidate).LinkTarget is not null ||
                ((File.Exists(candidate) || Directory.Exists(candidate)) &&
                 (File.GetAttributes(candidate) & FileAttributes.ReparsePoint) != 0))
                throw new InvalidDataException("Fichier de carnet redirigé : accès refusé.");
        }
    }

    public static void Validate(VisualCarnetDocument? document)
    {
        if (document is null || document.Version != CurrentVersion)
            throw new InvalidDataException("Version de carnet inconnue.");
        if (document.Positions is null || document.Positions.Count > MaxPositions)
            throw new InvalidDataException("Nombre de positions hors limites.");
        var ids = new HashSet<Guid>();
        foreach (var p in document.Positions)
        {
            if (p is null || p.Id == Guid.Empty || !ids.Add(p.Id) || p.CreatedUtc == default)
                throw new InvalidDataException("Identifiant de position invalide ou en double.");
            if (!ValidLabel(p.Label) || !ValidNote(p.Note) || !ValidCoordinate(p.X) || !ValidCoordinate(p.Y))
                throw new InvalidDataException("Coordonnées, libellé ou note invalides.");
        }
    }

    public static bool TryCreatePosition(string? label, string? xText, string? yText, string? note, bool bookmark,
        out VisualPosition? position, out string error)
    {
        position = null;
        label = label?.Trim();
        note = note?.Trim() ?? "";
        if (!ValidLabel(label)) { error = "Libellé requis (1 à 60 caractères, sans caractère de contrôle)."; return false; }
        if (!ValidNote(note)) { error = "Note limitée à 800 caractères (texte uniquement)."; return false; }
        if (!TryCoordinate(xText, out int x) || !TryCoordinate(yText, out int y))
        {
            error = "X et Y doivent être des entiers entre -10000 et 10000.";
            return false;
        }
        position = new VisualPosition(Guid.NewGuid(), label!, x, y, note, bookmark, DateTimeOffset.UtcNow);
        error = "";
        return true;
    }

    public static bool TryCoordinate(string? input, out int number)
    {
        number = 0;
        return input is not null && input.Trim().Length <= 7
            && int.TryParse(input.Trim(), NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out number)
            && ValidCoordinate(number);
    }

    public static bool ValidCoordinate(int number) => number is >= -MaxCoordinate and <= MaxCoordinate;
    private static bool ValidLabel(string? value) => !string.IsNullOrWhiteSpace(value)
        && value == value.Trim() && value.Length <= MaxLabelLength && !value.Any(char.IsControl);
    private static bool ValidNote(string? value) => value is not null && value.Length <= MaxNoteLength
        && !value.Any(c => char.IsControl(c) && c is not ('\r' or '\n' or '\t'));
}

public sealed record ManualHintRoute(int StartX, int StartY, int TargetX, int TargetY, int DeltaX,
    int DeltaY, int ManhattanDistance, string Direction, string Hint);

/// <summary>Only computes the distance between two manually provided points, never a path or treasure solution.</summary>
public static class ManualTreasurePlanner
{
    public static bool TryPlan(string? startX, string? startY, string? targetX, string? targetY,
        string? exactHint, bool manuallyVerified, out ManualHintRoute? route, out string error)
    {
        route = null;
        if (!manuallyVerified) { error = "Vérifie d'abord la transcription de ton indice manuel."; return false; }
        exactHint = exactHint?.Trim();
        if (string.IsNullOrWhiteSpace(exactHint) || exactHint.Length > 120 || exactHint.Any(char.IsControl))
        {
            error = "Indice exact requis (1 à 120 caractères, une seule ligne).";
            return false;
        }
        if (!VisualCarnetStore.TryCoordinate(startX, out var sx)
            || !VisualCarnetStore.TryCoordinate(startY, out var sy)
            || !VisualCarnetStore.TryCoordinate(targetX, out var tx)
            || !VisualCarnetStore.TryCoordinate(targetY, out var ty))
        {
            error = "Entre les 4 coordonnées entières entre -10000 et 10000.";
            return false;
        }
        int dx = tx - sx, dy = ty - sy;
        string direction = dx == 0 && dy == 0 ? "Même position" :
            string.Join(" + ", new[] { dx > 0 ? "Est" : dx < 0 ? "Ouest" : "",
                                           dy > 0 ? "Sud" : dy < 0 ? "Nord" : "" }
                .Where(s => s.Length != 0));
        route = new ManualHintRoute(sx, sy, tx, ty, dx, dy, Math.Abs(dx) + Math.Abs(dy), direction, exactHint);
        error = "";
        return true;
    }
}

public sealed record VisualMapPin(Guid Id, string Label, int X, int Y, bool IsBookmark, double Left, double Top);

/// <summary>Visual-only normalized plotting. The scale follows the saved set, not any real map geometry.</summary>
public static class VisualMapProjection
{
    public static IReadOnlyList<VisualMapPin> Project(IEnumerable<VisualPosition> source,
        double width = 440, double height = 210)
    {
        ArgumentNullException.ThrowIfNull(source);
        if (!double.IsFinite(width) || !double.IsFinite(height) || width < 80 || height < 80)
            throw new ArgumentOutOfRangeException(nameof(width), "Carte trop petite ou taille invalide.");
        var points = source.ToArray();
        VisualCarnetStore.Validate(new VisualCarnetDocument { Positions = points.ToList() });
        if (points.Length == 0) return Array.Empty<VisualMapPin>();
        double minX = points.Min(p => p.X), maxX = points.Max(p => p.X);
        double minY = points.Min(p => p.Y), maxY = points.Max(p => p.Y);
        double rangeX = Math.Max(1, maxX - minX), rangeY = Math.Max(1, maxY - minY);
        double scale = Math.Min((width - 44) / rangeX, (height - 44) / rangeY);
        double usedWidth = (maxX - minX) * scale, usedHeight = (maxY - minY) * scale;
        double left = (width - usedWidth) / 2, top = (height - usedHeight) / 2;
        return points.Select(p => new VisualMapPin(p.Id, p.Label, p.X, p.Y, p.IsBookmark,
            left + (p.X - minX) * scale, top + (p.Y - minY) * scale)).ToArray();
    }
}
