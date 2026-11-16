using System.Text.Json;
using GivreClone.App.Services.EquipmentSetData;

if (args.Length != 2)
{
    Console.Error.WriteLine("usage: ReaderSmoke <exported-directory> <declared-game-version>");
    return 2;
}

EquipmentSetCatalog result = EquipmentSetCatalogReader.LoadDirectory(args[0], args[1]);
if (!result.Admitted)
{
    Console.Error.WriteLine(string.Join("; ", result.Issues));
    return 3;
}

Console.WriteLine(JsonSerializer.Serialize(new
{
    admitted = result.Admitted,
    sets = result.Sets.Count,
    items = result.Items.Count,
    sourceHashValid = result.Source.LocalHashVerified,
    publisherVerified = result.Source.PublisherVerified,
    aiDisclosureAuthorized = result.Source.AiDisclosureAuthorized,
    memberIds = result.Sets.SelectMany(s => s.MemberItemIds).OrderBy(x => x).ToArray()
}));
return 0;
