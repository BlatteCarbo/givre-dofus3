using System;
using System.IO;
using GivreClone.Protocol;
using Xunit;

namespace GivreClone.Tests;

// Le contrat du mapping : la prod ne lit QUE matching_current.json + fields.json (copies de
// GivreProxy/pipelines/mapping-extract/out/<version>/givre/, la référence). Aucun pin confirmed._fields : ces tests chargent
// le catalogue SANS confirmed.json, pour qu'un champ qui ne tiendrait que sur un pin local échoue ici au lieu d'être inerte
// en prod (cas des ventes hors ligne, 04/10 : « champ absent du mapping : ObjectPriceDateEffect.price » en AOT).
public class HdvOfflineSalesMappingTests
{
    public static TheoryData<string> MappingDirs() => new() { "data", "server-mapping" };

    // Ventes HDV hors ligne : le contrat actuel est dérivé de matching_current.json et fields.json.
    // Les noms gbza/gbzc sont opaques ; leur sémantique exacte ne se déduit pas de leur numéro.
    [Theory]
    [MemberData(nameof(MappingDirs))]
    public void Offline_sold_items_resolve_from_matching_and_fields_only(string dir)
    {
        var cat = LoadWithoutPins(dir);

        Assert.Equal("ExchangeBidHouseOfflineSoldItemsEvent",
            cat.NameForCode(cat.CodeForName("ExchangeBidHouseOfflineSoldItemsEvent")!));
        Assert.NotEqual("kdk", cat.CodeForName("ExchangeBidHouseOfflineSoldItemsEvent")); // ancien code, désormais ExchangeStartedEvent
        Assert.Equal(1, cat.FieldNumber("ExchangeBidHouseOfflineSoldItemsEvent", "bid_house_items"));
        Assert.Equal(2, cat.FieldNumber("ObjectGidWithQuantity", "quantity"));
        Assert.Equal(4, cat.FieldNumber("ObjectGidWithQuantity", "price_date_effect"));
        Assert.Equal(1, cat.FieldNumber("ObjectGidWithQuantity", "object_gid"));
        Assert.Equal(1, cat.FieldNumber("ObjectPriceDateEffect", "gbza"));
        Assert.Equal(3, cat.FieldNumber("ObjectPriceDateEffect", "gbzc"));
    }

    // Socket : ne jamais inventer ClientChallengeInitRequest.fyvk, absent du mapping actuel.
    // La sélection conserve bien success et Character.id ; la re-vérification reste non certifiée.
    [Theory]
    [MemberData(nameof(MappingDirs))]
    public void Socket_selection_and_challenge_resolve_from_matching_and_fields_only(string dir)
    {
        var cat = LoadWithoutPins(dir);

        Assert.Null(cat.FieldNumber("ClientChallengeInitRequest", "fyvk"));
        Assert.Equal(1, cat.FieldNumber("ClientChallengeInitRequest", "challenge_key"));
        Assert.Equal(2, cat.FieldNumber("ClientChallengeInitRequest", "fyvi"));
        Assert.Equal(4, cat.FieldNumber("ClientChallengeInitRequest", "fyvj"));
        Assert.Equal(1, cat.FieldNumber("CharacterSelectionEvent", "success"));
        Assert.Equal(3, cat.FieldNumber("Character", "id"));
    }

    private static ProtocolCatalog LoadWithoutPins(string dir)
    {
        var d = Locate(dir);
        return ProtocolCatalog.LoadFromStrings(null,
            File.ReadAllText(Path.Combine(d, "matching_current.json")), null,
            File.ReadAllText(Path.Combine(d, "fields.json")));
    }

    // data/ est copié à côté des tests ; server-mapping/ est lu à la racine du dépôt (bundle réellement servi).
    private static string Locate(string dir)
    {
        var local = Path.Combine(AppContext.BaseDirectory, dir);
        if (Directory.Exists(local)) return local;
        var found = MappingScan.WalkUp(d => Directory.Exists(Path.Combine(d.FullName, dir)));
        if (found is not null) return Path.Combine(found.FullName, dir);
        throw new DirectoryNotFoundException(dir);
    }
}
