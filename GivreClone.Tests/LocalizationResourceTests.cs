using System.Xml.Linq;
using Xunit;

namespace GivreClone.Tests;

public class LocalizationResourceTests
{
    [Theory]
    [InlineData("fr")]
    [InlineData("en")]
    [InlineData("es")]
    public void Translation_keys_are_unique_and_match_french(string language)
    {
        string directory = MappingScan.WalkUp(d => Directory.Exists(Path.Combine(d.FullName, "GivreClone.App")))?.FullName
            ?? throw new DirectoryNotFoundException();
        string[] Keys(string code) => XDocument.Load(Path.Combine(directory, "GivreClone.App", "Lang", code + ".axaml"))
            .Descendants().Attributes(XName.Get("Key", "http://schemas.microsoft.com/winfx/2006/xaml"))
            .Select(a => a.Value).OrderBy(k => k, StringComparer.Ordinal).ToArray();
        var keys = Keys(language);
        Assert.Equal(keys.Length, keys.Distinct(StringComparer.Ordinal).Count());
        Assert.Equal(Keys("fr"), keys);
    }
}
