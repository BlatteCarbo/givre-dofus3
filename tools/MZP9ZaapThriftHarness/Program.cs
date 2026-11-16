using System.Buffers.Binary;
using System.Text;
using GivreLauncher.Core.Zaap;

int checks = 0;
void Check(bool condition, string message)
{
    if (!condition) throw new Exception("MZP9 Thrift contract: " + message);
    checks++;
}
static void I32(Stream s, int n)
{
    Span<byte> b = stackalloc byte[4]; BinaryPrimitives.WriteInt32BigEndian(b, n); s.Write(b);
}
static void Field(Stream s, short id, string value)
{
    s.WriteByte(11); s.WriteByte((byte)(id >> 8)); s.WriteByte((byte)id);
    var b = Encoding.UTF8.GetBytes(value); I32(s, b.Length); s.Write(b);
}
static void FieldI32(Stream s, short id, int value)
{
    s.WriteByte(8); s.WriteByte((byte)(id >> 8)); s.WriteByte((byte)id); I32(s, value);
}
static byte[] Call(string method, bool framed = false, bool duplicate = false, bool missing = false)
{
    using var ms = new MemoryStream();
    I32(ms, unchecked((int)0x80010001u));
    var n = Encoding.UTF8.GetBytes(method); I32(ms, n.Length); ms.Write(n); I32(ms, 19);
    if (method == "connect")
    {
        Field(ms, 1, "dofus3"); Field(ms, 2, "release");
        FieldI32(ms, 3, 4); if (!missing) Field(ms, 4, "opaque-test-session");
        if (duplicate) Field(ms, 1, "duplicate");
    }
    else
    {
        Field(ms, 1, "opaque-session"); if (!missing) FieldI32(ms, 2, 1);
        if (method == "auth_getGameTokenWithWindowId") FieldI32(ms, 3, 7);
        if (duplicate) Field(ms, 1, "duplicate");
    }
    ms.WriteByte(0);
    var unframed = ms.ToArray();
    if (!framed) return unframed;
    var result = new byte[unframed.Length + 4];
    BinaryPrimitives.WriteInt32BigEndian(result, unframed.Length);
    unframed.CopyTo(result, 4); return result;
}
foreach (var method in new[] { "connect", "auth_getGameToken", "auth_getGameTokenWithWindowId" })
{
    foreach (var framed in new[] { false, true })
    {
        var bytes = Call(method, framed);
        Check(MZP9ZaapThriftBinary.TryReadCall(bytes, framed, out var parsed), "typed request admitted");
        Check(parsed is { Method: var m, Sequence: 19 } && m == method, "method/sequence preserved");
        Check(method != "connect" ? parsed?.GameId == 1 : parsed?.Hash == "opaque-test-session",
            "required fields present");
        if (method == "auth_getGameTokenWithWindowId")
            Check(parsed?.WindowId == 7, "WindowId preserved, never silently dropped");
        Check(!MZP9ZaapThriftBinary.TryReadCall(bytes, !framed, out _), "framing mode cannot auto-fallback");
        var reply = MZP9ZaapThriftBinary.WriteStringReply(method, 19, "verified-test-token", framed);
        var payload = framed ? reply.AsSpan(4) : reply.AsSpan();
        Check(BinaryPrimitives.ReadUInt32BigEndian(payload) == 0x80010002u,
            "Thrift strict REPLY wire header");
        Check(!framed || BinaryPrimitives.ReadInt32BigEndian(reply) == reply.Length - 4,
            "prefix matches reply length");
    }
    Check(!MZP9ZaapThriftBinary.TryReadCall(Call(method, duplicate: true), false, out _),
        "duplicate singular refused");
    Check(!MZP9ZaapThriftBinary.TryReadCall(Call(method, missing: true), false, out _),
        "missing required field refused");
}
var baseline = Call("auth_getGameToken");
var wrongType = (byte[])baseline.Clone(); wrongType[3] = 2;
Check(!MZP9ZaapThriftBinary.TryReadCall(wrongType, false, out _), "REPLY not accepted as incoming CALL");
var wrongVersion = (byte[])baseline.Clone(); wrongVersion[1] = 2;
Check(!MZP9ZaapThriftBinary.TryReadCall(wrongVersion, false, out _), "version drift fails");
var trailing = baseline.Concat(new byte[] { 0x10 }).ToArray();
Check(!MZP9ZaapThriftBinary.TryReadCall(trailing, false, out _), "trailing-data smuggling rejected");
var prefixed = Call("connect", framed: true);
prefixed[3] ^= 1;
Check(!MZP9ZaapThriftBinary.TryReadCall(prefixed, true, out _), "length mismatch rejected");
Check(!MZP9ZaapThriftBinary.TryReadCall(new byte[1_048_577], false, out _), "budget capped");
var unsupported = Call("settings_set");
Check(!MZP9ZaapThriftBinary.TryReadCall(unsupported, false, out _), "unreviewed method rejected");
Console.WriteLine($"MZP9 Zaap candidate strict Thrift: PASS ({checks} offline assertions). Not authenticated game traffic.");
