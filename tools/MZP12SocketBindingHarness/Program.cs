using GivreClone.App.Services;

var count = 0;
void Pass(bool value, string desc)
{
    if (!value) throw new InvalidOperationException("MZP12: " + desc);
    ++count;
}
var bound = new MZP12SocketVersionBinding.Binding("3.7.11.11", new string('A', 64));
MZP11CytrusVersionProbe.Result Current(string version, string sha) =>
    new(version, "6.0", sha, "synthetic test evidence");

Pass(MZP12SocketVersionBinding.Evaluate(bound, Current("3.7.11.11", new string('A', 64)))
    .StructurallyConsistent, "exact mapping + installed manifest candidate");
var valid = MZP12SocketVersionBinding.Evaluate(bound, Current("3.7.11.11", new string('A', 64)));
Pass(!valid.ServerAck && !valid.Authenticated && !valid.GameplayCertified,
    "local byte-matching never certifies gameplay");
Pass(MZP12SocketVersionBinding.Evaluate(null, Current("3.7.11.11", new string('A', 64))).Reason ==
    MZP12SocketVersionBinding.Refusal.MappingNotPublished, "no catalogue blocked");
Pass(MZP12SocketVersionBinding.Evaluate(new(null, new string('A', 64)),
    Current("3.7.11.11", new string('A', 64))).Reason ==
    MZP12SocketVersionBinding.Refusal.MappingNotPublished, "null mapping build blocked");
Pass(MZP12SocketVersionBinding.Evaluate(new("3.7.11.11", null),
    Current("3.7.11.11", new string('A', 64))).Reason ==
    MZP12SocketVersionBinding.Refusal.MappingNotPublished, "unknown original manifest digest blocked");
Pass(MZP12SocketVersionBinding.Evaluate(bound, null).Reason ==
    MZP12SocketVersionBinding.Refusal.ManifestMissingOrReplaced, "missing current manifest blocked");
Pass(MZP12SocketVersionBinding.Evaluate(bound, new(null, null, null, "missing")).Reason ==
    MZP12SocketVersionBinding.Refusal.ManifestMissingOrReplaced, "unreadable current game version blocked");
Pass(MZP12SocketVersionBinding.Evaluate(bound, Current("3.7.10.10", new string('A', 64))).Reason ==
    MZP12SocketVersionBinding.Refusal.None, "old 3.7 patch shares protocol");
Pass(MZP12SocketVersionBinding.Evaluate(bound, Current("3.8.0.0", new string('A', 64))).Reason ==
    MZP12SocketVersionBinding.Refusal.MappingBuildMismatch, "new 3.8 minor blocked");
Pass(MZP12SocketVersionBinding.Evaluate(bound, Current("3.7.11.11", new string('B', 64))).Reason ==
    MZP12SocketVersionBinding.Refusal.None, "same version changed manifest retains protocol");
Pass(MZP12SocketVersionBinding.Evaluate(bound, Current("3.7.99.1", new string('B', 64))).Reason ==
    MZP12SocketVersionBinding.Refusal.None, "changed patch and hash retain 3.7 protocol");
Pass(MZP12SocketVersionBinding.CheckServerRequiredBuild(bound, "3.7.12.12").Reason ==
    MZP12SocketVersionBinding.Refusal.None, "server required another patch in same family");
Pass(MZP12SocketVersionBinding.CheckServerRequiredBuild(bound, "3.8.0.0").Reason ==
    MZP12SocketVersionBinding.Refusal.ServerRequiredDifferentBuild, "required minor mismatch");
Pass(MZP12SocketVersionBinding.CheckServerRequiredBuild(bound, "").Reason ==
    MZP12SocketVersionBinding.Refusal.ServerRequiredDifferentBuild, "empty required version cannot bypass");
Pass(MZP12SocketVersionBinding.CheckServerRequiredBuild(null, "3.7.11.11").Reason ==
    MZP12SocketVersionBinding.Refusal.MappingNotPublished, "required version without catalogue rejected");
Pass(MZP12SocketVersionBinding.CheckServerRequiredBuild(bound, "3.7.11.11").StructurallyConsistent,
    "exact required build structurally consistent, not a server ACK");
var reviewed = bound with { OperatorReviewedTarget = "3.8.0.0", IdenticalProtoSha256 = new string('C', 64) };
Pass(!MZP12SocketVersionBinding.Evaluate(reviewed, Current("3.8.0.0", new string('B', 64))).StructurallyConsistent,
    "operator review never crosses family");
Pass(!MZP12SocketVersionBinding.CheckServerRequiredBuild(reviewed, "3.8.0.0").StructurallyConsistent,
    "required version review never crosses family");
Pass(MZP12SocketVersionBinding.IsUnrestrictedSameBuildCandidate(bound, "3.7.99.1", true),
    "historical review flag does not restrict same family gameplay");
Pass(!MZP12SocketVersionBinding.IsUnrestrictedSameBuildCandidate(reviewed, "3.8.0.0", false),
    "cross family cannot enable gameplay");
var nextFamily = new MZP12SocketVersionBinding.Binding("3.8.0.0", new string('A', 64));
Pass(MZP12SocketVersionBinding.Evaluate(nextFamily, Current("3.8.12.1", new string('B', 64))).StructurallyConsistent,
    "3.8 patches require their own family catalogue");
Console.WriteLine($"MZP12 Socket version binding: PASS ({count} offline checks). No socket, token, original process or game session.");
