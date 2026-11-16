extern alias F06App;

using System;
using System.Collections;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Threading;
using System.Threading.Tasks;
using GivreClone.Protocol;
using GivreCore.Protobuf;
using Xunit;
using BidHouseStore = F06App::GivreClone.App.Services.BidHouseStore;
using HdvProfileStore = F06App::GivreClone.App.Services.HdvProfileStore;
using HdvProfilesFile = F06App::GivreClone.App.Services.HdvProfilesFile;
using HdvProfile = F06App::GivreClone.App.Services.HdvProfile;
using HdvProfileKey = F06App::GivreClone.App.Services.HdvProfileKey;
using HdvRepricePacing = F06App::GivreClone.App.Services.HdvRepricePacing;
using HdvTradeSettings = F06App::GivreClone.App.Services.HdvTradeSettings;
using HdvDirectMutationJournal = F06App::GivreClone.App.Services.HdvDirectMutationJournal;
using Decoder = F06App::GivreClone.App.Services.CaptureDecoder;
using Launch = F06App::GivreClone.App.Services.LaunchService;
using Packet = F06App::GivreClone.App.Services.DecodedPacket;

namespace GivreClone.Tests;

/// <summary>
/// Todo 12 (GAP-9 / IS-8) : le reprice HDV ne lit le marché qu'UNE fois par GID distinct et respecte le délai
/// CONFIGURÉ (0 ms autorisé, plus de 400 ms figés).
/// <para>
/// Ces tests pilotent la VRAIE méthode de production <see cref="Launch.HdvRepriceAllAsync"/> : le service est
/// instancié comme dans <c>QaHdvTradeBankTests</c> et son transport est simulé par l'injecteur socket, donc les
/// requêtes de marché comptées ici sont des requêtes RÉELLEMENT émises par le chemin de production (pas un
/// doublet de l'algorithme, pas une assertion sur le texte des sources).
/// </para>
/// </summary>
[Collection("Transport journal and timing")]
public sealed class HdvRepricePacingTests
{
    private const uint Pid = 9u;
    private const int ServerId = 42;
    private const BindingFlags Hidden = BindingFlags.Instance | BindingFlags.NonPublic;

    private static readonly ProtocolCatalog Catalog =
        ProtocolCatalog.LoadFromDirectory(Path.Combine(AppContext.BaseDirectory, "data"));

    // =================== chemin de PRODUCTION (HdvRepriceAllAsync) ===================

    [Fact]
    public async Task Five_lots_of_the_same_gid_issue_exactly_one_market_price_query()
    {
        var harness = new RepriceHarness(
            (Uid: 1, Gid: 100L, Qty: 10, Price: 9000L),
            (Uid: 2, Gid: 100L, Qty: 10, Price: 9100L),
            (Uid: 3, Gid: 100L, Qty: 10, Price: 9200L),
            (Uid: 4, Gid: 100L, Qty: 10, Price: 9300L),
            (Uid: 5, Gid: 100L, Qty: 10, Price: 9400L));

        int repriced = await harness.Service.HdvRepriceAllAsync(Pid, 1, CancellationToken.None);

        // UN relevé de marché pour le GID (et un seul), pas un par lot.
        Assert.Equal(new long[] { 100L }, harness.MarketPriceQueries);
        Assert.Equal(new long[] { 100L }, harness.MarketSearches);
        // ... et les 5 lots sont bien repositionnés sur CE prix unique.
        Assert.Equal(5, repriced);
        Assert.Equal(5, harness.Reprices.Count);
        Assert.All(harness.Reprices, r => Assert.Equal(4999L, r.Price));
    }

    [Fact]
    public async Task Multiple_gids_issue_one_market_price_query_per_distinct_gid()
    {
        var harness = new RepriceHarness(
            (Uid: 1, Gid: 100L, Qty: 10, Price: 9000L),
            (Uid: 2, Gid: 100L, Qty: 10, Price: 9100L),
            (Uid: 3, Gid: 200L, Qty: 10, Price: 9000L),
            (Uid: 4, Gid: 300L, Qty: 10, Price: 9000L),
            (Uid: 5, Gid: 200L, Qty: 10, Price: 9200L));

        int repriced = await harness.Service.HdvRepriceAllAsync(Pid, 1, CancellationToken.None);

        // Un relevé par GID distinct (100, 200, 300), dans l'ordre des groupes.
        Assert.Equal(new long[] { 100L, 200L, 300L }, harness.MarketPriceQueries);
        Assert.Equal(new long[] { 100L, 200L, 300L }, harness.MarketSearches);
        Assert.Equal(5, repriced);
        Assert.Equal(5, harness.Reprices.Count);
    }

    [Fact]
    public async Task Configured_zero_delay_is_honoured_between_lots()
    {
        var harness = new RepriceHarness(
            (Uid: 1, Gid: 100L, Qty: 10, Price: 9000L),
            (Uid: 2, Gid: 100L, Qty: 10, Price: 9100L),
            (Uid: 3, Gid: 100L, Qty: 10, Price: 9200L),
            (Uid: 4, Gid: 100L, Qty: 10, Price: 9300L),
            (Uid: 5, Gid: 100L, Qty: 10, Price: 9400L));
        harness.SetBulkDelay(0, 0);

        // Le résolveur de production lit le réglage du personnage : 0 ms => aucune attente.
        Assert.Equal(0, harness.Service.ResolveRepriceDelay(Pid));

        long started = Environment.TickCount64;
        int repriced = await harness.Service.HdvRepriceAllAsync(Pid, 1, CancellationToken.None);
        long elapsed = Environment.TickCount64 - started;

        Assert.Equal(5, repriced);
        // 0 ms configuré => AUCUNE pause entre les lots (l'ancien Task.Delay(400) en imposait 2 s sur 5 lots).
        Assert.True(elapsed < 1000, $"reprice de 5 lots avec 0 ms configuré : {elapsed} ms");
    }

    [Fact]
    public async Task Configured_delay_between_lots_is_the_one_that_is_applied()
    {
        // The old wall-clock assertion included first-use catalog/JIT and
        // durable journal filesystem work. Prime those SAME real service
        // boundaries before timing the 5-lot scenario: no timer mocking and
        // no relaxation of the existing 400..1500 ms window.
        var warmup = new RepriceHarness(
            (Uid: 91, Gid: 100L, Qty: 10, Price: 9000L));
        warmup.SetBulkDelay(0, 0);
        Assert.Equal(1, await warmup.Service.HdvRepriceAllAsync(
            Pid, 1, CancellationToken.None));
        var harness = new RepriceHarness(
            (Uid: 1, Gid: 100L, Qty: 10, Price: 9000L),
            (Uid: 2, Gid: 100L, Qty: 10, Price: 9100L),
            (Uid: 3, Gid: 100L, Qty: 10, Price: 9200L),
            (Uid: 4, Gid: 100L, Qty: 10, Price: 9300L),
            (Uid: 5, Gid: 100L, Qty: 10, Price: 9400L));
        harness.SetBulkDelay(100, 100);

        Assert.Equal(100, harness.Service.ResolveRepriceDelay(Pid));

        long started = Environment.TickCount64;
        int repriced = await harness.Service.HdvRepriceAllAsync(Pid, 1, CancellationToken.None);
        long elapsed = Environment.TickCount64 - started;

        Assert.Equal(5, repriced);
        Assert.Equal(5, harness.Reprices.Count);
        Assert.Equal(5, harness.RepriceSentTicks.Count);
        // Clock the REAL outgoing repricing frames. Each interval between
        // distinct lots must include the configured 100 ms timer. A slow
        // response/disk scheduler may add time; it cannot invent an earlier
        // send or hide a missing inter-lot wait.
        for (int i = 1; i < harness.RepriceSentTicks.Count; i++)
        {
            double gapMs = (harness.RepriceSentTicks[i] -
                harness.RepriceSentTicks[i - 1]) * 1000d / Stopwatch.Frequency;
            Assert.True(gapMs >= 90,
                $"outgoing lot {i + 1} arrived {gapMs:F1} ms after lot {i}; 100 ms configured");
        }
        // 100 ms configurés x 5 lots => ~500 ms : le délai vient du RÉGLAGE (ni 0, ni 400 ms figés).
        Assert.InRange(elapsed, 400, 1500);
    }

    // =================== aides pures utilisées par la production ===================

    [Fact]
    public void GroupLotsByGid_groups_five_lots_of_same_gid_into_one_group()
    {
        var lots = new[]
        {
            (Uid: 1, Gid: 42L),
            (Uid: 2, Gid: 42L),
            (Uid: 3, Gid: 42L),
            (Uid: 4, Gid: 42L),
            (Uid: 5, Gid: 42L),
        };

        var groups = HdvRepricePacing.GroupLotsByGid(lots, l => l.Gid);
        Assert.Single(groups);
        Assert.Equal(42L, groups[0].Gid);
        Assert.Equal(5, groups[0].Lots.Count);
    }

    [Theory]
    [InlineData(0, 0, 0)]
    [InlineData(0, -1, 0)]
    [InlineData(-10, -5, 0)]
    public void Zero_delay_returns_zero(int min, int max, int expected)
    {
        Assert.Equal(expected, HdvRepricePacing.ResolveDelayMs(min, max));
    }

    [Fact]
    public void Zero_delay_setting_means_no_wait()
    {
        var settings = new HdvTradeSettings { BulkDelayMinMs = 0, BulkDelayMaxMs = 0 };
        int delay = HdvRepricePacing.ResolveDelay(settings);
        Assert.Equal(0, delay);
        Assert.False(HdvRepricePacing.ShouldWait(delay));
    }

    [Fact]
    public void Fixed_delay_returns_exact_value()
    {
        int delay = HdvRepricePacing.ResolveDelayMs(400, 400);
        Assert.Equal(400, delay);
        Assert.True(HdvRepricePacing.ShouldWait(delay));
    }

    [Fact]
    public void Delay_respects_interval()
    {
        for (int i = 0; i < 20; i++)
        {
            int delay = HdvRepricePacing.ResolveDelayMs(200, 500);
            Assert.InRange(delay, 200, 500);
            Assert.True(HdvRepricePacing.ShouldWait(delay));
        }
    }

    // =================== harnais : faux transport HDV ===================

    /// <summary>
    /// Service de production piloté par un faux transport : les trames forgées par <c>HdvRepriceAllAsync</c>
    /// arrivent ici, le harness répond comme le serveur (prix marché du gid, retrait exact + nouvelle annonce
    /// pour le reprice) et compte ce qui est RÉELLEMENT émis.
    /// </summary>
    private sealed class RepriceHarness
    {
        // Prix marché min par tranche [x1, x10, x100, x1000] : le lot x10 est couse 5 000, mes lots sont a 9 000+.
        private static readonly long[] Market = { 10_000, 5_000, 500_000, 5_000_000 };

        private readonly Dictionary<int, long> _gidByUid = new();
        private readonly string _character = "PacingFixture" + Guid.NewGuid().ToString("N");

        public Launch Service { get; }

        /// <summary>GIDs des requêtes de prix marché (ExchangeBidHousePriceRequest) : un relevé par GID distinct.</summary>
        public List<long> MarketPriceQueries { get; } = new();

        /// <summary>GIDs des recherches HDV (ExchangeBidHouseSearchRequest).</summary>
        public List<long> MarketSearches { get; } = new();

        /// <summary>Reprices émis (uid, gid, quantité, prix demandé).</summary>
        public List<(int Uid, long Gid, int Qty, long Price)> Reprices { get; } = new();
        public List<long> RepriceSentTicks { get; } = new();

        public RepriceHarness(params (int Uid, long Gid, int Qty, long Price)[] lots)
        {
            // GetScoped normally persists a newly created profile immediately.
            // Install one isolated test-only scope into the existing in-memory
            // store before production ResolveRepriceDelay reads it. Never
            // create/overwrite the user's real hdv.json in a timing fixture.
            var data = (HdvProfilesFile)typeof(HdvProfileStore).GetField(
                "_data", BindingFlags.NonPublic | BindingFlags.Static)!.GetValue(null)!;
            data.Profiles[HdvProfileKey.For(_character, ServerId)] = new HdvProfile();
            Service = NewService(_character);
            Service.Hdv.SeedMine(Pid, lots.Select(l => new BidHouseStore.Listing(l.Uid, l.Gid, l.Qty, l.Price)));
            foreach (var lot in lots) _gidByUid[lot.Uid] = lot.Gid;
            State(Service, "_socketInjectors")[Pid] = (Func<byte[], bool>)(frame => OnFrame(frame));
            SetBulkDelay(0, 0); // no inherited user profile or default 600..1500 ms pacing
        }

        /// <summary>Règle le délai de vente en masse du personnage (0 ms = aucun délai), comme le fait l'onglet HDV.</summary>
        public void SetBulkDelay(int minMs, int maxMs)
        {
            var profile = HdvProfileStore.GetScoped(_character, ServerId);
            profile.Trade.BulkDelayMinMs = minMs;
            profile.Trade.BulkDelayMaxMs = maxMs;
            // ResolveRepriceDelay reads this same scoped in-memory profile;
            // no test needs to write the user's persisted hdv.json here.
        }

        private bool OnFrame(byte[] frame)
        {
            var (name, payload) = Decode(frame);
            switch (name)
            {
                case "ExchangeBidHouseSearchRequest":
                    MarketSearches.Add(Number(payload, name, "object_gid"));
                    return true;

                case "ExchangeBidHousePriceRequest":
                {
                    long gid = Number(payload, name, "object_gid");
                    MarketPriceQueries.Add(gid);
                    Service.Hdv.SetMarketPrices(Pid, gid, Market);
                    Complete("ExchangeBidPriceEvent", []);
                    return true;
                }

                case "ExchangeObjectModifyPricedRequest":
                {
                    int uid = (int)Number(payload, name, "object_uid");
                    int qty = (int)Number(payload, name, "quantity");
                    long price = Number(payload, name, "price");
                    long gid = _gidByUid[uid];
                    RepriceSentTicks.Add(Stopwatch.GetTimestamp());
                    Reprices.Add((uid, gid, qty, price));
                    AcknowledgeReprice(uid, gid, qty, price);
                    return true;
                }

                default:
                    return true;
            }
        }

        /// <summary>Preuve serveur d'un reprice : retrait EXACT de l'ancien lot puis nouvelle annonce (même gid/qty/prix).</summary>
        private void AcknowledgeReprice(int uid, long gid, int qty, long price)
        {
            var removed = new Packet(Pid, false, "", "ExchangeBidHouseItemRemovedEvent",
                [new ProtoField { Field = Wire("ExchangeBidHouseItemRemovedEvent", "sell_id"), Wire = WireType.Varint, Varint = (ulong)uid }],
                []) { DecoderEpoch = Service.CurrentLocalProductionDecoderEpoch(Pid) };
            var added = new Packet(Pid, false, "", "ExchangeBidHouseItemAddedEvent",
                [
                    new ProtoField { Field = Wire("ExchangeBidHouseItemAddedEvent", "price"), Wire = WireType.Varint, Varint = (ulong)price },
                    new ProtoField
                    {
                        Field = Wire("ExchangeBidHouseItemAddedEvent", "item"),
                        Wire = WireType.Len,
                        Message =
                        [
                            new ProtoField { Field = Wire("BidItem", "gid"), Wire = WireType.Varint, Varint = (ulong)gid },
                            new ProtoField { Field = Wire("BidItem", "uid"), Wire = WireType.Varint, Varint = (ulong)(uid + 1000) },
                            new ProtoField { Field = Wire("BidItem", "quantity"), Wire = WireType.Varint, Varint = (ulong)qty },
                        ]
                    },
                ],
                []) { DecoderEpoch = Service.CurrentLocalProductionDecoderEpoch(Pid) };
            var waiter = Waiter();
            Assert.NotNull(waiter);
            var accept = (Func<Packet, bool>?)waiter![1];
            accept?.Invoke(removed);   // le retrait exact passe par le prédicat de production
            accept?.Invoke(added);    // ... puis la nouvelle annonce (même gid, même quantité, même prix)
            ((TaskCompletionSource<Packet>)waiter[2]!).TrySetResult(added);
        }

        private void Complete(string eventName, IReadOnlyList<ProtoField> payload)
        {
            var waiter = Waiter();
            Assert.NotNull(waiter);
            ((TaskCompletionSource<Packet>)waiter![2]!).TrySetResult(new Packet(Pid, false, "", eventName, payload, []));
        }

        private ITuple? Waiter()
        {
            var waiters = State(Service, "_codeWait");
            return waiters.Contains(Pid) ? (ITuple)waiters[Pid]! : null;
        }
    }

    // =================== Reflect plumbing ===================

    private static FieldInfo Field(Type type, string name) => type.GetField(name, Hidden)!;

    private static IDictionary State(Launch service, string name)
        => (IDictionary)Field(typeof(Launch), name).GetValue(service)!;

    private static int Wire(string message, string field)
        => Catalog.FieldNumber(message, field)!.Value;

    private static long Number(IReadOnlyList<ProtoField> payload, string message, string field)
        => PayloadReader.Int(payload, Wire(message, field));

    private static (string Name, IReadOnlyList<ProtoField> Payload) Decode(byte[] frame)
    {
        var frames = RawProtobuf.ConsumeFrames(frame, out var rest);
        Assert.Empty(rest);
        var hit = Assert.Single(RawProtobuf.FindTypeUrls(RawProtobuf.Parse(Assert.Single(frames))));
        foreach (string name in new[]
        {
            "ExchangeBidHouseSearchRequest", "ExchangeBidHousePriceRequest", "ExchangeObjectModifyPricedRequest",
        })
        {
            if (Catalog.CodeForName(name) == hit.Code) return (name, hit.Payload ?? []);
        }
        throw new Xunit.Sdk.XunitException("Trame HDV inattendue : " + hit.Code);
    }

    private static Launch NewService(string character)
    {
        var service = (Launch)RuntimeHelpers.GetUninitializedObject(typeof(Launch));
        foreach (string name in new[]
        {
            "<Hdv>k__BackingField", "_hdvOpen", "_hdvAnyOpen", "_socketInjectors", "_codeWait", "_hdvSellLocks",
            "_hdvSaleLimits", "_avgPrices", "_serverInfo", "_hdvTaxPct", "_charId", "_selfInfo",
            "_hdvAutoUntil", "_hdvLastAction", "_charLevel", "_capRefusedLogTick",
            "_hdvRepriceNeedsReview", "_hdvRepriceProfileNeedsReview", "_linkDown", "_returnWait",
        })
        {
            var field = Field(typeof(Launch), name);
            field.SetValue(service, Activator.CreateInstance(field.FieldType));
        }
        Field(typeof(Launch), "_catalog").SetValue(service, Catalog);
        // A real direct-send journal is required at the dispatch boundary.
        // Give each offline fixture its own isolated path: never write to the
        // user's HDV data or reuse an order key between unrelated tests.
        Field(typeof(Launch), "_hdvDirectMutations").SetValue(service,
            new HdvDirectMutationJournal(Path.Combine(Path.GetTempPath(),
                "givre-hdv-pacing-" + Guid.NewGuid().ToString("N"), "direct.journal")));
        // The contract now requires a current LOCAL decoder lifetime and a
        // server/character snapshot at the final action boundary. Construct
        // only a synthetic local epoch for the offline pacing fixture. This
        // is NOT a real TCP source identity or a server ACK.
        var decoder = new Decoder(Catalog);
        decoder.Feed(Pid, false, Array.Empty<byte>());
        Field(typeof(Launch), "_decoder").SetValue(service, decoder);
        State(service, "_hdvOpen")[Pid] = true;                                    // HDV ouvert en mode VENDEUR
        // V9 requires that the seller-mode observation belong to an actually
        // open bid-house session, not just a single mode flag. The fixture
        // owns this synthetic observation; production never infers it.
        State(service, "_hdvAnyOpen")[Pid] = true;
        State(service, "_selfInfo")[Pid] = (1L, new CharacterInfo(character, 200, "Xelor", "F"));
        State(service, "_serverInfo")[Pid] = (ServerId, "Test");
        State(service, "_charId")[Pid] = 1L;
        return service;
    }
}
