using System.Security.Cryptography;
using System.Text;
using GivreClone.Protocol;
using Xunit;

public sealed class MZP0ProtocolAdmissionTests
{
    private const string GoodMatching =
        "{\"type.ankama.com/abc\":\"MessageType.GameMessage\",\"type.ankama.com/xyz\":\"MessageType.MapEvent\"}";
    private const string GoodFields =
        "{\"GameMessage\":{\"uid\":1,\"payload\":2},\"MapEvent\":{\"actor_id\":1}}";

    private static MZP0MappingReview Check(string? matching = null, string? fields = null,
        string version = MZP0ProtocolAdmission.TargetVersion, bool badHash = false,
        IReadOnlyCollection<MZP0RequiredField>? required = null)
    {
        var m = Encoding.UTF8.GetBytes(matching ?? GoodMatching);
        var f = Encoding.UTF8.GetBytes(fields ?? GoodFields);
        var pinM = Convert.ToHexString(SHA256.HashData(m));
        var pinF = Convert.ToHexString(SHA256.HashData(f));
        return MZP0ProtocolAdmission.Review(m, f, version, badHash ? new string('0', 64) : pinM,
            pinF, required ?? new[]
            {
                new MZP0RequiredField("GameMessage", "uid"),
                new MZP0RequiredField("MapEvent", "actor_id")
            });
    }

    [Fact]
    public void Matching_pinned_offline_catalogue_is_not_runtime_authorization()
    {
        var review = Check();
        Assert.Equal(MZP0MappingVerdict.OfflineCandidateOnly, review.Verdict);
        Assert.Equal("CANDIDATE_NOT_LIVE_ATTESTED", review.Reason);
        Assert.Equal(2, review.AliasCount);
        Assert.Equal(2, review.TypeCount);
        Assert.Equal(3, review.FieldCount);
        Assert.Empty(review.MissingRequired);
        Assert.Empty(review.AmbiguousTags);
        Assert.False(review.PermitsGameplay);
    }

    [Fact]
    public void Version_and_sha_require_explicit_exact_pinning()
    {
        Assert.Equal("VERSION_UNVERIFIED", Check(version: "3.6.12.16").Reason);
        Assert.Equal("VERSION_UNVERIFIED", Check(version: "3.8.0.0").Reason);
        Assert.Equal("VERSION_UNVERIFIED", Check(version: "3.07.10.10").Reason);
        Assert.Equal("VERSION_UNVERIFIED", Check(version: "3.7.10.10x").Reason);
        Assert.Equal(MZP0MappingVerdict.OfflineCandidateOnly,
            Check(version: "3.7.11.11").Verdict);
        Assert.Equal(MZP0MappingVerdict.OfflineCandidateOnly,
            Check(version: "3.7.99.999").Verdict);
        Assert.Equal("SHA256_PIN_MISMATCH", Check(badHash: true).Reason);
        Assert.Equal("SHA256_PIN_MISMATCH",
            Check(version: "3.7.11.11", badHash: true).Reason);
        Assert.Equal(MZP0MappingVerdict.Blocked, Check(badHash: true).Verdict);
    }

    [Fact]
    public void Missing_required_field_does_not_get_promoted()
    {
        var review = Check(fields: "{\"GameMessage\":{\"uid\":1},\"MapEvent\":{}}",
            required: new[] { new MZP0RequiredField("GameMessage", "payload") });
        Assert.Equal(MZP0MappingVerdict.RequiresReview, review.Verdict);
        Assert.Equal("REQUIRED_FIELDS_MISSING", review.Reason);
        Assert.Single(review.MissingRequired);
        Assert.False(review.PermitsGameplay);
    }

    [Fact]
    public void Colliding_tag_retains_both_names_for_manual_resolution()
    {
        var review = Check(fields: "{\"GameMessage\":{\"uid\":1,\"payload\":2},\"MapEvent\":{\"actor_id\":1,\"entity_id\":1}}");
        Assert.Equal(MZP0MappingVerdict.RequiresReview, review.Verdict);
        Assert.Equal("AMBIGUOUS_TAGS", review.Reason);
        var item = Assert.Single(review.AmbiguousTags);
        Assert.Equal("MapEvent", item.Message);
        Assert.Equal(1, item.Tag);
        Assert.Equal(new[] { "actor_id", "entity_id" }, item.CandidateFieldNames);
        Assert.False(review.PermitsGameplay);
    }

    [Theory]
    [InlineData(0)]
    [InlineData(-1)]
    [InlineData(19000)]
    [InlineData(19999)]
    [InlineData(536870912)]
    public void Illegal_tags_rejected(int tag)
    {
        var review = Check(fields: "{\"GameMessage\":{\"uid\":" + tag +
            "},\"MapEvent\":{\"actor_id\":1}}");
        Assert.Equal(MZP0MappingVerdict.Blocked, review.Verdict);
        Assert.Equal("FIELD_TAG_INVALID_OR_DUPLICATE", review.Reason);
    }

    [Fact]
    public void Duplicate_json_alias_key_rejected()
    {
        var review = Check(matching:
            "{\"type.ankama.com/abc\":\"MessageType.GameMessage\",\"type.ankama.com/abc\":\"MessageType.MapEvent\"}");
        Assert.Equal("DUPLICATE_OR_CONFLICTING_ALIAS", review.Reason);
    }

    [Fact]
    public void Duplicate_clear_message_name_rejected()
    {
        var review = Check(matching:
            "{\"type.ankama.com/abc\":\"MessageType.GameMessage\",\"type.ankama.com/xyz\":\"MessageType.GameMessage\"}");
        Assert.Equal("DUPLICATE_OR_CONFLICTING_ALIAS", review.Reason);
    }

    [Fact]
    public void Duplicate_field_name_rejected_without_last_write_wins()
    {
        var review = Check(fields:
            "{\"GameMessage\":{\"uid\":1,\"uid\":2},\"MapEvent\":{\"actor_id\":1}}");
        Assert.Equal("FIELD_TAG_INVALID_OR_DUPLICATE", review.Reason);
    }

    [Fact]
    public void Malformed_json_missing_required_manifest_and_invalid_shape_fail_closed()
    {
        Assert.Equal("MALFORMED_JSON", Check(matching: "{").Reason);
        Assert.Equal("REQUIRED_MANIFEST_MISSING", Check(required: Array.Empty<MZP0RequiredField>()).Reason);
        Assert.Equal("CATALOGUE_SHAPE_INVALID", Check(fields: "[]").Reason);
        Assert.Equal("ALIAS_SHAPE_INVALID", Check(matching: "{\"bad/prefix\":\"MessageType.GameMessage\"}").Reason);
    }
}
