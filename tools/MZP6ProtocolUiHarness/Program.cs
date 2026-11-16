using System.Security.Cryptography;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Interactivity;
using Avalonia.LogicalTree;
using GivreClone.App.Views;
using GivreClone.Protocol;

AppBuilder.Configure<OfflineApp>().UseHeadless(
    new AvaloniaHeadlessPlatformOptions()).SetupWithoutStarting();
int count = 0;
void Check(bool ok, string name)
{
    if (!ok) throw new InvalidOperationException("MZP6 FAILED: " + name);
    count++;
    Console.WriteLine("PASS " + name);
}

// TEST ONLY: entirely invented aliases/tags; no production catalogue in UI.
byte[] matching = Encoding.UTF8.GetBytes(
    """{"type.ankama.com/e2":"MessageType.SampleEvent","type.ankama.com/r3":"MessageType.SampleResponse","type.ankama.com/a1":"MessageType.SampleRequest"}""");
byte[] fields = Encoding.UTF8.GetBytes(
    """{"GameMessage":{"request":7,"event":5,"response":9},"Request":{"content":2,"uid":3},"Event":{"content":4},"Response":{"content":2,"uid":6},"SampleEvent":{"text":2},"SampleResponse":{"n":1},"SampleRequest":{"n":1}}""");
string Sha(byte[] b) => Convert.ToHexString(SHA256.HashData(b));
var reference = new MZP4CatalogReference("3.7.10.10",
    Sha(matching), Sha(fields), "synthetic-test-only");
var opened = MZP4ProtocolAdapter.Open(matching, fields, reference);
Check(opened.IsOfflineCandidate && opened.Adapter is not null &&
    !opened.IsRuntimeCertified, "real MZP4 admits only offline candidate");
var catalogue = opened.Adapter!;
var scope = new MZP5SourceScope("manual-fixture", "local-test", 75, 1,
    reference.ClientVersion, reference.MatchingSha256, reference.FieldsSha256);

var panel = new MZP6ProtocolInspectorPanel();
var window = new Window { Width = 1100, Height = 790, Content = panel };
window.Show();
Check(window.Content == panel && panel.Content is ScrollViewer,
    "real UI panel hosted in Avalonia Headless window");
Check(!panel.IsOfflineCandidate && !panel.HasAuthenticSource &&
    !panel.ServerAckCertified && !panel.CanConnect &&
    panel.ReadinessText.Contains("Aucun catalogue"),
    "no default provider, fake auth, ACK or connection");
T Find<T>(string id) where T : Control =>
    panel.GetLogicalDescendants().OfType<T>().Single(x => x.Name == id);
var hex = Find<TextBox>("MZP6HexInput");
var seq = Find<TextBox>("MZP6SequenceInput");
var direction = Find<ComboBox>("MZP6Direction");
var analyze = Find<Button>("MZP6Inspect");
var clear = Find<Button>("MZP6Clear");
void Press(Button button) => button.RaiseEvent(new RoutedEventArgs(Button.ClickEvent));
void Feed(byte[] b, long n, int d = 2)
{
    hex.Text = Convert.ToHexString(b);
    seq.Text = n.ToString(System.Globalization.CultureInfo.InvariantCulture);
    direction.SelectedIndex = d;
}
string Visible() => string.Join("|", panel.GetLogicalDescendants()
    .OfType<TextBlock>().Select(x => x.Text)) + panel.ResultText + panel.ReadinessText;
void Denied(string reason, string description)
{
    Press(analyze);
    Check(panel.ResultText.Contains(reason) && panel.DisplayedFieldCount == 0 &&
        panel.InputCharacterCount == 0 && !panel.ServerAckCertified, description);
}
byte[] Payload(string confidential) => new MZP3MessageBuilder()
    .AddUtf8(2, confidential).AddVarint(80, 987654321).Build().Encode();
byte[] Make(MZP5Branch branch, string code, string confidential)
{
    var any = new MZP3MessageBuilder()
        .AddUtf8(1, MZP3EnvelopeCodec.TypeUrlPrefix + code)
        .AddBytes(2, Payload(confidential)).Build().Encode();
    var nested = new MZP3MessageBuilder()
        .AddBytes(branch == MZP5Branch.Event ? 4 : 2, any);
    if (branch == MZP5Branch.Response) nested.AddSignedInt64(6, 123456789);
    return new MZP3MessageBuilder()
        .AddBytes(branch == MZP5Branch.Event ? 5 : 9, nested.Build().Encode())
        .Build().Encode();
}

Check(!analyze.IsEnabled, "no inspect button without catalogue");
Feed(Make(MZP5Branch.Event, "e2", "PRECONF_SECRET"), 1);
Check(!panel.Configure(null, scope) && panel.InputCharacterCount == 0,
    "missing catalogue refused and old pasted data cleared");
Check(!panel.Configure(catalogue, null), "missing scope rejected");
Check(!panel.Configure(catalogue, scope with { DeclaredClientVersion = "3.6.12.16" }),
    "wrong client version rejected");
Check(!panel.Configure(catalogue, scope with { MatchingSha256 = new string('A', 64) }),
    "matching SHA mismatch rejected");
Check(!panel.Configure(catalogue, scope with { FieldsSha256 = new string('0', 64) }),
    "fields SHA mismatch rejected");
Check(!panel.Configure(catalogue, scope with { SourceLabel = "" }),
    "unknown source label rejected");
Check(!panel.Configure(catalogue, scope with { ConnectionId = 0 }),
    "unknown connection rejected");
Check(!panel.Configure(catalogue, scope with { Generation = 0 }),
    "unknown generation rejected");
Check(!panel.IsOfflineCandidate && !analyze.IsEnabled,
    "invalid admission remains disabled and fail-closed");

Check(panel.Configure(catalogue, scope) && panel.IsOfflineCandidate &&
    analyze.IsEnabled && panel.ReadinessText.Contains("3.7.10.10") &&
    panel.ReadinessText.Contains("AUCUNE attestation"),
    "explicit SHA-pinned catalogue and scope enable offline inspection only");
Feed(Make(MZP5Branch.Event, "e2", "PRIVATE_DIRECTION"), 1, 0);
Denied("DIRECTION_NOT_EXPLICIT", "no direction does not consume sequence");
Feed(Make(MZP5Branch.Event, "e2", "PRIVATE_SEQUENCE"), 0);
Denied("INVALID_SEQUENCE", "zero sequence rejected");
Feed(Make(MZP5Branch.Event, "e2", "PRIVATE_SEQUENCE"), 1);
seq.Text = "one";
Denied("INVALID_SEQUENCE", "non-numeric sequence rejected");
hex.Text = "GG"; seq.Text = "1"; direction.SelectedIndex = 2;
Denied("HEX_SYNTAX_INVALID", "invalid hex rejected");
hex.Text = "ABC";
Denied("HEX_LENGTH_INVALID", "odd hex nibble rejected");
hex.Text = "0x1A";
Denied("HEX_SYNTAX_INVALID", "hex prefix never silently corrected");
hex.Text = "   ";
Denied("EMPTY_OR_EXCESSIVE_BYTES", "blank bytes rejected");

Feed(Make(MZP5Branch.Event, "e2", "ULTRA_PRIVATE_PAYLOAD_34"), 1);
Press(analyze);
Check(panel.ResultText.Contains("Forme parsée") &&
    panel.DisplayedFieldCount == 2 && panel.InputCharacterCount == 0,
    "known Event shows only field count, wire, length and scrubs input");
Check(!Visible().Contains("ULTRA_PRIVATE_PAYLOAD_34") &&
    !Visible().Contains("987654321") &&
    !Visible().Contains("manual-fixture") &&
    !Visible().Contains("e2") &&
    !Visible().Contains("SampleEvent") &&
    Visible().Contains("LengthDelimited") &&
    Visible().Contains("Varint"),
    "secret values and source/alias absent from visible output");
Feed(Make(MZP5Branch.Response, "r3", "RESPONSE_SECRET_9"), 2);
Press(analyze);
Check(panel.ResultText.Contains("ACK : NON CERTIFIÉS") &&
    Visible().Contains("Response") &&
    !Visible().Contains("RESPONSE_SECRET_9") &&
    !Visible().Contains("123456789") &&
    panel.HistoryCount >= 2 && panel.HistoryCount <= 12,
    "Response UID not shown, never ACK; local redacted history");

Feed(Make(MZP5Branch.Event, "e2", "REPLAY"), 2);
Denied("SequenceReplayOrReorder", "replayed sequence refused");
Feed(Make(MZP5Branch.Event, "e2", "QUARANTINE"), 3);
Denied("StreamQuarantined", "replay quarantines direction");
Check(panel.IsOfflineCandidate, "MZP5 quarantines direction, not entire scope");
Check(!panel.Configure(catalogue, scope) && !panel.IsOfflineCandidate &&
    panel.ReadinessText.Contains("Génération déjà consommée"),
    "cannot evade replay quarantine by reconfiguring identical scope");

Check(panel.Configure(catalogue, scope with { Generation = 2 }),
    "new explicit scope restarts fresh reader");
Feed(Make(MZP5Branch.Event, "invented", "UNKNOWN_TYPE_SECRET"), 1);
Denied("UNKNOWN_MESSAGE_TYPE", "unknown type refused even if protobuf shape valid");
Check(!panel.IsOfflineCandidate && !analyze.IsEnabled,
    "unknown type locks panel until reconfiguration");

Check(panel.Configure(catalogue, scope with { Generation = 3 }),
    "explicitly configure new generation");
Feed(Make(MZP5Branch.Event, "e2", "MALFORMED"), 1);
hex.Text = "00";
Denied("MalformedWire", "wire malformed refused using real MZP5");
Feed(Make(MZP5Branch.Event, "e2", "QUARANTINED"), 2);
Denied("StreamQuarantined", "malformed wire quarantines direction");
Check(panel.Configure(catalogue, scope with { Generation = 4 }),
    "explicit scope generation after bad wire");
Feed(Make(MZP5Branch.Event, "e2", "GAP"), 2);
Denied("SequenceGap", "sequence gap refused using MZP5");
Check(panel.Configure(catalogue, scope with { Generation = 5 }),
    "explicitly configure next generation");
Feed(Make(MZP5Branch.Event, "e2", "BAD_DIRECTION"), 1, 1);
Denied("DirectionConflict", "Event claimed from client is refused");

Check(panel.Configure(catalogue, scope with { Generation = 6 }),
    "fresh scope for valid MZP4 request");
byte[] request = catalogue.BuildClientRequest(
    catalogue.ForMessage("SampleRequest").Int64("n", 789), -5);
Feed(request, 1, 1);
Press(analyze);
Check(panel.ResultText.Contains("Forme parsée") &&
    Visible().Contains("Request") && panel.DisplayedFieldCount == 1 &&
    !Visible().Contains("-5"),
    "real MZP4 request -> real MZP5 inbound -> UI sans UID");
Feed(Make(MZP5Branch.Event, "e2", "ERASE_ME"), 1);
Press(clear);
Check(panel.InputCharacterCount == 0 && panel.ResultText.Contains("effacés"),
    "manual clear button scrubs pasted hex");
Check(panel.Configure(catalogue, scope with { Generation = 7 }) &&
    panel.DisplayedFieldCount == 0 && panel.HistoryCount == 0,
    "new Configure resets all former display and history");
Check(!Visible().Contains("ULTRA_PRIVATE_PAYLOAD_34") &&
    !Visible().Contains("RESPONSE_SECRET_9") &&
    !panel.ServerAckCertified && !panel.HasAuthenticSource,
    "all sensitive values remain hidden and all trust claims false");

window.Close();
Console.WriteLine($"MZP6 Avalonia Headless: PASS ({count} checks), " +
    "in-memory synthetic-only, no app runtime/network/process.");

sealed class OfflineApp : Application { }
