using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using GivreClone.App.Services;

namespace GivreClone.App.Services.Eco;

/// <summary>Prix saisis à la main, par serveur (un prix saisi sert à toutes les recettes qui utilisent l'objet) : complète ou corrige l'HDV observé.</summary>
public sealed class ManualPriceBook
{
    public sealed class Entry { public decimal Price { get; set; } public DateTime AtUtc { get; set; } }

    public Dictionary<int, Dictionary<long, Entry>> Servers { get; set; } = new();

    public void Set(int serverId, long gid, decimal price, DateTime? at = null)
    {
        if (gid <= 0 || price < 0) throw new ArgumentOutOfRangeException(nameof(price));
        if (!Servers.TryGetValue(serverId, out var d)) Servers[serverId] = d = new();
        if (price == 0) d.Remove(gid); else d[gid] = new Entry { Price = price, AtUtc = at ?? DateTime.UtcNow };
    }

    public PriceQuote? Get(int serverId, long gid, DateTime? now = null)
        => Servers.TryGetValue(serverId, out var d) && d.TryGetValue(gid, out var e)
            ? new PriceQuote(e.Price, (long)Math.Max(0, ((now ?? DateTime.UtcNow) - e.AtUtc).TotalMilliseconds), "manuel") : null;

    public int Count(int serverId) => Servers.TryGetValue(serverId, out var d) ? d.Count : 0;
}

/// <summary>Carnet personnel : positions (coordonnées), notes et messages prêts à coller. Local, jamais envoyé nulle part.</summary>
public sealed class Notebook
{
    public sealed class Position { public string Name { get; set; } = ""; public int X { get; set; } public int Y { get; set; } public long MapId { get; set; } public string Note { get; set; } = ""; }
    public sealed class Note { public string Title { get; set; } = ""; public string Text { get; set; } = ""; public DateTime AtUtc { get; set; } = DateTime.UtcNow; }
    public sealed class Message { public string Title { get; set; } = ""; public string Text { get; set; } = ""; }

    public List<Position> Positions { get; set; } = new();
    public List<Note> Notes { get; set; } = new();
    public List<Message> Messages { get; set; } = new();
    public List<long> TrackedItems { get; set; } = new();   // objets suivis (Craft ou acheter)

    public static string Coordinates(Position p) => $"[{p.X},{p.Y}]";
    public static bool TryParseCoordinates(string text, out int x, out int y)
    {
        x = y = 0;
        var t = (text ?? "").Trim().Trim('[', ']', '(', ')');
        var parts = t.Split(new[] { ',', ';', ' ' }, StringSplitOptions.RemoveEmptyEntries);
        return parts.Length == 2 && int.TryParse(parts[0], out x) && int.TryParse(parts[1], out y) && x is >= -200 and <= 200 && y is >= -200 and <= 200;
    }
}

/// <summary>
/// Stores économiques JSON. Un original illisible est conservé par <see cref="AtomicJson.LoadStore{T}"/>
/// avant qu'un Save puisse écrire ; les mises à jour sérialisent aussi lectures et écritures par chemin.
/// L'API générique historique Load/Save est conservée pour les carnets, prix, alertes et séances FM.
/// </summary>
public static class EcoJson
{
    private static readonly JsonSerializerOptions Opts = new() { WriteIndented = true };
    private sealed class PathState
    {
        public readonly object Gate = new();
        // Si l'original illisible ne pouvait être renommé, ne JAMAIS l'écraser.
        public bool WriteAside;
    }

    // Même chemin : pas de collision entre les fichiers .tmp/.bak ni entre Load et Save.
    private static readonly ConcurrentDictionary<string, PathState> States = new(
        OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal);
    private static PathState StateFor(string path) => States.GetOrAdd(Path.GetFullPath(path), static _ => new PathState());

    // AtomicJson.LoadStore exige un type référence ; cet emballage conserve l'ancienne contrainte T : new().
    private sealed class Value<T>(T data) { public T Data { get; } = data; }

    private static T Decode<T>(string json)
    {
        if (JsonSerializer.Deserialize<T>(json) is not { } data)
            throw new JsonException("Store JSON null.");

        // Un JSON syntaxiquement valide mais avec une collection essentielle à null
        // ne constitue pas un état exploitable : préférer la sauvegarde de secours.
        bool invalid = data switch
        {
            Notebook n => n.Positions is null || n.Notes is null || n.Messages is null || n.TrackedItems is null
                || n.Positions.Any(p => p is null) || n.Notes.Any(note => note is null) || n.Messages.Any(m => m is null),
            ManualPriceBook p => p.Servers is null || p.Servers.Values.Any(s => s is null || s.Values.Any(e => e is null)),
            FmLedgerStore f => f.Sessions is null || f.Sessions.Any(s => s is null || s.Purchases is null || s.Uses is null),
            PriceAlertBook a => a.Alerts is null || a.Alerts.Any(x => x is null),
            SubscriptionBook s => s.Entries is null || s.Entries.Any(x => x is null || x.SentThresholds is null),
            _ => false
        };
        if (invalid) throw new JsonException("Store JSON incomplet (collection requise absente).");
        return data;
    }

    private static StoreLoadResult<Value<T>> Examine<T>(string path, Func<T> empty)
        => AtomicJson.LoadStore(path, json => new Value<T>(Decode<T>(json)), () => new Value<T>(empty()));

    private static bool TryReadRecovery<T>(string path, out T data)
    {
        try
        {
            if (File.Exists(path))
            {
                data = Decode<T>(File.ReadAllText(path));
                return true;
            }
        }
        catch (Exception e) when (e is IOException or UnauthorizedAccessException or JsonException or NotSupportedException) { }
        data = default!;
        return false;
    }

    public static T Load<T>(string path) where T : new()
    {
        var state = StateFor(path);
        lock (state.Gate)
        {
            var loaded = Examine(path, static () => new T());
            state.WriteAside = loaded.WriteAside;

            // Une écriture antérieure dans .recovered (original verrouillé) ne doit pas être
            // perdue lorsque cet original peut enfin être mis de côté à la session suivante.
            if ((loaded.Outcome is StoreLoad.Missing or StoreLoad.Quarantined or StoreLoad.Restored)
                && TryReadRecovery(path + ".recovered", out T recovered)
                && (loaded.Outcome != StoreLoad.Restored || !File.Exists(path + ".bak")
                    || File.GetLastWriteTimeUtc(path + ".recovered") >= File.GetLastWriteTimeUtc(path + ".bak")))
                return recovered;
            return loaded.Data.Data;
        }
    }

    public static void Save<T>(string path, T value)
    {
        var state = StateFor(path);
        lock (state.Gate)
        {
            // Save peut être appelé sans Load préalable, ou après une modification externe du disque.
            // Vérifier AVANT tout remplacement que le fichier cible n'est pas devenu illisible.
            if (!state.WriteAside)
                state.WriteAside = Examine(path, static () => default(T)!).WriteAside;

            string target = AtomicJson.StorePath(path, state.WriteAside);
            if (state.WriteAside && Examine(target, static () => default(T)!).WriteAside)
                throw new IOException("Le fichier de récupération économique est illisible et verrouillé ; original conservé.");

            if (File.Exists(target))
            {
                string previous = File.ReadAllText(target);
                _ = Decode<T>(previous);

                // L'ancienne version saine DOIT être recopiée et vérifiée avant de remplacer la courante.
                // Contrairement au fallback de File.Replace, un échec de cette étape bloque le Save.
                string backup = target + ".bak";
                if (File.Exists(backup) && Examine(backup, static () => default(T)!).WriteAside)
                    throw new IOException("La copie de secours économique est verrouillée ; fichier courant conservé.");
                AtomicJson.Write(backup, previous);
                if (!string.Equals(File.ReadAllText(backup), previous, StringComparison.Ordinal))
                    throw new IOException("La copie de secours du fichier économique n'a pas pu être vérifiée.");
            }

            AtomicJson.Write(target, JsonSerializer.Serialize(value, Opts));
        }
    }
}

/// <summary>Séances de forgemagie persistées.</summary>
public sealed class FmLedgerStore
{
    public List<FmSession> Sessions { get; set; } = new();
}
