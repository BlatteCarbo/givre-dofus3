using System.Buffers.Binary;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using GivreClone.App.Services;
using GivreCore.Injection;

enum HarnessMode { Baseline, Turbo }

static class Program
{
    private static readonly CancellationTokenSource Stop = new();

    public static async Task<int> Main(string[] args)
    {
        try
        {
            var options = Options.Parse(args);
            Console.CancelKeyPress += (_, e) => { e.Cancel = true; Stop.Cancel(); };

            Console.WriteLine($"HARNESS mode={options.Mode} listen=127.0.0.1:{options.Port} started={DateTimeOffset.Now:O}");
            var relay = RunRelayAsync(options.Port, Stop.Token);

            int pid = options.Pid ?? await WaitForNewDofusAsync(options.AfterUtc, Stop.Token);
            using var process = Process.GetProcessById(pid);
            if (!string.Equals(process.ProcessName, "Dofus", StringComparison.OrdinalIgnoreCase))
                throw new InvalidOperationException($"PID {pid} n'est pas Dofus.exe");
            Console.WriteLine($"OWNED_DOFUS pid={pid} start={process.StartTime.ToUniversalTime():O}");

            if (!string.IsNullOrWhiteSpace(options.GivreKama))
            {
                if (!string.IsNullOrWhiteSpace(options.HwSeed))
                {
                    DllInjector.SetEnvironmentVariable(pid, "GIVRE_HWSEED", options.HwSeed);
                    Console.WriteLine($"HWSEED_SET pid={pid} chars={options.HwSeed.Length}");
                }
                DllInjector.Inject(pid, options.GivreKama);
                bool loaded = await WaitModuleAsync(pid, "GivreKama.dll", TimeSpan.FromSeconds(5), Stop.Token);
                Console.WriteLine($"GIVREKAMA loaded={loaded} pid={pid}");
                if (!loaded) throw new InvalidOperationException("GivreKama.dll non confirmé dans le Dofus de test");
            }

            bool turboActive = false;
            var stateGate = new SemaphoreSlim(1, 1);

            async Task SetTurboAsync(bool enabled)
            {
                await stateGate.WaitAsync();
                try
                {
                    if (enabled == turboActive)
                    {
                        Console.WriteLine($"TURBO_STATE pid={pid} active={turboActive} unchanged=true at={DateTimeOffset.Now:O}");
                        return;
                    }

                    if (enabled)
                    {
                        Console.WriteLine($"TURBO_ENABLE_REQUEST pid={pid} at={DateTimeOffset.Now:O}");
                        NativeTurboRuntime.RequestState(pid, enabled: true, combatSpeedHack: true);
                        bool speed = await WaitModuleAsync(pid, NativeTurboRuntime.SpeedHackModuleName, TimeSpan.FromSeconds(15), CancellationToken.None);
                        bool skipper = await WaitModuleAsync(pid, NativeTurboRuntime.SkipperModuleName, TimeSpan.FromSeconds(2), CancellationToken.None);
                        Console.WriteLine($"TURBO_ARMED pid={pid} speedhack={speed} skipper={skipper} runtime={NativeTurboRuntime.RuntimeVersion} at={DateTimeOffset.Now:O}");
                        if (!speed || !skipper) throw new InvalidOperationException("runtime Turbo non confirmé");
                        turboActive = true;
                    }
                    else
                    {
                        Console.WriteLine($"TURBO_DISABLE_REQUEST pid={pid} at={DateTimeOffset.Now:O}");
                        NativeTurboRuntime.RequestState(pid, enabled: false);
                        await WaitModuleAbsentAsync(pid, NativeTurboRuntime.SpeedHackModuleName, TimeSpan.FromSeconds(8));
                        bool stillLoaded = NativeModules.IsLoaded(pid, NativeTurboRuntime.SpeedHackModuleName);
                        Console.WriteLine($"TURBO_DISABLED pid={pid} speedhackLoaded={stillLoaded} at={DateTimeOffset.Now:O}");
                        if (stillLoaded) throw new InvalidOperationException("speedhack encore chargé après demande de restauration");
                        turboActive = false;
                    }
                }
                finally { stateGate.Release(); }
            }

            if (options.Mode == HarnessMode.Turbo)
                await SetTurboAsync(true);
            else
                Console.WriteLine($"BASELINE_ARMED pid={pid} turbo=false at={DateTimeOffset.Now:O}");

            Console.WriteLine("COMMANDS baseline | turbo | status | stop");
            _ = Task.Run(async () =>
            {
                while (!Stop.IsCancellationRequested)
                {
                    string? line = await Console.In.ReadLineAsync();
                    string command = line?.Trim().ToLowerInvariant() ?? "stop";
                    try
                    {
                        switch (command)
                        {
                            case "turbo":
                                await SetTurboAsync(true);
                                break;
                            case "baseline":
                                await SetTurboAsync(false);
                                Console.WriteLine($"BASELINE_ARMED pid={pid} turbo=false at={DateTimeOffset.Now:O}");
                                break;
                            case "status":
                                Console.WriteLine(
                                    $"STATUS pid={pid} turbo={turboActive} " +
                                    $"givreKama={NativeModules.IsLoaded(pid, "GivreKama.dll")} " +
                                    $"skipper={NativeModules.IsLoaded(pid, NativeTurboRuntime.SkipperModuleName)} " +
                                    $"speedhack={NativeModules.IsLoaded(pid, NativeTurboRuntime.SpeedHackModuleName)} " +
                                    $"at={DateTimeOffset.Now:O}");
                                break;
                            case "stop":
                                Stop.Cancel();
                                return;
                            case "":
                                break;
                            default:
                                Console.WriteLine($"UNKNOWN_COMMAND {command}");
                                break;
                        }
                    }
                    catch (Exception ex)
                    {
                        Console.Error.WriteLine($"COMMAND_ERROR {command} {ex.GetType().Name}: {ex.Message}");
                    }
                }
            });

            while (!Stop.IsCancellationRequested && !process.HasExited)
                await Task.Delay(250, Stop.Token).ContinueWith(_ => { }, TaskScheduler.Default);

            if (turboActive && !process.HasExited)
            {
                try { await SetTurboAsync(false); }
                catch (Exception ex)
                {
                    Console.Error.WriteLine($"RESTORE_ERROR pid={pid} {ex.GetType().Name}: {ex.Message}");
                }
            }
            if (!process.HasExited) NativeTurboRuntime.Forget(pid);

            Stop.Cancel();
            try { await relay; } catch (OperationCanceledException) { }
            Console.WriteLine($"HARNESS_STOP mode={options.Mode} at={DateTimeOffset.Now:O}");
            return 0;
        }
        catch (OperationCanceledException)
        {
            return 0;
        }
        catch (Exception ex)
        {
            Console.Error.WriteLine($"HARNESS_ERROR {ex.GetType().Name}: {ex.Message}");
            return 1;
        }
    }

    private static async Task<int> WaitForNewDofusAsync(DateTime afterUtc, CancellationToken ct)
    {
        Console.WriteLine($"WAIT_DOFUS afterUtc={afterUtc:O}");
        while (true)
        {
            ct.ThrowIfCancellationRequested();
            foreach (var p in Process.GetProcessesByName("Dofus"))
            {
                using (p)
                {
                    try
                    {
                        var start = p.StartTime.ToUniversalTime();
                        if (start >= afterUtc)
                            return p.Id;
                    }
                    catch { }
                }
            }
            await Task.Delay(100, ct);
        }
    }

    private static async Task<bool> WaitModuleAsync(int pid, string module, TimeSpan timeout, CancellationToken ct)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            ct.ThrowIfCancellationRequested();
            if (NativeModules.IsLoaded(pid, module)) return true;
            await Task.Delay(100, ct);
        }
        return NativeModules.IsLoaded(pid, module);
    }

    private static async Task WaitModuleAbsentAsync(int pid, string module, TimeSpan timeout)
    {
        var sw = Stopwatch.StartNew();
        while (sw.Elapsed < timeout)
        {
            try { if (!NativeModules.IsLoaded(pid, module)) return; } catch { return; }
            await Task.Delay(100);
        }
    }

    private static async Task RunRelayAsync(int port, CancellationToken ct)
    {
        var listener = new TcpListener(IPAddress.Loopback, port);
        listener.Start();
        Console.WriteLine($"MITM_RELAY_LISTEN 127.0.0.1:{port}");
        try
        {
            while (!ct.IsCancellationRequested)
            {
                TcpClient local = await listener.AcceptTcpClientAsync(ct);
                _ = Task.Run(() => HandleRelayAsync(local, ct), ct);
            }
        }
        finally { listener.Stop(); }
    }

    private static async Task HandleRelayAsync(TcpClient local, CancellationToken ct)
    {
        using (local)
        {
            local.NoDelay = true;
            NetworkStream downstream = local.GetStream();
            byte[] handshake = new byte[26];
            await ReadExactAsync(downstream, handshake, ct);

            byte[] addr = handshake.AsSpan(0, 16).ToArray();
            int addrLen = BinaryPrimitives.ReadInt32LittleEndian(handshake.AsSpan(16, 4));
            ushort port = BinaryPrimitives.ReadUInt16LittleEndian(handshake.AsSpan(20, 2));
            uint pid = BinaryPrimitives.ReadUInt32LittleEndian(handshake.AsSpan(22, 4));
            var ip = new IPAddress(addr);
            if (ip.IsIPv4MappedToIPv6) ip = ip.MapToIPv4();
            Console.WriteLine($"MITM_ACCEPT pid={pid} dest={ip}:{port} addrlen={addrLen} at={DateTimeOffset.Now:O}");

            using var upstream = new TcpClient(ip.AddressFamily) { NoDelay = true };
            await upstream.ConnectAsync(ip, port, ct);
            NetworkStream server = upstream.GetStream();
            Task a = downstream.CopyToAsync(server, ct);
            Task b = server.CopyToAsync(downstream, ct);
            await Task.WhenAny(a, b);
            Console.WriteLine($"MITM_CLOSE pid={pid} at={DateTimeOffset.Now:O}");
        }
    }

    private static async Task ReadExactAsync(Stream stream, byte[] buffer, CancellationToken ct)
    {
        int read = 0;
        while (read < buffer.Length)
        {
            int n = await stream.ReadAsync(buffer.AsMemory(read, buffer.Length - read), ct);
            if (n == 0) throw new EndOfStreamException("handshake GivreKama tronqué");
            read += n;
        }
    }

    private sealed record Options(HarnessMode Mode, int Port, int? Pid, string? GivreKama, string? HwSeed, DateTime AfterUtc)
    {
        public static Options Parse(string[] args)
        {
            HarnessMode mode = HarnessMode.Baseline;
            int port = 5658;
            int? pid = null;
            string? givreKama = null;
            string? hwSeed = null;
            DateTime after = DateTime.UtcNow.AddSeconds(-1);
            for (int i = 0; i < args.Length; i++)
            {
                switch (args[i])
                {
                    case "--mode":
                        mode = Enum.Parse<HarnessMode>(args[++i], ignoreCase: true);
                        break;
                    case "--port":
                        port = int.Parse(args[++i]);
                        break;
                    case "--pid":
                        pid = int.Parse(args[++i]);
                        break;
                    case "--givre-kama":
                        givreKama = args[++i];
                        break;
                    case "--hw-seed":
                        hwSeed = args[++i];
                        break;
                    case "--after-utc":
                        after = DateTime.Parse(args[++i], null, System.Globalization.DateTimeStyles.AdjustToUniversal | System.Globalization.DateTimeStyles.AssumeUniversal);
                        break;
                }
            }
            return new Options(mode, port, pid, givreKama, hwSeed, after);
        }
    }
}
