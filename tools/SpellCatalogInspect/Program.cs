using System.Text.Json;
using System.Security.Cryptography;
using GivreClone.App.Services;

if (args.Length == 0 || args.Length % 2 != 0)
{
    Console.Error.WriteLine("Usage: SpellCatalogInspect <spellId> <grade> [<spellId> <grade> ...]");
    return 2;
}

var content = DofusPaths.ContentDir();
if (content is null)
{
    Console.Error.WriteLine("Installed client content not found");
    return 1;
}
Console.Error.WriteLine($"Catalog content: {content}");
foreach (var bundle in Directory.EnumerateFiles(Path.Combine(content, "Data"), "*spelllevelsdataroot*.asset.bundle"))
{
    using var stream = File.OpenRead(bundle);
    Console.Error.WriteLine($"Catalog bundle: {Path.GetFileName(bundle)} SHA256={Convert.ToHexString(SHA256.HashData(stream))}");
}
var names = new GameNameResolver();
names.EnsureCombatLoaded();
for (int i = 0; i < args.Length; i += 2)
{
    int id = int.Parse(args[i]);
    int grade = int.Parse(args[i + 1]);
    var descriptor = names.GetSpellDescriptor(id, grade);
    if (descriptor is null)
    {
        Console.Error.WriteLine($"Missing spell {id} grade {grade}");
        return 1;
    }
    Console.WriteLine(JsonSerializer.Serialize(descriptor));
}
return 0;
