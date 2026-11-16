using System.Text.Json;
using GivreLauncher.Core.Ankama;
using Xunit;
namespace GivreClone.Tests;
public class LauncherAccountReaderTests
{
    [Fact]
    public void Imports_metadata_and_deduplicates_without_returning_secrets()
    {
        using var doc = JsonDocument.Parse("""{"USER_ACCOUNTS":[{"login":" alice ","nickname":"A","apiKey":"secret"},{"login":"ALICE"},{"login":"bob"},{"nickname":"invalid"},null]}""");
        var accounts = LauncherAccountReader.Parse(doc.RootElement);
        Assert.Equal(2, accounts.Count);
        Assert.Equal(new LauncherAccountMetadata("alice", "A"), accounts[0]);
        Assert.Equal(new LauncherAccountMetadata("bob", null), accounts[1]);
        Assert.DoesNotContain("secret", JsonSerializer.Serialize(accounts));
    }
    [Fact]
    public void Rejects_unknown_schema_instead_of_reporting_success()
    {
        using var doc = JsonDocument.Parse("{}");
        Assert.Throws<InvalidDataException>(() => LauncherAccountReader.Parse(doc.RootElement));
    }
    [Fact]
    public void Empty_list_is_valid()
    {
        using var doc = JsonDocument.Parse("""{"USER_ACCOUNTS":[]}""");
        Assert.Empty(LauncherAccountReader.Parse(doc.RootElement));
    }
}
