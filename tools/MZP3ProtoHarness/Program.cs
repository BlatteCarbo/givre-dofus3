using System.Buffers.Binary;
using System.Text;
using GivreClone.Protocol;
using GivreRework.Infrastructure.Protocol.Raw;

static class Program
{
    static int _passes;

    static void Main()
    {
        Run("Varint canonical reference vectors + maximum unsigned", Varints);
        Run("Varint rejects overflow, unterminated, noncanonical", VarintNegatives);
        Run("Exact handcrafted all wire types and repeated unknown roundtrip", WireRoundtrip);
        Run("Unknown wire fields preserve original spelling in opt-in loose-value mode", UnknownPreservation);
        Run("Field bytes are defensive copies", FieldImmutability);
        Run("Bounds: total, count, single field and output", BudgetGuards);
        Run("Negative field numbers, reserved numbers, invalid wire, truncated data", InvalidFields);
        Run("Length-delimited bytes are opaque, strict UTF8 explicit", OpaquePayload);
        Run("Embedded message explicit and depth limit enforced", EmbeddedDepth);
        Run("Packed values and fixed endian orientation", FixedAndPacked);
        Run("Cross-implementation differential 0/1/2/5 and offsets", DifferentialAgainstP0);
        Run("Random deterministic multi-field decode/encode differential", RandomizedDifferential);
        Run("Malformed bounded random input never publishes partial data", MalformedFuzz);
        Run("Generic frame prefix: complete, coalesced, incomplete, invalid", Frames);
        Run("Envelope Any and request from independently supplied slots", Envelopes);
        Run("Envelope byte-layout matches explicit source-derived shape", GoldenEnvelope);
        Run("Invalid or ambiguous request refuses extraction", NegativeEnvelopes);
        Run("No accidental attestation or send surface", NoAuthority);
        Console.WriteLine($"MZP3 ALL PASS ({_passes} suites; production codec compiled, offline only)");
    }

    static void Run(string label, Action test)
    {
        test();
        _passes++;
        Console.WriteLine($"PASS {_passes:00}: {label}");
    }

    static void Check(bool value, string message)
    {
        if (!value) throw new Exception("ASSERT: " + message);
    }

    static void Eq<T>(T a, T b, string context)
    {
        if (!EqualityComparer<T>.Default.Equals(a, b))
            throw new Exception($"ASSERT: {context}: expected {b}, got {a}");
    }

    static void Same(byte[] a, byte[] b, string context)
        => Check(a.SequenceEqual(b), context + " bytes differ");

    static void Error(ReadOnlySpan<byte> data, MZP3WireError expected, MZP3Limits? budget = null)
    {
        var result = MZP3RawProtobuf.TryParse(data, budget);
        Eq(result.Error, expected, "parser status");
        Check(result.Message is null, "invalid message must not expose partial fields");
    }

    static void Varints()
    {
        (ulong Value, byte[] Encoding)[] cases =
        [
            (0UL, [0]), (1UL, [1]), (127UL, [127]), (128UL, [0x80, 0x01]),
            (300UL, [0xAC, 0x02]), (ulong.MaxValue, [0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0x01])
        ];
        foreach (var (v, encoded) in cases)
        {
            Same(MZP3RawProtobuf.EncodeVarint(v), encoded, "varint writer");
            var index = 0;
            Eq(MZP3RawProtobuf.ReadVarint(encoded, ref index, out var parsed), MZP3WireError.None, "varint decode");
            Eq(parsed, v, "varint scalar");
            Eq(index, encoded.Length, "consumed all bytes");
        }
    }

    static void VarintNegatives()
    {
        void Try(byte[] bytes, MZP3WireError expected)
        {
            int p = 0;
            Eq(MZP3RawProtobuf.ReadVarint(bytes, ref p, out _), expected, "bad varint");
            Eq(p, 0, "failed varint does not advance caller offset");
        }
        Try([0x80], MZP3WireError.TruncatedVarint);
        Try([0x80, 0x00], MZP3WireError.NonCanonicalVarint);
        Try([0x81, 0x00], MZP3WireError.NonCanonicalVarint);
        Try([0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0x02], MZP3WireError.OverflowVarint);
        Try([0x80,0x80,0x80,0x80,0x80,0x80,0x80,0x80,0x80,0x80], MZP3WireError.OverflowVarint);
    }

    static void WireRoundtrip()
    {
        // Independent protobuf wire fixture; type 0, 1, 2, 5 and repeated type 2.
        byte[] source = [0x08,0x96,0x01,
            0x11,0x08,0x07,0x06,0x05,0x04,0x03,0x02,0x01,
            0x1A,0x03,0x00,0xFF,0x40,
            0x25,0xA5,0xA4,0xA3,0xA2,
            0x1A,0x00,
            0xE8,0x12,0x07]; // unknown field 301=7
        var r = MZP3RawProtobuf.Parse(source);
        Eq(r.Fields.Count, 6, "field count");
        Eq(r.Fields[0].UnsignedValue, (ulong?)150, "varint field");
        Eq(r.Fields[1].Fixed64, (ulong?)0x0102030405060708, "LE64");
        Eq(r.Fields[3].Fixed32, (uint?)0xA2A3A4A5, "LE32");
        Same(r.Fields[2].GetData(), [0, 0xFF, 0x40], "opaque bytes");
        Eq(r.Fields[4].DataLength, 0, "empty bytes retained");
        Eq(r.Fields[5].Number, 301, "unknown field retained");
        Same(r.Encode(), source, "byte-exact roundtrip");
        Same(new MZP3MessageBuilder().AddRaw(r.Fields[5]).AddRaw(r.Fields[2]).Build().Encode(),
            source.Skip(source.Length - 3).Concat(source.Skip(12).Take(5)).ToArray(), "selected field reorder");
    }

    static void UnknownPreservation()
    {
        byte[] odd = [0x08, 0x81, 0x00, 0x10, 0x05]; // noncanonical VALUE for field 1 only
        Error(odd, MZP3WireError.NonCanonicalVarint);
        var loose = MZP3RawProtobuf.TryParse(odd, allowNonCanonicalValues: true);
        Check(loose.Ok, "opt-in noncanonical value accepted");
        Same(loose.Message!.Encode(), odd, "exact unknown encoded representation");
        var repeated = new MZP3MessageBuilder()
            .AddRaw(loose.Message.Fields[0]).AddVarint(1, 0x1234)
            .AddRaw(loose.Message.Fields[1]).Build();
        Same(repeated.Encode(), [0x08,0x81,0x00,0x08,0xB4,0x24,0x10,0x05], "unknown preserved despite append");
    }

    static void FieldImmutability()
    {
        var bytes = new byte[] { 0x33, 0x44 };
        var b = new MZP3MessageBuilder().AddBytes(4, bytes).Build();
        bytes[0] = 0xFF;
        var out1 = b.Encode();
        b.Fields[0].GetData()[0] = 0xFF;
        b.Fields[0].GetEncoded()[0] = 0xFF;
        Same(b.Encode(), out1, "writer and accessor copies isolate encoded fields");
    }

    static void BudgetGuards()
    {
        var message = new MZP3MessageBuilder()
            .AddVarint(1, 1234).AddBytes(2, [1,2,3,4,5]).Build();
        var raw = message.Encode();
        Error(raw, MZP3WireError.TooLarge, new MZP3Limits(MaxMessageBytes: 6));
        Error(raw, MZP3WireError.TooManyFields, new MZP3Limits(MaxFields: 1));
        Error(raw, MZP3WireError.FieldTooLarge, new MZP3Limits(MaxFieldBytes: 4));
        Error(raw, MZP3WireError.InvalidLimits, new MZP3Limits(MaxMessageBytes: 0));
        try { _ = message.Encode(new MZP3Limits(MaxMessageBytes: 4)); throw new Exception("missing output bound"); }
        catch (MZP3FormatException ex) { Eq(ex.Error, MZP3WireError.TooLarge, "bounded write"); }
    }

    static void InvalidFields()
    {
        Error([0x00], MZP3WireError.InvalidFieldNumber);
        Error([0x0B], MZP3WireError.InvalidWireType); // legacy group type3
        Error([0x0C], MZP3WireError.InvalidWireType);
        Error([0x0E], MZP3WireError.InvalidWireType);
        Error([0x09,0x01], MZP3WireError.TruncatedValue);
        Error([0x0D,0x01,0x02], MZP3WireError.TruncatedValue);
        Error([0x0A,0x04,0x01], MZP3WireError.TruncatedValue);
        Error([0x08,0x80], MZP3WireError.TruncatedVarint);
        Error([0x0A,0x80,0x00], MZP3WireError.NonCanonicalVarint);
        Error([0x80], MZP3WireError.TruncatedVarint);
        try { _ = new MZP3MessageBuilder().AddBytes(19000, []); throw new Exception("accepted reserved number"); }
        catch (ArgumentOutOfRangeException) { }
        try { _ = new MZP3MessageBuilder().AddVarint(0, 0); throw new Exception("accepted zero number"); }
        catch (ArgumentOutOfRangeException) { }
    }

    static void OpaquePayload()
    {
        var r = MZP3RawProtobuf.Parse(new MZP3MessageBuilder()
            .AddUtf8(3, "té").AddBytes(4, [0xFF,0x00,0x80]).Build().Encode());
        Check(r.Fields[0].TryGetStrictUtf8(out string? v), "valid UTF8");
        Eq(v, "té", "decoded accents");
        Check(!r.Fields[1].TryGetStrictUtf8(out _), "invalid UTF8 cannot be assumed string");
        Check(!r.Fields[0].ParseEmbedded().Ok, "text is not silently interpreted as message");
    }

    static void EmbeddedDepth()
    {
        var child = new MZP3MessageBuilder().AddVarint(2, 12).Build();
        var parent = new MZP3MessageBuilder().AddEmbedded(1, child).Build();
        var parsed = MZP3RawProtobuf.Parse(parent.Encode());
        var embedded = parsed.Fields[0].ParseEmbedded();
        Check(embedded.Ok, "explicit nested parse");
        Eq(embedded.Message!.Fields[0].UnsignedValue, (ulong?)12, "nested value");
        Eq(parsed.Fields[0].ParseEmbedded(new MZP3Limits(MaxDepth: 1), 2).Error,
            MZP3WireError.DepthExceeded, "depth bound");
    }

    static void FixedAndPacked()
    {
        var packed = new byte[] { 1, 2, 0xAC, 2 };
        var bytes = new MZP3MessageBuilder().AddFixed32(8, 0xDEADBEEF)
            .AddFixed64(9, ulong.MaxValue).AddBytes(10, packed).Build().Encode();
        var r = MZP3RawProtobuf.Parse(bytes);
        Eq(r.Fields[0].Fixed32, (uint?)0xDEADBEEF, "fixed32");
        Eq(r.Fields[1].Fixed64, (ulong?)ulong.MaxValue, "fixed64");
        Same(r.Fields[2].GetData(), packed, "packed field remains opaque");
        Same(r.Encode(), bytes, "packed and fixed lossless");
    }

    static void DifferentialAgainstP0()
    {
        byte[] source = new MZP3MessageBuilder().AddVarint(1, 150)
            .AddFixed64(2, 0x1020304050607080).AddBytes(3, [0xFF, 0x80, 0x00])
            .AddFixed32(4, 0xFFFFEEEE).Build().Encode();
        var ours = MZP3RawProtobuf.Parse(source);
        var reference = P0RawProtobufReader.Parse(source);
        Check(reference.IsValid, "independent reference accepts our bytes");
        Eq(reference.Fields.Length, ours.Fields.Count, "same field count");
        for (var i = 0; i < ours.Fields.Count; i++)
        {
            var a = reference.Fields[i];
            var b = ours.Fields[i];
            Eq(a.Number, b.Number, "same tag");
            Eq(a.WireType, (int)b.WireType, "same wire type");
            Eq(a.UnsignedValue, b.UnsignedValue, "same scalar");
            Same(source.AsSpan(a.Offset, a.EncodedLength).ToArray(), b.GetEncoded(), "same encoded slice");
        }
    }

    static void RandomizedDifferential()
    {
        var random = new Random(0x3203103);
        for (int seed = 0; seed < 500; seed++)
        {
            var builder = new MZP3MessageBuilder();
            int count = random.Next(0, 42);
            for (int n = 0; n < count; n++)
            {
                int field = random.Next(1, 512);
                switch (random.Next(4))
                {
                    case 0: builder.AddVarint(field, (ulong)random.NextInt64()); break;
                    case 1: builder.AddFixed32(field, (uint)random.NextInt64()); break;
                    case 2: builder.AddFixed64(field, (ulong)random.NextInt64()); break;
                    case 3:
                        byte[] data = new byte[random.Next(0, 98)];
                        random.NextBytes(data);
                        builder.AddBytes(field, data);
                        break;
                }
            }
            var encoded = builder.Build().Encode();
            var parsed = MZP3RawProtobuf.TryParse(encoded);
            Check(parsed.Ok, $"our parser seed {seed}");
            Same(parsed.Message!.Encode(), encoded, "500x roundtrip");
            var refResult = P0RawProtobufReader.Parse(encoded);
            Check(refResult.IsValid, $"P0 reader seed {seed}");
            Eq(parsed.Message.Fields.Count, refResult.Fields.Length, "500x equal field count");
        }
    }

    static void MalformedFuzz()
    {
        var random = new Random(0x37_10_10);
        for (int i = 0; i < 1200; i++)
        {
            var bytes = new byte[random.Next(0, 650)];
            random.NextBytes(bytes);
            var result = MZP3RawProtobuf.TryParse(bytes);
            if (!result.Ok)
            {
                Check(result.Message is null, "no partial parse leaked after error");
                Check(result.Error != MZP3WireError.None, "non-success always has error");
            }
            else
            {
                Same(result.Message!.Encode(), bytes, "random valid data lossless");
            }
        }
    }

    static void Frames()
    {
        var body = new MZP3MessageBuilder().AddVarint(1, 5).Build().Encode();
        var framed = MZP3FrameCodec.Prefix(body);
        var res = MZP3FrameCodec.TryReadOne(framed, requireExact: true);
        Check(res.Complete, "frame complete");
        Same(framed.AsSpan(res.BodyOffset, res.BodyLength).ToArray(), body, "body exact");
        Eq(res.Consumed, framed.Length, "frame length");
        var combined = framed.Concat(framed).ToArray();
        Eq(MZP3FrameCodec.TryReadOne(combined, requireExact: false).Consumed, framed.Length, "coalesced two frames");
        Eq(MZP3FrameCodec.TryReadOne(combined, requireExact: true).Error, MZP3WireError.TrailingBytes, "exact rejects trailers");
        // Each frame fits the budget, but the receive buffer may contain several frames.
        var largeValid = MZP3FrameCodec.Prefix(new byte[100], maxFrameBytes: 128);
        var coalescedLarge = largeValid.Concat(largeValid).ToArray();
        var first = MZP3FrameCodec.TryReadOne(coalescedLarge, maxFrameBytes: 128, requireExact: false);
        Check(first.Complete, "first of two coalesced frames fits the frame limit");
        Eq(first.Consumed, largeValid.Length, "first large coalesced frame consumed");
        var second = MZP3FrameCodec.TryReadOne(coalescedLarge.AsSpan(first.Consumed), maxFrameBytes: 128);
        Check(second.Complete, "second of two coalesced frames fits the frame limit");
        Eq(second.Consumed, largeValid.Length, "second large coalesced frame consumed");
        Eq(MZP3FrameCodec.TryReadOne(framed[..^1]).Error, MZP3WireError.TruncatedValue, "incomplete frame");
        Eq(MZP3FrameCodec.TryReadOne([0x80,0x00]).Error, MZP3WireError.NonCanonicalVarint, "bad length prefix");
        Eq(MZP3FrameCodec.TryReadOne([0xFF,0x7F], maxFrameBytes:128).Error, MZP3WireError.TooLarge, "huge length prefix");
    }

    static void Envelopes()
    {
        var slots = new MZP3RequestSlots(1,1,2); // test mapping only, not server certified
        var payload = new MZP3MessageBuilder().AddUtf8(7,"test").Build().Encode();
        var bytes = MZP3EnvelopeCodec.BuildClientRequest(slots,"abc", payload, -1);
        Check(MZP3EnvelopeCodec.TryReadClientRequest(bytes,slots,out var parsed), "request decoded");
        Eq(parsed!.Code,"abc", "Any type_url");
        Eq(parsed.Uid,-1L,"signed int64 UID");
        Same(parsed.Payload,payload,"payload exact");
        Check(!parsed.ServerAckCertified,"request not ack");
        var zero = MZP3EnvelopeCodec.BuildClientRequest(slots,"def",[],0);
        Check(MZP3EnvelopeCodec.TryReadClientRequest(zero,slots,out var p0),"empty request read");
        Eq(p0!.Uid,null,"proto3 uid 0 omitted");
        Same(p0.Payload,[],"empty Any value omitted");
    }

    static void GoldenEnvelope()
    {
        // Source-derived EnvelopeBuilder shape: GameMessage.request #1,
        // Request.content #1, Request.uid #2, Any.type_url #1, Any.value #2.
        // This is a FIXTURE, not a claim of currently certified Dofus 3.7 slots.
        var slots = new MZP3RequestSlots(1, 1, 2);
        var code = "abc";
        var type = Encoding.UTF8.GetBytes("type.ankama.com/abc");
        var payload = new byte[] { 0x08, 0x01 };
        var any = new List<byte> { 0x0A, (byte)type.Length };
        any.AddRange(type);
        any.AddRange([0x12, 0x02, 0x08, 0x01]);
        var request = new List<byte> { 0x0A, (byte)any.Count };
        request.AddRange(any);
        request.Add(0x10);
        request.AddRange([0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0xFF,0x01]);
        var envelope = new List<byte> { 0x0A, (byte)request.Count };
        envelope.AddRange(request);
        var actual = MZP3EnvelopeCodec.BuildClientRequest(slots, code, payload, -1);
        Same(actual, envelope.ToArray(), "independently assembled envelope expected bytes");
    }

    static void NegativeEnvelopes()
    {
        var slots = new MZP3RequestSlots(1,1,2);
        var raw = MZP3EnvelopeCodec.BuildClientRequest(slots,"abc",[1],123);
        Check(!MZP3EnvelopeCodec.TryReadClientRequest(raw,new MZP3RequestSlots(3,1,2),out _),"wrong tag fails closed");
        Check(!MZP3EnvelopeCodec.TryReadClientRequest(raw,new MZP3RequestSlots(1,1,1),out _),"colliding field numbers denied");
        Check(!MZP3EnvelopeCodec.TryReadClientRequest(raw[..^1],slots,out _),"truncated envelope rejected");
        var bad = new MZP3MessageBuilder().AddBytes(1,
            new MZP3MessageBuilder().AddBytes(1,
                new MZP3MessageBuilder().AddUtf8(1,"https://wrong/").Build().Encode()).Build().Encode()).Build().Encode();
        Check(!MZP3EnvelopeCodec.TryReadClientRequest(bad,slots,out _),"wrong type_url rejected");
        try { _ = MZP3EnvelopeCodec.BuildClientRequest(slots,"a/b",[],1); throw new Exception("bad code accepted"); }
        catch(ArgumentException) { }
    }

    static void NoAuthority()
    {
        Check(!MZP3RawProtobuf.TryParse([0x08,0x01]).ServerAckCertified, "raw parse does not imply ACK");
        Check(!MZP3FrameCodec.TryReadOne([0x00]).GameFramingCertified,"frame shape does not prove client transport");
    }
}
