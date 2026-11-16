using System.Net;
using System.Net.Sockets;
using System.Text;
using GivreLauncher.Core.Launch;
using GivreLauncher.Core.Proxy;
using GivreLauncher.Core.Transport;
using GivreLauncher.Core.Zaap;

// MZP9: Only synthetic loopback TCP owned by this harness. The identity provider
// below is deliberately NOT a real Windows PID authority. It never enters App.
using var timeout = new CancellationTokenSource(TimeSpan.FromSeconds(12));
var ct = timeout.Token;
int checks = 0;
void Check(bool ok, string description)
{
    if (!ok) throw new InvalidOperationException("MZP9 test failed: " + description);
    checks++;
}
static int EphemeralPort()
{
    using var reservation = new TcpListener(IPAddress.Loopback, 0);
    reservation.Start();
    return ((IPEndPoint)reservation.LocalEndpoint).Port;
}
static byte[] Wire(string message) => Encoding.ASCII.GetBytes(message);
static async Task<string> ReadExactAsync(NetworkStream s, int count, CancellationToken ct)
{
    var buffer = new byte[count];
    await s.ReadExactlyAsync(buffer, ct);
    return Encoding.ASCII.GetString(buffer);
}
Check(!new GameLauncher().HasIndependentTestAuthority, "default launcher lacks independent provider");
Check(!MZP9OwnedClientRegistry.IsOwnedAndLive(Environment.ProcessId),
    "current harness process not registered as owned Dofus child");
Check(!MZP9OwnedClientRegistry.TryStopOwned(Environment.ProcessId),
    "refuse termination of current process without owned launcher handle");
Check(!MZP9OwnedClientRegistry.TryStopOwned(0x40000001),
    "never terminate a synthetic socket-PID by OS lookup");
using (var protectedZaap = new ZaapEmulator(26666))
    Check(!protectedZaap.HasIndependentTestAuthority, "historical Zaap port never admitted");
using (var separateZaap = new ZaapEmulator(EphemeralPort()))
    Check(!separateZaap.HasIndependentTestAuthority, "test Zaap without independent provider denied");

using var upstream = new TcpListener(IPAddress.Loopback, 0);
upstream.Start();
int upstreamPort = ((IPEndPoint)upstream.LocalEndpoint).Port;
int relayPort = EphemeralPort();
while (relayPort == upstreamPort || relayPort is 5657 or 5658 or 26666) relayPort = EphemeralPort();
var authority = new SyntheticHarnessAuthority(
    new AdmittedTestTunnel(811000, 638900000000000000, 9,
        new IPEndPoint(IPAddress.Loopback, upstreamPort), ConnectionServer: false));
using var relay = new HandshakeRelay(_ => null, relayPort, _ => true);
var closed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
var observed = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
int inbound = 0, outbound = 0, opened = 0;
relay.OnData = (_, clientToServer, bytes) =>
{
    if (clientToServer && bytes.AsSpan().SequenceEqual(Wire("PING")))
        Interlocked.Increment(ref outbound);
    else if (!clientToServer && bytes.AsSpan().SequenceEqual(Wire("PONG")))
    {
        Interlocked.Increment(ref inbound);
        observed.TrySetResult(true);
    }
};
relay.OnGameTunnelOpened = (_, _, _, _) => Interlocked.Increment(ref opened);
relay.OnClosed = (_, _, _) => closed.TrySetResult(true);
relay.ConfigureOwnedLoopbackTest(authority);
relay.Start();
var serverTask = Task.Run(async () =>
{
    using var server = await upstream.AcceptTcpClientAsync(ct);
    using var stream = server.GetStream();
    Check(await ReadExactAsync(stream, 4, ct) == "PING", "upstream received real duplex bytes");
    await stream.WriteAsync(Wire("PONG"), ct);
    Check(await ReadExactAsync(stream, 6, ct) == "ACTION", "injected TEST bytes reached upstream");
    return true;
}, ct);
using (var downstream = new TcpClient())
{
    await downstream.ConnectAsync(IPAddress.Loopback, relayPort, ct);
    using var stream = downstream.GetStream();
    await stream.WriteAsync(Wire("PING"), ct);
    Check(await ReadExactAsync(stream, 4, ct) == "PONG", "real server-to-client forwarding");
    await observed.Task.WaitAsync(ct);
    Check(opened == 1 && inbound == 1 && outbound == 1, "per-direction callbacks generated from actual bytes");
    Check(relay.Inject(authority.Permit.ProcessId, Wire("ACTION")), "admitted TEST write accepted (not an ACK)");
    Check(await serverTask.WaitAsync(ct), "TEST upstream completed");
    authority.Revoked = true;
    Check(!relay.Inject(authority.Permit.ProcessId, Wire("LATE")), "revocation blocks write");
    await closed.Task.WaitAsync(ct);
}
relay.Stop();
Check(!relay.Inject(authority.Permit.ProcessId, Wire("AFTER")), "teardown blocks old session");

// Negative: intentionally configured proxy must not silently fall back to raw
// socket routing even though a synthetic TEST admission returns a loopback target.
int deniedPort = EphemeralPort();
while (deniedPort == upstreamPort || deniedPort == relayPort || deniedPort is 5657 or 5658 or 26666)
    deniedPort = EphemeralPort();
using var denied = new HandshakeRelay(_ => new ProxySettings(ProxyKind.Socks5, "test.invalid", 47111),
    deniedPort, _ => true);
denied.ConfigureOwnedLoopbackTest(new SyntheticHarnessAuthority(authority.Permit));
denied.Start();
using (var deniedClient = new TcpClient())
{
    await deniedClient.ConnectAsync(IPAddress.Loopback, deniedPort, ct);
    using var deniedStream = deniedClient.GetStream();
    var one = new byte[1];
    Check(await deniedStream.ReadAsync(one, ct) == 0, "proxy-bound test client rejected before routing");
}
denied.Stop();
Console.WriteLine($"MZP9 TEST loopback duplex: PASS ({checks} checks). No Dofus, account, original GivreClone, or game ACK.");

internal sealed class SyntheticHarnessAuthority(AdmittedTestTunnel permit) : IIndependentTestHandshakeAuthority
{
    public AdmittedTestTunnel Permit => permit;
    public volatile bool Revoked;
    public Task<AdmittedTestTunnel?> AdmitAsync(TcpClient accepted, CancellationToken ct)
        => Task.FromResult<AdmittedTestTunnel?>(Revoked ? null : permit);
    public bool IsStillOwned(AdmittedTestTunnel admitted) => !Revoked && admitted == permit;
    public bool MayInject(AdmittedTestTunnel admitted) => IsStillOwned(admitted);
}
