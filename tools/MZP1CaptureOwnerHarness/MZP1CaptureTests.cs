using System.Security.Cryptography;
using GivreClone.Engine;
using GivreCore.Injection;
using GivreCore.Mitm;
using Xunit;

public sealed class MZP1CaptureTests
{
    [Fact]
    public void No_permit_means_no_proxy_no_scan_no_injection()
    {
        var proxy = new TcpProxyServer();
        var host = new DofusCaptureHost(proxy, "not-a-hook.dll");
        host.Start();
        Assert.False(host.IsRunning);
        Assert.Equal(0, proxy.Started);
        Assert.Equal(0, DllInjector.Invocations);
        host.Stop();
        Assert.Equal(0, proxy.Stopped);
    }

    [Fact]
    public void Invalid_version_and_process_birth_cannot_open_capture()
    {
        var proxy = new TcpProxyServer();
        var invalid = new MZP1OwnedClientPermit(0, 0, "3.6.12.16",
            "C:\\not-real\\Dofus.exe", new string('a', 64));
        var host = new DofusCaptureHost(proxy, "not-a-hook.dll", invalid);
        host.Start();
        Assert.False(host.IsRunning);
        Assert.Equal(0, proxy.Started);
    }

    [Fact]
    public void Valid_shape_is_not_proof_of_a_running_process()
    {
        var permit = new MZP1OwnedClientPermit(int.MaxValue, 638960000000000000L,
            "3.7.10.10", "C:\\fixture-only\\Dofus.exe", new string('a', 64));
        Assert.True(permit.IsWellFormed);
        Assert.False(permit.MatchesExecutableHash("C:\\another\\Dofus.exe"));
    }

    [Fact]
    public void Executable_hash_must_match_pinned_bytes_without_any_process_access()
    {
        var file = Path.Combine(Path.GetTempPath(), "mzp1-capture-test-" + Guid.NewGuid().ToString("N") + ".bin");
        try
        {
            byte[] fixture = [1, 2, 3, 4, 5];
            File.WriteAllBytes(file, fixture);
            var expected = Convert.ToHexString(SHA256.HashData(fixture));
            var permit = new MZP1OwnedClientPermit(1, 638960000000000000L,
                "3.7.10.10", file, expected);
            Assert.True(permit.MatchesExecutableHash(file));
            File.WriteAllBytes(file, [1, 2, 3, 4, 6]);
            Assert.False(permit.MatchesExecutableHash(file));
        }
        finally { if (File.Exists(file)) File.Delete(file); }
    }
}
