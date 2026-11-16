using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using GivreClone.Engine;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using Xunit;

namespace GivreClone.Tests;

/// <summary>Lot K (confort) : forges MapInformationRequest / PlayerStatusUpdateRequest / ResetCharacterCharacteristicsRequest,
/// lecteurs de défis actifs (ChallengeAdd/Targets/List/Result) contre le VRAI catalogue (data/), et règles pures HTTP
/// (ScriptHttpGuard : URL, adresses publiques, en-têtes).</summary>
public class ComfortLotTests
{
    private static ProtocolCatalog Catalog() => ProtocolCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data"));
    private static int F(string message, string name) => Catalog().FieldNumber(message, name)
        ?? throw new InvalidOperationException($"Missing {message}.{name} in bundled fields.json");

    // --- encodeur protobuf minimal (copié de Patch361112Tests) ---
    private static byte[] Cat(params byte[][] parts)
    {
        int n = 0;
        foreach (var p in parts) n += p.Length;
        var b = new byte[n];
        int o = 0;
        foreach (var p in parts) { p.CopyTo(b, o); o += p.Length; }
        return b;
    }
    private static byte[] Var(ulong v)
    {
        var l = new List<byte>();
        do { byte c = (byte)(v & 0x7F); v >>= 7; if (v != 0) c |= 0x80; l.Add(c); } while (v != 0);
        return l.ToArray();
    }
    private static byte[] Tag(int field, int wire) => Var((ulong)((field << 3) | wire));
    private static byte[] I(int field, long v) => Cat(Tag(field, 0), Var(unchecked((ulong)v)));
    private static byte[] M(int field, params byte[][] parts)
    {
        var b = Cat(parts);
        return Cat(Tag(field, 2), Var((ulong)b.Length), b);
    }
    private static IReadOnlyList<ProtoField> Parse(params byte[][] parts) => RawProtobuf.Parse(Cat(parts));

    // Déballe une trame forgée -> (code, payload).
    private static (string Code, IReadOnlyList<ProtoField> Payload) Request(byte[] framed)
    {
        Assert.NotEmpty(framed);
        var frames = RawProtobuf.ConsumeFrames(framed, out var rest);
        Assert.Single(frames);
        Assert.Empty(rest);
        var hits = RawProtobuf.FindTypeUrls(RawProtobuf.Parse(frames[0]));
        Assert.Single(hits);
        return (hits[0].Code, hits[0].Payload ?? new List<ProtoField>());
    }
    private static long V(IReadOnlyList<ProtoField> p, int field) => unchecked((long)(p.First(f => f.Field == field && f.Wire == WireType.Varint).Varint));

    // =============================================================== forges
    [Fact]
    public void MapInformationRequest_carries_map_id()
    {
        var cat = Catalog();
        var (code, p) = Request(MoveForge.BuildMapInformationRequestFramed(191104002));
        Assert.Equal(cat.CodeForName("MapInformationRequest"), code);
        Assert.Equal(191104002, V(p, F("MapInformationRequest", "map_id")));
    }

    [Fact]
    public void PlayerStatus_wraps_CharacterStatus_status_only()
    {
        var cat = Catalog();
        var (code, p) = Request(MoveForge.BuildPlayerStatusFramed(4));
        Assert.Equal(cat.CodeForName("PlayerStatusUpdateRequest"), code);
        int fOuter = cat.FieldNumber("PlayerStatusUpdateRequest", "status") ?? -1, fInner = cat.FieldNumber("CharacterStatus", "status") ?? -1;
        Assert.True(fOuter > 0 && fInner > 0);
        var sub = Assert.Single(p, f => f.Field == fOuter).Message;
        Assert.NotNull(sub);
        Assert.Equal(4, V(sub!, fInner));
        Assert.DoesNotContain(sub!, f => f.Field == F("CharacterStatus", "gbpe"));
        Assert.DoesNotContain(p, f => f.Field == F("PlayerStatusUpdateRequest", "fzeu"));
    }

    [Fact]
    public void ResetCharacteristics_is_empty()
    {
        var (code, p) = Request(MoveForge.BuildResetCharacteristicsFramed());
        Assert.Equal(Catalog().CodeForName("ResetCharacterCharacteristicsRequest"), code);
        Assert.Empty(p);
    }

    // =============================================================== défis actifs
    [Fact]
    public void ChallengeAdd_reads_nested_challenge_and_signed_target()
    {
        var c = FightPrepReader.ReadChallengeCarrier("ChallengeAddEvent",
            Parse(I(F("ChallengeAddEvent", "fzql"), 7),
                M(F("ChallengeAddEvent", "challenge"),
                    I(F("Challenge", "drop_bonus"), 20),
                    M(F("Challenge", "targets"),
                        I(F("ChallengeTarget", "target_id"), -20004),
                        I(F("ChallengeTarget", "target_cell"), 312)),
                    I(F("Challenge", "xp_bonus"), 30),
                    I(F("Challenge", "challenge_id"), 12))));
        Assert.NotNull(c);
        Assert.Equal(12, c!.Id);
        Assert.Equal(20, c.DropBonus);
        Assert.Equal(30, c.XpBonus);
        Assert.Equal(0, c.State);
        var t = Assert.Single(c.Targets);
        Assert.Equal(-20004, t.TargetId);
        Assert.Equal(312, t.TargetCell);
    }

    [Fact]
    public void ChallengeTargets_uses_same_carrier()
    {
        var c = FightPrepReader.ReadChallengeCarrier("ChallengeTargetsEvent",
            Parse(M(F("ChallengeTargetsEvent", "challenge"),
                M(F("Challenge", "targets"),
                    I(F("ChallengeTarget", "target_id"), -5),
                    I(F("ChallengeTarget", "target_cell"), 100)),
                I(F("Challenge", "challenge_id"), 974))));
        Assert.NotNull(c);
        Assert.Equal(974, c!.Id);
        Assert.Equal((-5L, 100), (c.Targets[0].TargetId, c.Targets[0].TargetCell));
    }

    [Fact]
    public void ChallengeList_reads_all_entries()
    {
        var list = FightPrepReader.ReadChallengeList(Parse(
            M(F("ChallengeListEvent", "challenges"), I(F("Challenge", "challenge_id"), 3)),
            M(F("ChallengeListEvent", "challenges"),
                I(F("Challenge", "challenge_id"), 974), I(F("Challenge", "state"), 1))));
        Assert.Equal(new[] { 3, 974 }, list.Select(x => x.Id).ToArray());
        Assert.Equal(1, list[1].State);
    }

    [Fact]
    public void ChallengeResult_success_omitted_is_false()
    {
        Assert.Equal((12, false), FightPrepReader.ReadChallengeResult(Parse(I(F("ChallengeResultEvent", "challenge_id"), 12))));
        Assert.Equal((12, true), FightPrepReader.ReadChallengeResult(Parse(
            I(F("ChallengeResultEvent", "challenge_id"), 12), I(F("ChallengeResultEvent", "success"), 1))));
        Assert.Null(FightPrepReader.ReadChallengeResult(Parse()));
    }

    // =============================================================== HTTP (règles pures)
    [Theory]
    [InlineData("127.0.0.1")] [InlineData("10.1.2.3")] [InlineData("172.20.0.1")] [InlineData("192.168.1.1")]
    [InlineData("169.254.1.1")] [InlineData("100.64.0.1")] [InlineData("0.0.0.0")] [InlineData("::1")]
    [InlineData("fe80::1")] [InlineData("fd00::1")] [InlineData("::ffff:127.0.0.1")] [InlineData("2002:7f00:0001::")]
    [InlineData("64:ff9b::a00:1")] [InlineData("224.0.0.1")] [InlineData("2001:db8::1")]
    public void Private_addresses_are_blocked(string ip) => Assert.False(ScriptHttpGuard.IsPublicAddress(IPAddress.Parse(ip)));

    [Theory]
    [InlineData("1.1.1.1")] [InlineData("8.8.8.8")] [InlineData("2606:4700:4700::1111")] [InlineData("::ffff:8.8.8.8")]
    public void Public_addresses_are_allowed(string ip) => Assert.True(ScriptHttpGuard.IsPublicAddress(IPAddress.Parse(ip)));

    [Theory]
    [InlineData("http://example.com")] [InlineData("https://u:p@example.com")] [InlineData("https://localhost/x")]
    [InlineData("https://127.0.0.1/")] [InlineData("https://printer.local")] [InlineData("ftp://x")]
    [InlineData("https://[::1]/")] [InlineData("https://2130706433/")] [InlineData("https://api.internal/x")] [InlineData("")]
    public void Bad_urls_are_refused(string url) => Assert.False(ScriptHttpGuard.TryValidateUrl(url, out _, out _));

    [Fact]
    public void Long_url_is_refused() => Assert.False(ScriptHttpGuard.TryValidateUrl("https://example.com/" + new string('a', 3000), out _, out _));

    [Fact]
    public void Https_public_url_is_accepted()
    {
        Assert.True(ScriptHttpGuard.TryValidateUrl("https://example.com/a?b=1", out var uri, out var err));
        Assert.Equal("example.com", uri!.Host);
        Assert.Equal("", err);
    }

    [Fact]
    public void Headers_are_filtered()
    {
        Assert.False(ScriptHttpGuard.IsAllowedHeader("Host", "x"));
        Assert.False(ScriptHttpGuard.IsAllowedHeader("X-A", "a\r\nb"));
        Assert.False(ScriptHttpGuard.IsAllowedHeader("Bad Name", "x"));
        Assert.False(ScriptHttpGuard.IsAllowedHeader("Proxy-Authorization", "x"));
        Assert.False(ScriptHttpGuard.IsAllowedHeader("Transfer-Encoding", "chunked"));
        Assert.True(ScriptHttpGuard.IsAllowedHeader("Authorization", "Bearer x"));
        Assert.True(ScriptHttpGuard.IsAllowedHeader("User-Agent", "custom"));
        Assert.True(ScriptHttpGuard.IsAllowedHeader("Content-Type", "application/json"));
    }
}
