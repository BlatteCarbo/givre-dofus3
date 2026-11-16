using System.Security.Cryptography;
using System.Text;

namespace GivreRelayAgent;

/// <summary>
/// GivreRelay — l'ami lance cet exe pour partager sa connexion avec un utilisateur GivreClone.
/// Premier lancement : saisie du code reçu (affiché sur le portail Givre), gardé chiffré pour cet utilisateur Windows (DPAPI).
/// Ensuite : connexion automatique, reconnexion automatique, état en direct.
///
/// Options : --code &lt;code&gt; (enregistre ce code), --reset (oublie le code), --server &lt;wss://…&gt; (essais uniquement).
/// Touches : P = pause / reprise, C = changer de code, Q = quitter.
/// </summary>
public static class Program
{
    public const string Version = "1.0.0";
    private const string DefaultServer = "wss://example.invalid/relay/agent";
    private static readonly object ConsoleLock = new();

    public static async Task<int> Main(string[] args)
    {
        try { Console.OutputEncoding = Encoding.UTF8; Console.Title = "GivreRelay"; } catch { /* sans console (sortie redirigée) */ }
        string? argCode = Arg(args, "--code");
        var server = new Uri(Arg(args, "--server") ?? DefaultServer);
        if (args.Contains("--reset")) { TokenStore.Clear(); Say("Code oublié."); }

        using var single = new Mutex(true, @"Local\GivreRelayAgent", out bool first);
        if (!first) { Say("GivreRelay est déjà lancé sur cet ordinateur."); Pause(); return 1; }

        Say($"GivreRelay {Version} — partage de connexion pour GivreClone");
        Say("Ce programme laisse passer, par votre connexion Internet, le trafic autorisé par le partage.");
        Say("Jamais vers votre réseau local (box, imprimante, autres appareils).");
        Say("");

        if (argCode is { Length: > 0 }) TokenStore.Save(argCode.Trim());
        using var quit = new CancellationTokenSource();
        Console.CancelKeyPress += (_, e) => { e.Cancel = true; quit.Cancel(); };

        while (!quit.IsCancellationRequested)
        {
            string? token = TokenStore.Load();
            if (string.IsNullOrEmpty(token))
            {
                token = AskCode();
                if (token is null) return 0;
                TokenStore.Save(token);
            }

            var link = new AgentLink(server, token);
            link.Log += m => Say($"[{DateTime.Now:HH:mm:ss}] {m}");
            link.Changed += () => UpdateTitle(link);
            using var session = CancellationTokenSource.CreateLinkedTokenSource(quit.Token);
            var run = link.RunAsync(session.Token);
            var ui = Task.Run(() => KeyLoop(link, session), session.Token);
            Say("[P] pause / reprise   [C] changer de code   [Q] quitter");
            using var status = new PeriodicTimer(TimeSpan.FromSeconds(2));
            string lastLine = "";
            while (!run.IsCompleted)
            {
                string line = StatusLine(link);
                if (line != lastLine) { Say(line); lastLine = line; }
                UpdateTitle(link);
                try { await Task.WhenAny(run, status.WaitForNextTickAsync(session.Token).AsTask()).ConfigureAwait(false); }
                catch (OperationCanceledException) { break; }
            }
            session.Cancel();
            try { await run.ConfigureAwait(false); } catch { }

            if (quit.IsCancellationRequested) break;
            if (link.State == LinkState.Replaced)
            {
                // Le même code tourne ailleurs : s'arrêter évite que les deux agents s'éjectent en boucle. Code gardé.
                Say("Ce code de partage vient d'être utilisé par un autre agent GivreRelay (autre ordinateur ou autre fenêtre).");
                Say("Cet agent s'arrête. Relancez-le ici si c'est bien cet ordinateur qui doit partager la connexion.");
                Pause();
                break;
            }
            if (link.State == LinkState.TokenRejected || _changeCode)
            {
                _changeCode = false;
                TokenStore.Clear();
                Say(link.State == LinkState.TokenRejected
                    ? "Ce code n'est plus valable. Demandez-en un nouveau à votre ami (portail Givre → Relais)."
                    : "Saisie d'un nouveau code.");
                continue;
            }
            break;
        }
        Say("GivreRelay arrêté.");
        return 0;
    }

    private static volatile bool _changeCode;

    private static void KeyLoop(AgentLink link, CancellationTokenSource session)
    {
        if (Console.IsInputRedirected) return;
        while (!session.IsCancellationRequested)
        {
            if (!Console.KeyAvailable) { Thread.Sleep(100); continue; }
            switch (char.ToUpperInvariant(Console.ReadKey(intercept: true).KeyChar))
            {
                case 'P':
                    link.SetPaused(!link.Paused);
                    Say(link.Paused ? "En pause : le partage apparaît hors ligne pour GivreClone." : "Reprise du partage…");
                    break;
                case 'C':
                    _changeCode = true;
                    session.Cancel();
                    return;
                case 'Q':
                    session.Cancel();
                    return;
            }
        }
    }

    private static string StatusLine(AgentLink l)
    {
        string state = l.State switch
        {
            LinkState.Connected => "connecté",
            LinkState.Connecting => "connexion…",
            LinkState.Reconnecting => "reconnexion…",
            LinkState.Paused => "en pause",
            LinkState.TokenRejected => "code refusé",
            LinkState.Replaced => "remplacé par un autre agent",
            _ => "arrêté",
        };
        if (l.State != LinkState.Connected)
            return $"État : {state}" + (l.LastError is { } e && l.State != LinkState.Paused ? $" ({e})" : "");
        return $"État : {state} — « {l.Label} » — {(l.ScopeAll ? "tout (web 80/443 + Dofus)" : "Dofus uniquement")} — " +
               $"{l.ActiveStreams} connexion(s) — {Usage(l)}" + (l.PublicIp is { } ip ? $" — IP {ip}" : "");
    }

    private static string Usage(AgentLink l)
    {
        static string Go(long b) => (b / 1024d / 1024d / 1024d).ToString("0.00");
        return l.QuotaBytes > 0 ? $"{Go(l.UsedBytes)} / {Go(l.QuotaBytes)} Go" : $"{Go(l.UsedBytes)} Go";
    }

    private static void UpdateTitle(AgentLink l)
    {
        try
        {
            Console.Title = l.State == LinkState.Connected
                ? $"GivreRelay — {l.Label} — {l.ActiveStreams} connexion(s) — {Usage(l)}"
                : $"GivreRelay — {StatusLine(l).Replace("État : ", "")}";
        }
        catch { /* pas de console */ }
    }

    private static string? AskCode()
    {
        if (Console.IsInputRedirected) { Say("Aucun code enregistré : lancez avec --code <code>."); return null; }
        while (true)
        {
            Say("Entrez le code de partage reçu (portail Givre → Relais), puis Entrée :");
            Console.Write("> ");
            string? code = Console.ReadLine()?.Trim();
            if (code is null) return null;
            if (code.Length >= 8) return code;
            Say("Code trop court, vérifiez-le.");
        }
    }

    private static string? Arg(string[] args, string name)
    {
        int i = Array.IndexOf(args, name);
        return i >= 0 && i + 1 < args.Length ? args[i + 1] : null;
    }

    private static void Say(string s) { lock (ConsoleLock) Console.WriteLine(s); }

    private static void Pause()
    {
        if (Console.IsInputRedirected) return;
        Say("Appuyez sur une touche…");
        try { Console.ReadKey(intercept: true); } catch { }
    }
}

/// <summary>Code du partage chiffré pour l'utilisateur Windows courant (DPAPI), dans %APPDATA%\GivreRelay.</summary>
internal static class TokenStore
{
    private static readonly byte[] Entropy = Encoding.UTF8.GetBytes("GivreRelay.agent.v1");
    private static string PathOf => System.IO.Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GivreRelay", "agent.dat");

    public static string? Load()
    {
        try
        {
            if (!OperatingSystem.IsWindows() || !File.Exists(PathOf)) return null;
            return Encoding.UTF8.GetString(ProtectedData.Unprotect(File.ReadAllBytes(PathOf), Entropy, DataProtectionScope.CurrentUser));
        }
        catch { return null; }   // fichier d'une autre session Windows / abîmé : on redemande le code
    }

    public static void Save(string token)
    {
        if (!OperatingSystem.IsWindows()) throw new PlatformNotSupportedException("GivreRelay requiert Windows.");
        Directory.CreateDirectory(System.IO.Path.GetDirectoryName(PathOf)!);
        var tmp = PathOf + ".tmp";
        File.WriteAllBytes(tmp, ProtectedData.Protect(Encoding.UTF8.GetBytes(token), Entropy, DataProtectionScope.CurrentUser));
        File.Move(tmp, PathOf, overwrite: true);
    }

    public static void Clear()
    {
        try { File.Delete(PathOf); } catch { }
    }
}
