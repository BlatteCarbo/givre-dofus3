using System;
using System.Collections.Concurrent;
using System.IO;
using System.Net.Http;
using System.Threading.Tasks;
using Avalonia.Media.Imaging;

namespace GivreClone.App.Services;

/// <summary>Icônes de métiers (api.dofusdb.fr/img/jobs/&lt;iconId&gt;.png), téléchargées une fois puis gardées sur disque. Un échec n'est pas réessayé avant 10 minutes.</summary>
public static class JobIcons
{
    private static readonly HttpClient Http = new() { Timeout = TimeSpan.FromSeconds(15) };
    private static readonly ConcurrentDictionary<int, Bitmap?> Cache = new();
    private static readonly ConcurrentDictionary<int, long> Failed = new();

    private static string PathOf(int iconId) => Path.Combine(MZP19LocalClone.DataRoot, "jobicons", iconId.ToString(System.Globalization.CultureInfo.InvariantCulture) + ".png");

    /// <summary>Appelle <paramref name="onReady"/> (thread appelant arbitraire) quand l'icône est prête ; ne fait rien si elle est indisponible.</summary>
    public static async Task RequestAsync(int iconId, Action<Bitmap> onReady)
    {
        if (iconId <= 0) return;
        if (Cache.TryGetValue(iconId, out var hit) && hit is not null) { onReady(hit); return; }
        if (Failed.TryGetValue(iconId, out var at) && Environment.TickCount64 - at < 600_000) return;
        try
        {
            string p = PathOf(iconId);
            if (!File.Exists(p))
            {
                Directory.CreateDirectory(Path.GetDirectoryName(p)!);
                var bytes = await Http.GetByteArrayAsync($"https://api.dofusdb.fr/img/jobs/{iconId}.png").ConfigureAwait(false);
                if (bytes.Length < 200) { Failed[iconId] = Environment.TickCount64; return; }
                await File.WriteAllBytesAsync(p, bytes).ConfigureAwait(false);
            }
            var bmp = new Bitmap(p);
            Cache[iconId] = bmp;
            onReady(bmp);
        }
        catch (Exception e) when (e is HttpRequestException or IOException or ArgumentException or TaskCanceledException or UnauthorizedAccessException)
        {
            Failed[iconId] = Environment.TickCount64;
        }
    }
}
