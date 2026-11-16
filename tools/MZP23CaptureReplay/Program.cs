// Rejoue un flux TCP capturé (TSV : stream, srcport, payload hex ; ordre chronologique) dans le décodeur du clone et mesure la
// couverture du mapping SANS jamais écrire de valeur : seulement noms de messages et NUMÉROS de champ.
using System.Globalization;
using GivreClone.App.Services;
using GivreClone.Protocol;
using GivreCore.Protobuf;
static string Shape(List<ProtoField> t) => "{" + string.Join(",", t.Select(f => f.Field + (f.Wire == WireType.Varint ? "v" : f.Message is { Count: > 0 } ? Shape(f.Message) : f.String is { } st && st.StartsWith("type.ankama") ? "T" : "b"))) + "}";

var tsv = args[0]; var dataDir = args[1];
// Option : noms de messages dont on affiche l'arbre de champs (numéros et entiers seulement ; jamais de texte ni d'octets).
var dumpNames = args.Length > 2 ? new HashSet<string>(args[2].Split(',', StringSplitOptions.RemoveEmptyEntries)) : new HashSet<string>();
static string Tree(IReadOnlyList<ProtoField> t) => "{" + string.Join(" ", t.Select(f =>
    f.Wire == WireType.Varint ? $"#{f.Field}={f.AsSignedVarint}"
    : f.Message is { Count: > 0 } ? $"#{f.Field}:{Tree(f.Message)}"
    : $"#{f.Field}=<{(f.Bytes?.Length ?? f.String?.Length ?? 0)} octets>")) + "}";
var cat = ProtocolCatalog.LoadFromDirectory(dataDir);
var dec = new CaptureDecoder(cat);
MoveForge.UseCatalog(cat.CodeForName, cat.FieldNumber);
EnvelopeBuilder.UseCatalog(cat.FieldNumber);
int total = 0, unnamed = 0, noFields = 0;
var cmp = new SortedDictionary<string, string>();
var nf = new SortedDictionary<string, int>();
var lotm = new SortedDictionary<string, string>();
LotMReader.UseCatalog(cat.FieldNumber);
void LotM(DecodedPacket p)
{
    string? r = p.Name switch
    {
        "AchievementsEvent" => $"{LotMReader.AchievedList(p.Payload).Count} succès terminés",
        "AchievementFinishedEvent" => LotMReader.Finished(p.Payload) is { } f ? $"succès {f.Id} niveau {f.FinishedLevel}" : "illisible",
        "AnomalyStateEvent" => LotMReader.AnomalyState(p.Payload) is var a ? $"sous-zone {a.SubareaId} ouverte={a.Open}" : null,
        "AnomalySubareaInformationEvent" => $"{LotMReader.AnomalySubareas(p.Payload).Count} sous-zones",
        "DocumentReadingBeginEvent" => $"document {LotMReader.DocumentId(p.Payload)}",
        "ExchangeNpcShopStartedEvent" => LotMReader.NpcShop(p.Payload) is var sh ? $"boutique pnj {sh.NpcId} : " + string.Join(" ; ", sh.Items.Select(i => $"gid {i.Gid} prix {i.Price}")) : null,
        "ObjectAveragePricesHistoryEvent" => $"{LotMReader.PriceHistory(p.Payload).Count} points de prix",
        _ => null,
    };
    if (r is not null) lotm[p.Name!] = r;
}
var byName = new SortedDictionary<string, int>();
var unmappedFields = new SortedDictionary<string, SortedSet<int>>();   // message -> numéros de champ vus, absents du mapping
var seenFields = new SortedDictionary<string, SortedSet<int>>();
dec.Packet += p =>
{
    total++;
    if (p.Name is null) { unnamed++; return; }
    byName[p.Name] = byName.GetValueOrDefault(p.Name) + 1;
    LotM(p);
    if (dumpNames.Contains(p.Name!) || dumpNames.Any(d => d.EndsWith("*") && p.Name!.StartsWith(d[..^1], StringComparison.Ordinal))) Console.WriteLine($"[{(p.ClientToServer ? "C->S" : "S->C")}] {p.Name} {Tree(p.Payload)}");
    if (p.ClientToServer && p.Name == "MapInformationRequest" && !cmp.ContainsKey("MapInformationRequest"))
    {
        long mid = PayloadReader.Int(p.Payload, cat.FieldNumber("MapInformationRequest", "map_id") ?? -1, 0);
        var fr = RawProtobuf.ConsumeFrames(MoveForge.BuildMapInformationRequestFramed(mid), out _);
        cmp["MapInformationRequest"] = "réel=" + Shape(RawProtobuf.Parse(p.RawFrame)) + " forgé=" + (fr.Count > 0 ? Shape(RawProtobuf.Parse(fr[0])) : "aucune trame");
    }
    if (!cat.HasFields(p.Name)) { noFields++; nf[p.Name] = nf.GetValueOrDefault(p.Name) + 1; return; }
    foreach (var f in p.Payload)
    {
        if (!seenFields.TryGetValue(p.Name, out var s)) seenFields[p.Name] = s = new();
        s.Add(f.Field);
        if (cat.FieldName(p.Name, f.Field) is null)
        {
            if (!unmappedFields.TryGetValue(p.Name, out var u)) unmappedFields[p.Name] = u = new();
            u.Add(f.Field);
        }
    }
};
foreach (var line in File.ReadLines(tsv))
{
    var a = line.Split('\t'); if (a.Length < 3 || a[2].Length == 0) continue;
    uint stream = uint.Parse(a[0]); bool up = a[1] != "5555";
    dec.Feed(stream, up, Convert.FromHexString(a[2].Replace(":", "")));
}
Console.WriteLine($"messages décodés: {total} | sans nom: {unnamed} | nommés sans définition de champs: {noFields} | noms distincts: {byName.Count}");
int bad = unmappedFields.Count;
Console.WriteLine($"messages avec champs réels ABSENTS du mapping: {bad}");
foreach (var (n, s) in unmappedFields) Console.WriteLine($"  {n} ({byName[n]}x) champs inconnus: {string.Join(",", s)} | vus: {string.Join(",", seenFields[n])}");
Console.WriteLine("sans définition de champs (" + nf.Count + " noms): " + string.Join(", ", nf.OrderByDescending(k => k.Value).Take(40).Select(k => $"{k.Key}×{k.Value}")));
Console.WriteLine("lot M vus dans la capture: " + (lotm.Count == 0 ? "aucun" : string.Join(" | ", lotm.Select(k => $"{k.Key}: {k.Value}"))));
Console.WriteLine("forme réel vs forgé: " + (cmp.Count == 0 ? "rien à comparer" : string.Join(" | ", cmp.Select(k => $"{k.Key}: {k.Value}"))));
Console.WriteLine("top messages: " + string.Join(", ", byName.OrderByDescending(k => k.Value).Take(15).Select(k => $"{k.Key}×{k.Value}")));
