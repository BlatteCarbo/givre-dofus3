extern alias F06App;
using System.Text.Json;
using GivreClone.Protocol;
using Xunit;
using BotSettings = F06App::GivreClone.App.Services.BotSettings;
using BotSettingsFile = F06App::GivreClone.App.Services.BotSettingsFile;

namespace GivreClone.Tests;

public class OrderedCharacteristicSettingsTests
{
    [Fact]
    public void Existing_characters_remain_opted_out()
    {
        var settings = JsonSerializer.Deserialize<BotSettings>("{}")!;
        Assert.False(settings.OrderedCharacteristicsEnabled);
        Assert.Empty(settings.OrderedCharacteristicTargets);
    }

    [Fact]
    public void Ordered_caps_and_remainder_round_trip_per_character()
    {
        var file = new BotSettingsFile();
        file.Settings["A"] = new BotSettings
        {
            OrderedCharacteristicsEnabled = true,
            OrderedCharacteristicTargets = new()
            {
                new("strength", 100), new("intelligence", 200), new("wisdom", null)
            }
        };
        file.Settings["B"] = new BotSettings();
        var loaded = JsonSerializer.Deserialize<BotSettingsFile>(JsonSerializer.Serialize(file))!;
        Assert.True(loaded.Settings["A"].OrderedCharacteristicsEnabled);
        Assert.Equal(new[] { new CharacteristicTarget("strength", 100), new("intelligence", 200), new("wisdom", null) },
            loaded.Settings["A"].OrderedCharacteristicTargets);
        Assert.False(loaded.Settings["B"].OrderedCharacteristicsEnabled);
        Assert.Empty(loaded.Settings["B"].OrderedCharacteristicTargets);
    }
}
