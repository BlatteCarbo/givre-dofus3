using System;
using System.Buffers.Binary;
using System.IO;
using System.IO.Pipes;
using System.Text;
using System.Text.Json;
using System.Threading;
using System.Threading.Tasks;

namespace GivreClone.App.Services;

/// <summary>
/// Rich Presence Discord (« Joue à GivreClone ») — client IPC MINIMAL parlant directement le pipe nommé de Discord
/// (\\.\pipe\discord-ipc-0..9), SANS dépendance externe ni réflexion (compatible AOT). Affiche le nom de l'app + un
/// libellé + un chrono (temps écoulé) + 2 boutons (Discord / site). Désactivable (AppConfig.RichPresence).
///
/// ⚠️ PRÉREQUIS (une fois) : créer une Application Discord "GivreClone" sur https://discord.com/developers/applications,
/// y uploader un asset Rich Presence nommé <see cref="LargeImageKey"/>, et coller son Application ID dans <see cref="AppId"/>.
/// Tant que <see cref="AppId"/> reste le placeholder, la présence est un NO-OP (aucune connexion, aucune erreur).
/// </summary>
public static class DiscordRichPresence
{
    // ==== À RENSEIGNER (public, pas un secret — embarqué dans le client) ==========================================
    private const string AppId = "1551577335315173450";  // Application ID de l'app Discord "GivreClone" (public)
    private const string LargeImageKey = "logo";        // clé de l'asset Rich Presence uploadé sur l'app Discord
    private const string LargeImageText = "GivreClone";
    // 2 lignes COURTES (Discord tronque ~30 car. par champ et n'autorise pas de retour à la ligne dans un champ) :
    // on répartit l'info sur les 2 lignes visibles (details puis state) plutôt que de tout entasser sur la première.
    private const string Details = "Bot Dofus 3 · Multi-comptes";
    private const string State = "HDV · Combat · Récolte auto";
    private const string BtnDiscordLabel = "Rejoindre le Discord";
    private const string BtnDiscordUrl = Links.DiscordInvite;
    private const string BtnSiteLabel = "Visiter le site web";
    private const string BtnSiteUrl = "https://example.invalid";
    // =============================================================================================================

    private const long ReconnectDelayMs = 15000;
    private static readonly long StartUnix = DateTimeOffset.UtcNow.ToUnixTimeSeconds();   // chrono « temps écoulé »
    private static readonly object _lock = new();
    private static CancellationTokenSource? _cts;

    /// <summary>Configurée = un Application ID a été renseigné. Sinon la présence ne tente rien.</summary>
    public static bool Configured => AppId.Length >= 17 && ulong.TryParse(AppId, out _);

    /// <summary>Démarre ou arrête la présence selon AppConfig.RichPresence (à appeler au lancement ET au toggle).</summary>
    public static void ApplyFromConfig()
    {
        if (AppConfig.Current.RichPresence && Configured) Start();
        else Stop();
    }

    private static void Start()
    {
        lock (_lock)
        {
            if (_cts is not null) return;                 // déjà en cours
            _cts = new CancellationTokenSource();
            var ct = _cts.Token;
            _ = Task.Run(() => RunLoopAsync(ct), ct);
        }
    }

    private static void Stop()
    {
        // On NE dispose PAS le CTS ici : la boucle en cours peut encore l'utiliser (Task.Delay(ct)) -> Dispose provoquerait
        // un ObjectDisposedException. Cancel + null suffit (pas de handle non managé tant qu'on ne touche pas .WaitHandle).
        lock (_lock) { _cts?.Cancel(); _cts = null; }
    }

    // Boucle de vie : (re)connexion au pipe Discord, handshake, pose de l'activité, puis keep-alive (PING->PONG).
    // Discord fermé / pas lancé -> exception -> on réessaie après ReconnectDelayMs. L'annulation coupe proprement.
    private static async Task RunLoopAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            NamedPipeClientStream? pipe = null;
            try
            {
                pipe = await ConnectAsync(ct).ConfigureAwait(false);
                if (pipe is null) { await DelaySafe(ReconnectDelayMs, ct).ConfigureAwait(false); continue; }

                await WriteFrameAsync(pipe, 0, HandshakeJson(), ct).ConfigureAwait(false);   // op 0 = HANDSHAKE
                await ReadFrameAsync(pipe, ct).ConfigureAwait(false);                        // READY (ignoré)
                await WriteFrameAsync(pipe, 1, SetActivityJson(), ct).ConfigureAwait(false); // op 1 = FRAME (SET_ACTIVITY)

                while (!ct.IsCancellationRequested)                                          // keep-alive
                {
                    var (op, data) = await ReadFrameAsync(pipe, ct).ConfigureAwait(false);
                    if (op == 3) await WriteFrameAsync(pipe, 4, data, ct).ConfigureAwait(false);   // PING -> PONG
                    else if (op == 2) break;                                                       // CLOSE
                }
            }
            catch (OperationCanceledException) { break; }
            catch { /* Discord absent / pipe cassé -> on retente */ }
            finally { try { pipe?.Dispose(); } catch { } }

            if (ct.IsCancellationRequested) break;
            await DelaySafe(ReconnectDelayMs, ct).ConfigureAwait(false);
        }
    }

    private static async Task<NamedPipeClientStream?> ConnectAsync(CancellationToken ct)
    {
        for (int i = 0; i < 10 && !ct.IsCancellationRequested; i++)
        {
            var pipe = new NamedPipeClientStream(".", "discord-ipc-" + i, PipeDirection.InOut, PipeOptions.Asynchronous);
            try { await pipe.ConnectAsync(2000, ct).ConfigureAwait(false); return pipe; }
            catch { try { pipe.Dispose(); } catch { } }
        }
        return null;
    }

    private static byte[] HandshakeJson()
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteNumber("v", 1);
            w.WriteString("client_id", AppId);
            w.WriteEndObject();
        }
        return ms.ToArray();
    }

    private static byte[] SetActivityJson()
    {
        using var ms = new MemoryStream();
        using (var w = new Utf8JsonWriter(ms))
        {
            w.WriteStartObject();
            w.WriteString("cmd", "SET_ACTIVITY");
            w.WriteString("nonce", Guid.NewGuid().ToString());
            w.WriteStartObject("args");
            w.WriteNumber("pid", Environment.ProcessId);
            w.WriteStartObject("activity");
            w.WriteString("details", Details);
            w.WriteString("state", State);
            w.WriteStartObject("timestamps");
            w.WriteNumber("start", StartUnix);
            w.WriteEndObject();
            w.WriteStartObject("assets");
            w.WriteString("large_image", LargeImageKey);
            w.WriteString("large_text", LargeImageText);
            w.WriteEndObject();
            w.WriteStartArray("buttons");
            w.WriteStartObject(); w.WriteString("label", BtnDiscordLabel); w.WriteString("url", BtnDiscordUrl); w.WriteEndObject();
            w.WriteStartObject(); w.WriteString("label", BtnSiteLabel); w.WriteString("url", BtnSiteUrl); w.WriteEndObject();
            w.WriteEndArray();
            w.WriteEndObject();   // activity
            w.WriteEndObject();   // args
            w.WriteEndObject();
        }
        return ms.ToArray();
    }

    // Trame IPC Discord : [op int32 LE][len int32 LE][payload UTF-8].
    private static async Task WriteFrameAsync(Stream s, int op, byte[] payload, CancellationToken ct)
    {
        var header = new byte[8];
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(0, 4), op);
        BinaryPrimitives.WriteInt32LittleEndian(header.AsSpan(4, 4), payload.Length);
        await s.WriteAsync(header, ct).ConfigureAwait(false);
        if (payload.Length > 0) await s.WriteAsync(payload, ct).ConfigureAwait(false);
        await s.FlushAsync(ct).ConfigureAwait(false);
    }

    private static async Task<(int op, byte[] data)> ReadFrameAsync(Stream s, CancellationToken ct)
    {
        var header = await ReadExactAsync(s, 8, ct).ConfigureAwait(false);
        int op = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(0, 4));
        int len = BinaryPrimitives.ReadInt32LittleEndian(header.AsSpan(4, 4));
        if ((uint)len > 1_000_000) throw new IOException("trame IPC démesurée");   // garde-fou
        var data = len > 0 ? await ReadExactAsync(s, len, ct).ConfigureAwait(false) : Array.Empty<byte>();
        return (op, data);
    }

    private static async Task<byte[]> ReadExactAsync(Stream s, int n, CancellationToken ct)
    {
        var buf = new byte[n];
        int off = 0;
        while (off < n)
        {
            int r = await s.ReadAsync(buf.AsMemory(off, n - off), ct).ConfigureAwait(false);
            if (r <= 0) throw new EndOfStreamException();
            off += r;
        }
        return buf;
    }

    private static async Task DelaySafe(long ms, CancellationToken ct)
    {
        try { await Task.Delay((int)ms, ct).ConfigureAwait(false); } catch { }
    }
}
