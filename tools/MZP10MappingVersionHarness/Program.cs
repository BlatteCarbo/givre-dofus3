using GivreClone.App.Services;
using static GivreClone.App.Services.MZP10MappingVersionAdmission;

int checks = 0;
void Check(bool value, string label)
{
    if (!value) throw new Exception("MZP10 admission test FAIL: " + label);
    checks++;
}
IReadOnlyDictionary<string, string> Fixture(string marker) => new Dictionary<string, string>
{
    ["proto"] = "syntax = \"proto3\"; message Map { string name = 1; }" + marker,
    ["matching"] = "{\"type.ankama.com/abc\":\"MessageType.Map\"}",
    ["fields"] = "{\"Map\":{\"name\":1}}",
    ["confirmed"] = "{\"abc\":\"Map\"}"
};
Decision EvaluateVersion(string client, string served, bool fallback, string? reference = null,
    IReadOnlyDictionary<string, string>? newData = null,
    IReadOnlyDictionary<string, string>? priorData = null)
    => Evaluate(new(client, served, fallback, newData ?? Fixture("same"), reference, priorData));

Check(TryParse("3.7.10.10", out var mj, out var mn, out var p, out var b)
      && mj == 3 && mn == 7 && p == 10 && b == 10, "four part version");
foreach (var malformed in new[] { "3.7", "latest", "3.7.10", "3.7.10.10\n", "3.07.10.10",
    "-3.7.10.10", "3.7.10.10x", "999999999999.7.1.2", "0.0.0.0", "3.7.1.1.1" })
    Check(!TryParse(malformed, out _, out _, out _, out _), "bad version " + malformed.Replace('\n','_'));

var same = EvaluateVersion("3.7.10.10", "3.7.10.10", false);
Check(same.Status == Disposition.ExactBuildCandidate && same.ExactBuildCandidate,
    "candidate only for identical four part version");
Check(same.BundleDigest is { Length: 64 }, "non-secret artifact digest available");
Check(!same.MayInject && !same.AuthenticatedServer && !same.GameplayCertified,
    "version equality is never auth/ACK/gameplay");

var noReference = EvaluateVersion("3.7.11.12", "3.7.10.10", false);
Check(noReference.ProtocolFamilyCandidate && !noReference.ExactBuildCandidate,
    "same 3.7 family needs no patch reference");
var family = EvaluateVersion("3.7.11.12", "3.7.10.10", false,
    reference: "3.7.10.10", priorData: Fixture("same"));
Check(family.Status == Disposition.SameFamilyCandidate,
    "same family mapping is structurally compatible");
Check(!family.ExactBuildCandidate && !family.MayInject,
    "family admission alone is not authentication");
Check(EvaluateVersion("3.7.11.12", "3.7.10.10", false,
        reference: "3.7.10.10", priorData: Fixture("changed")).ProtocolFamilyCandidate,
    "changed artifact hash does not change protocol family");
Check(EvaluateVersion("3.8.0.0", "3.7.10.10", false).Status == Disposition.DifferentProtocolFamily,
    "3.8 vs 3.7 must be rejected");
Check(EvaluateVersion("3.6.12.16", "3.7.10.10", false).Status == Disposition.DifferentProtocolFamily,
    "3.6 vs 3.7 must be rejected");
Check(EvaluateVersion("3.7.10.10", "3.7.10.10", true).ProtocolFamilyCandidate,
    "same family fallback admitted");
Check(EvaluateVersion("3.7.99.1", "3.7.10.10", true).ProtocolFamilyCandidate,
    "different patch fallback within family admitted");
Check(!EvaluateVersion("3.8.0.0", "3.7.10.10", true).ProtocolFamilyCandidate,
    "fallback never crosses protocol families");
Check(EvaluateVersion("3.8.12.1", "3.8.0.0", true).ProtocolFamilyCandidate,
    "3.8 uses its own family catalogue");
Check(IsSameProtocolFamily("3.7.1.2", "3.7.99.9") &&
    !IsSameProtocolFamily("3.8.0.0", "3.7.99.9") &&
    !IsSameProtocolFamily("3.7", "3.7.1.2") &&
    !IsSameProtocolFamily(null, "3.7.1.2"), "strict shared family helper");
Check(EvaluateVersion("3.7.10.10", "latest", false).Status == Disposition.InvalidVersion,
    "latest is never an exact protocol build");
Check(EvaluateVersion("3.7.10.10", "3.7.10.10", false,
        newData: new Dictionary<string, string> { ["proto"] = "test" }).Status == Disposition.MissingEvidence,
    "incomplete mapping rejected");
Check(Digest(Fixture("same")) == Digest(Fixture("same")), "deterministic digest");
Check(Digest(Fixture("same")) != Digest(Fixture("changed")), "changed proto changes fingerprint");
var fieldChange = new Dictionary<string,string>(Fixture("same")) { ["fields"] = "{\"Map\":{\"name\":5}}" };
Check(Digest(fieldChange) != Digest(Fixture("same")), "changed field wire number is detected");
var confirmChange = new Dictionary<string,string>(Fixture("same")) { ["confirmed"] = "{\"abc\":\"Other\"}" };
Check(Digest(confirmChange) != Digest(Fixture("same")), "changed confirmed aliases detected");
var oversized = new Dictionary<string,string>(Fixture("same")) { ["fields"] = new string('X', 20_000_001) };
Check(Digest(oversized) is null, "oversized schema rejected");
Check(Evaluate(null).Status == Disposition.MissingEvidence, "missing witness refused");

Console.WriteLine($"MZP10 mapping family/version admission: PASS ({checks} checks). No gameplay/ACK or authenticated wire proof.");
