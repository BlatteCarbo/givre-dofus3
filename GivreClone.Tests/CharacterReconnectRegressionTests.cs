extern alias F06App;

using System.Collections.Concurrent;
using System.Reflection;
using System.Runtime.CompilerServices;
using GivreClone.Protocol;
using LaunchService = F06App::GivreClone.App.Services.LaunchService;
using Xunit;

namespace GivreClone.Tests;

public sealed class CharacterReconnectRegressionTests
{
    private const uint Pid = 0x40000001;
    private const long Character = 623456789012;
    private static readonly CharacterInfo Info = new("Testeur", 80, "Crâ", "Homme", 8, 0);

    private static LaunchService OfflineService()
    {
        var service = (LaunchService)RuntimeHelpers.GetUninitializedObject(typeof(LaunchService));
        foreach (var name in new[] { "_charId", "_selfInfo", "_characters", "_charLevel" })
        {
            var field = typeof(LaunchService).GetField(name, BindingFlags.Instance | BindingFlags.NonPublic)!;
            field.SetValue(service, Activator.CreateInstance(field.FieldType));
        }
        return service;
    }

    [Fact]
    public void Server_selection_restores_identity_without_the_account_list()
    {
        var service = OfflineService();
        CharacterInfo? observed = null;
        service.CharacterIdentified += (pid, info) => { Assert.Equal(Pid, pid); observed = info; };
        service.AdoptCharacterId(Pid, Character, "selection serveur", Info);
        Assert.Equal(Character, service.CharacterId(Pid));
        Assert.Equal(Info, service.SelfCharacterInfo(Pid));
        Assert.Equal(80, service.CharacterLevel(Pid));
        Assert.Equal(Info, observed);
        Assert.Equal(0, service.CharacterId(Pid + 1));
    }

    [Fact]
    public void Identity_can_arrive_after_the_fighter_id_and_is_published_once()
    {
        var service = OfflineService();
        int notifications = 0;
        service.CharacterIdentified += (_, _) => notifications++;
        service.AdoptCharacterId(Pid, Character);
        Assert.Null(service.SelfCharacterInfo(Pid));
        service.AdoptCharacterId(Pid, Character, "selection serveur", Info);
        service.AdoptCharacterId(Pid, Character, "selection serveur", Info);
        Assert.Equal(Info, service.SelfCharacterInfo(Pid));
        Assert.Equal(1, notifications);
    }

    [Fact]
    public void Combat_fallback_uses_the_matching_account_character()
    {
        var service = OfflineService();
        var field = typeof(LaunchService).GetField("_characters", BindingFlags.Instance | BindingFlags.NonPublic)!;
        var characters = (ConcurrentDictionary<uint, IReadOnlyList<(long Id, CharacterInfo Info)>>)field.GetValue(service)!;
        characters[Pid] = [(Character - 1, Info with { Name = "Autre" }), (Character, Info)];
        service.AdoptCharacterId(Pid, Character, "combattant du compte");
        Assert.Equal(Info, service.SelfCharacterInfo(Pid));
        Assert.Equal(80, service.CharacterLevel(Pid));
    }

    [Fact]
    public void Another_fighter_cannot_replace_the_selected_character()
    {
        var service = OfflineService();
        service.AdoptCharacterId(Pid, Character, selectedInfo: Info);
        service.AdoptCharacterId(Pid, Character + 1, selectedInfo: Info with { Name = "Autre" });
        service.AdoptCharacterId(Pid, -1);
        service.AdoptCharacterId(Pid, 0);
        Assert.Equal(Character, service.CharacterId(Pid));
        Assert.Equal(Info, service.SelfCharacterInfo(Pid));
    }
}
