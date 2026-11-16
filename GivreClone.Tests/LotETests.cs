using System.Collections.Generic;
using System.Linq;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using Xunit;

namespace GivreClone.Tests;

/// <summary>
/// Lot E (petites forges) : forges et lecteurs vérifiés contre le VRAI catalogue livré (branché par TestSetup). Les codes et
/// numéros attendus sont ceux de data/matching_current.json + data/fields.json (⚠ plusieurs messages sont numérotés dans un
/// autre ordre que clear_game_2025.proto : c'est fields.json qui fait foi).
/// </summary>
public class LotETests
{
    private static readonly ProtocolCatalog Mapping = ProtocolCatalog.LoadFromDirectory(
        System.IO.Path.Combine(System.AppContext.BaseDirectory, "data"));
    private static string Code(string clearName) => Mapping.CodeForName(clearName)
        ?? throw new System.InvalidOperationException($"Message {clearName} absent du catalogue");
    private static int Field(string message, string field) => Mapping.FieldNumber(message, field)
        ?? throw new System.InvalidOperationException($"Champ {message}.{field} absent du catalogue");

    // Retire le préfixe varint(len) et rend l'arbre du GameMessage.
    private static IReadOnlyList<ProtoField> Frame(byte[] framed)
    {
        Assert.NotEmpty(framed);
        int pos = 0;
        Assert.True(RawProtobuf.TryReadVarint(framed, ref pos, out ulong len));
        Assert.Equal((int)len, framed.Length - pos);
        return RawProtobuf.Parse(framed[pos..]);
    }

    // GameMessage -> (code du type_url, charge utile du message de jeu) selon le mapping livré.
    private static (string Code, IReadOnlyList<ProtoField> Payload) Request(byte[] framed)
    {
        var gm = Frame(framed);
        var req = gm.FirstOrDefault(f => f.Field == Field("GameMessage", "request") && f.Message is not null)?.Message;
        Assert.NotNull(req);
        var any = req!.FirstOrDefault(f => f.Field == Field("Request", "content") && f.Message is not null)?.Message;
        Assert.NotNull(any);
        var url = any!.FirstOrDefault(f => f.Field == 1)?.String;
        Assert.NotNull(url);
        Assert.StartsWith("type.ankama.com/", url!);
        var payload = any.FirstOrDefault(f => f.Field == 2 && f.Message is not null)?.Message ?? new List<ProtoField>();
        return (url!["type.ankama.com/".Length..], payload);
    }

    private static long V(IReadOnlyList<ProtoField> p, int field)
        => p.First(f => f.Field == field && f.Wire == WireType.Varint).AsSignedVarint;

    private static ProtoField Vf(int field, long value) => new() { Field = field, Wire = WireType.Varint, Varint = (ulong)value };
    private static ProtoField Sf(int field, string value) => new() { Field = field, Wire = WireType.Len, String = value };

    // ---------------------------------------------------------------- forges

    [Fact]
    public void FightOptionToggle_closed_sends_option_2_without_obfuscated_field()
    {
        var (code, p) = Request(MoveForge.BuildFightOptionToggleFramed(2));
        Assert.Equal(Code("FightOptionToggleRequest"), code);
        Assert.Equal(2, V(p, Field("FightOptionToggleRequest", "option")));
        Assert.DoesNotContain(p, f => f.Field == Field("FightOptionToggleRequest", "fwmg"));
    }

    [Fact]
    public void FightOptionToggle_secret_is_empty_payload_and_party_only_is_1()
    {
        var (code, p) = Request(MoveForge.BuildFightOptionToggleFramed(0));
        Assert.Equal(Code("FightOptionToggleRequest"), code);
        Assert.Empty(p);                           // SECRET = 0 -> omis en proto3
        var (code1, p1) = Request(MoveForge.BuildFightOptionToggleFramed(1));
        Assert.Equal(Code("FightOptionToggleRequest"), code1);
        Assert.Equal(1, V(p1, Field("FightOptionToggleRequest", "option")));
    }

    [Fact]
    public void ZaapRespawnSave_is_empty_hin()
    {
        var (code, p) = Request(MoveForge.BuildZaapRespawnSaveFramed());
        Assert.Equal(Code("ZaapRespawnSaveRequest"), code);
        Assert.Empty(p);
    }

    [Fact]
    public void FriendlyDuelAnswer_refuse_omits_accept_and_accept_sets_it()
    {
        var (code, p) = Request(MoveForge.BuildFriendlyDuelAnswerFramed(4321, false));
        Assert.Equal(Code("PlayerFightFriendlyAnswerRequest"), code);
        Assert.Equal(4321, V(p, Field("PlayerFightFriendlyAnswerRequest", "fight_id")));
        Assert.DoesNotContain(p, f => f.Field == Field("PlayerFightFriendlyAnswerRequest", "accept"));
        var (_, p2) = Request(MoveForge.BuildFriendlyDuelAnswerFramed(4321, true));
        Assert.Equal(1, V(p2, Field("PlayerFightFriendlyAnswerRequest", "accept")));
        Assert.Equal(4321, V(p2, Field("PlayerFightFriendlyAnswerRequest", "fight_id")));
    }

    [Fact]
    public void SpellVariantActivation_sends_spell_id_1()
    {
        var (code, p) = Request(MoveForge.BuildSpellVariantActivationFramed(13721));
        Assert.Equal(Code("SpellVariantActivationRequest"), code);
        Assert.Equal(13721, V(p, Field("SpellVariantActivationRequest", "spell_id")));
        Assert.Single(p);                          // fmuq/fmur omis
    }

    [Fact]
    public void ContextQuit_is_empty_kjy()
    {
        var (code, p) = Request(MoveForge.BuildContextQuitFramed());
        Assert.Equal(Code("ContextQuitRequest"), code);
        Assert.Empty(p);
    }

    // ---------------------------------------------------------------- lecteurs

    [Fact]
    public void ReadTradeRequest_uses_source_4_target_2_type_1()
    {
        var p = new List<ProtoField> {
            Vf(Field("ExchangeRequestedTradeEvent", "exchange_type"), 1),
            Vf(Field("ExchangeRequestedTradeEvent", "target_id"), 222),
            Vf(Field("ExchangeRequestedTradeEvent", "source_id"), 444)
        };
        var (src, tgt, type) = LotEEventsReader.ReadTradeRequest(p);
        Assert.Equal(444, src);
        Assert.Equal(222, tgt);
        Assert.Equal(1, type);
    }

    [Fact]
    public void ReadDuelRequest_uses_fight_2_source_3_target_4()
    {
        var p = new List<ProtoField> {
            Vf(Field("PlayerFightFriendlyRequestedEvent", "fight_id"), 55),
            Vf(Field("PlayerFightFriendlyRequestedEvent", "source_id"), 333),
            Vf(Field("PlayerFightFriendlyRequestedEvent", "target_id"), 444)
        };
        var (fid, src, tgt) = LotEEventsReader.ReadDuelRequest(p);
        Assert.Equal(55, fid);
        Assert.Equal(333, src);
        Assert.Equal(444, tgt);
    }

    [Fact]
    public void ReadFightId_per_message()
    {
        Assert.Equal(12, LotEEventsReader.ReadFightId(
            new List<ProtoField> { Vf(Field("FightRequestCanceledEvent", "fight_id"), 12) }, "FightRequestCanceledEvent"));
        Assert.Equal(34, LotEEventsReader.ReadFightId(
            new List<ProtoField> { Vf(Field("PlayerFightFriendlyAnsweredEvent", "fight_id"), 34) }, "PlayerFightFriendlyAnsweredEvent"));
    }

    [Fact]
    public void ReadPopup_uses_content_1_lock_2_author_3()
    {
        var p = new List<ProtoField> {
            Sf(Field("PopupWarningEvent", "content"), "Merci de répondre"),
            Vf(Field("PopupWarningEvent", "lock_duration"), 60),
            Sf(Field("PopupWarningEvent", "author"), "[Modo]")
        };
        var (content, author, lockS) = LotEEventsReader.ReadPopup(p);
        Assert.Equal("Merci de répondre", content);
        Assert.Equal("[Modo]", author);
        Assert.Equal(60, lockS);
    }

    [Fact]
    public void ReadFightOptionUpdate_absent_fields_are_zero_false()
    {
        var (opt, state, fid, team) = LotEEventsReader.ReadFightOptionUpdate(new List<ProtoField> {
            Vf(Field("FightOptionUpdateEvent", "fight_id"), 99)
        });
        Assert.Equal(0, opt);       // SECRET omis
        Assert.False(state);        // false omis
        Assert.Equal(99, fid);
        Assert.Equal(0, team);      // Challenger omis
        var (opt2, state2, _, team2) = LotEEventsReader.ReadFightOptionUpdate(new List<ProtoField> {
            Vf(Field("FightOptionUpdateEvent", "option"), 2),
            Vf(Field("FightOptionUpdateEvent", "state"), 1),
            Vf(Field("FightOptionUpdateEvent", "fight_id"), 99),
            Vf(Field("FightOptionUpdateEvent", "team"), 1)
        });
        Assert.Equal(2, opt2);
        Assert.True(state2);
        Assert.Equal(1, team2);
    }

    [Fact]
    public void ReadSpellVariantResult_uses_spell_2_effective_1()
    {
        var (id, eff) = LotEEventsReader.ReadSpellVariantResult(new List<ProtoField> { Vf(1, 1), Vf(2, 13721) });
        Assert.Equal(13721, id);
        Assert.True(eff);
        var (id2, eff2) = LotEEventsReader.ReadSpellVariantResult(new List<ProtoField> { Vf(2, 13722) });
        Assert.Equal(13722, id2);
        Assert.False(eff2);
    }

    [Fact]
    public void ReadRespawnMap_uses_map_id_1()
    {
        Assert.Equal(88085249, LotEEventsReader.ReadRespawnMap(new List<ProtoField> { Vf(1, 88085249) }));
        Assert.Equal(0, LotEEventsReader.ReadRespawnMap(new List<ProtoField>()));
    }
}
