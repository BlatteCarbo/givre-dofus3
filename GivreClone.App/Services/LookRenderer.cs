using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Threading.Tasks;

namespace GivreClone.App.Services;

/// <summary>Image d'un personnage à partir de sa chaîne de look, via le moteur de rendu public d'Ankama (static.ankama.com/dofus/renderer). Cache disque par chaîne de look ;
/// un échec n'est pas réessayé avant 5 minutes. Le look vient des paquets (rien n'est envoyé au jeu).</summary>
public static class LookRenderer
{
    public const string Url = "https://static.ankama.com/dofus/renderer/look/{0}/full/1/300_450-10.png";
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(2.5) };
    private static readonly ConcurrentDictionary<string, long> Failed = new();
    private const long RetryAfterMs = 5 * 60 * 1000;

    /// <summary>Dernière cause d'échec du rendu (code HTTP, réseau…), pour l'afficher au lieu d'un message vague.</summary>
    public static string LastFailure { get; private set; } = "";

    public static string Folder => Path.Combine(MZP19LocalClone.DataRoot, "looks");

    /// <summary>Hex UTF-8 de la chaîne de look : la forme attendue dans l'URL du moteur de rendu.</summary>
    public static string HexOf(string look) => Convert.ToHexString(Encoding.UTF8.GetBytes(look)).ToLowerInvariant();

    public static string CachePath(string look) => Path.Combine(Folder, Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(look)))[..24].ToLowerInvariant() + ".png");

    /// <summary>Chemin local de l'image (téléchargée si besoin) ; null si indisponible.</summary>
    public static async Task<string?> EnsureAsync(string look, CancellationToken ct = default)
    {
        string path = CachePath(look);
        if (File.Exists(path)) return path;
        if (Failed.TryGetValue(look, out var at) && Environment.TickCount64 - at < RetryAfterMs) return null;
        try
        {
            Directory.CreateDirectory(Folder);
            using var resp = await Http.GetAsync(string.Format(Url, HexOf(look)), ct).ConfigureAwait(false);
            if (!resp.IsSuccessStatusCode) { Failed[look] = Environment.TickCount64; LastFailure = "HTTP " + (int)resp.StatusCode; return null; }
            var bytes = await resp.Content.ReadAsByteArrayAsync(ct).ConfigureAwait(false);
            if (bytes.Length < 500) { Failed[look] = Environment.TickCount64; LastFailure = "image vide"; return null; }
            await File.WriteAllBytesAsync(path + ".part", bytes, ct).ConfigureAwait(false);
            File.Move(path + ".part", path, overwrite: true);
            return path;
        }
        catch (Exception e) when (e is HttpRequestException or IOException or OperationCanceledException or UnauthorizedAccessException)
        {
            Failed[look] = Environment.TickCount64;
            LastFailure = e.GetType().Name;
            return null;
        }
    }
}
