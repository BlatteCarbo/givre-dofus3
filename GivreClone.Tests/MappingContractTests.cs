using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Json;
using GivreClone.Protocol;
using Xunit;

namespace GivreClone.Tests;

/// <summary>
/// CONTRAT DU MAPPING. La prod ne lit que le bundle streamé : matching_current.json + fields.json (copies exactes de
/// GivreProxy/pipelines/mapping-extract/out/&lt;version&gt;/givre/) et le pont code -&gt; nom de confirmed.json. Les sections
/// « _aliases » et « _fields » de confirmed.json n'y sont PAS appliquées.
///
/// Deux régressions sont passées inaperçues pour cette raison (03-04/10) : les ventes HDV hors ligne (pin _fields) et tout
/// le décodage des actions de combat (poussée, mort, PV, PA/PM via _aliases). Le code demandait des noms que le fields.json
/// livré ne contient pas ; les tests chargeaient confirmed.json complet et passaient quand même.
///
/// Ces tests échouent si :
///  - une recherche (message, champ) écrite en clair dans le code, lecture OU forge, n'existe pas dans le fields.json livré ;
///  - le manifeste RequiredMapping (contrôlé contre le bundle streamé au chargement de l'app) n'est plus à jour ;
///  - confirmed.json, lorsqu'il existe (extension optionnelle), réintroduit une section que la prod n'applique pas ;
///  - les copies data/ et server-mapping/ divergent (ou divergent de la référence extraite, si elle est présente).
/// Régénérer le manifeste : GIVRE_UPDATE_MANIFEST=1 dotnet test --filter RequiredMapping
/// </summary>
public class MappingContractTests
{
    public static TheoryData<string> MappingDirs() => new() { "data", "server-mapping" };

    [Theory]
    [MemberData(nameof(MappingDirs))]
    public void Every_lookup_written_in_the_code_exists_in_the_shipped_fields_json(string dir)
    {
        var root = MappingScan.RepoRoot();
        var fields = LoadFields(Path.Combine(root, dir, "fields.json"));
        var hits = MappingScan.Scan(root);
        Assert.True(hits.Count > 300, $"le scan n'a trouvé que {hits.Count} recherches : motif cassé ?");
        Assert.Contains(hits, h => h.Where.Contains("MoveForge.cs"));   // les forges sont bien couvertes
        var missing = hits.Where(h => !fields.TryGetValue(h.Message, out var f) || !f.Contains(h.Field))
                          .Select(h => $"{h.Where}  {h.Message}.{h.Field}" + (fields.ContainsKey(h.Message) ? "  (champ absent)" : "  (message absent)"))
                          .ToList();
        Assert.True(missing.Count == 0,
            $"{missing.Count} recherche(s) introuvable(s) dans {dir}/fields.json (inertes en prod) :\n" + string.Join("\n", missing));
    }

    // Chaque nom de message comparé ou émis doit être un nom de matching_current.json (la vérité) : plus aucun nom qui ne
    // tiendrait que par le pont de confirmed.json (régression du 04/10 : 9 noms internes, handlers muets avec la vérité seule).
    [Theory]
    [MemberData(nameof(MappingDirs))]
    public void Every_message_name_used_in_the_code_exists_in_the_matching(string dir)
    {
        var root = MappingScan.RepoRoot();
        using var doc = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, dir, "matching_current.json")));
        var names = doc.RootElement.EnumerateObject()
                       .Select(p => p.Value.GetString() ?? "")
                       .Select(v => v.StartsWith("MessageType.", StringComparison.Ordinal) ? v["MessageType.".Length..] : v)
                       .ToHashSet(StringComparer.Ordinal);
        var hits = MappingScan.ScanMessageNames(root);
        Assert.True(hits.Count > 150, $"le scan des noms n'a trouvé que {hits.Count} usages : motif cassé ?");
        var missing = hits.Where(h => !names.Contains(h.Message)).Select(h => $"{h.Where}  {h.Message}").Distinct().ToList();
        Assert.True(missing.Count == 0,
            $"{missing.Count} nom(s) de message absent(s) de {dir}/matching_current.json (handler muet / forge sans code en prod) :\n"
            + string.Join("\n", missing));
    }

    [Theory]
    [MemberData(nameof(MappingDirs))]
    public void Hand_listed_required_fields_exist_in_the_shipped_fields_json(string dir)
    {
        var fields = LoadFields(Path.Combine(MappingScan.RepoRoot(), dir, "fields.json"));
        var missing = RequiredMapping.Extra.Where(p => !fields.TryGetValue(p.Message, out var f) || !f.Contains(p.Field))
                                           .Select(p => $"{p.Message}.{p.Field}").ToList();
        Assert.True(missing.Count == 0, $"RequiredMapping.Extra : absent de {dir}/fields.json : {string.Join(", ", missing)}");
    }

    // Le manifeste embarqué dans l'app (contrôle du bundle streamé au chargement) = exactement les recherches du code.
    [Fact]
    public void RequiredMapping_manifest_matches_the_code()
    {
        var root = MappingScan.RepoRoot();
        var expected = MappingScan.Scan(root).Select(h => (h.Message, h.Field)).Distinct()
                                  .OrderBy(p => p.Message, StringComparer.Ordinal).ThenBy(p => p.Field, StringComparer.Ordinal).ToList();
        var actual = RequiredMapping.Scanned.ToList();
        if (expected.SequenceEqual(actual)) return;

        var path = Path.Combine(root, "GivreClone.Protocol", "RequiredMapping.cs");
        var content = Render(expected);
        if (Environment.GetEnvironmentVariable("GIVRE_UPDATE_MANIFEST") == "1") { File.WriteAllText(path, content); return; }
        var added = expected.Except(actual).Select(p => $"+ {p.Message}.{p.Field}");
        var removed = actual.Except(expected).Select(p => $"- {p.Message}.{p.Field}");
        Assert.Fail("RequiredMapping.cs n'est plus à jour. Régénérer : GIVRE_UPDATE_MANIFEST=1 dotnet test --filter RequiredMapping\n"
                    + string.Join("\n", added.Concat(removed)));
    }

    [Theory]
    [MemberData(nameof(MappingDirs))]
    public void Confirmed_json_has_no_section_that_production_ignores(string dir)
    {
        var path = Path.Combine(MappingScan.RepoRoot(), dir, "confirmed.json");
        // ProtocolCatalog.LoadFromDirectory accepte explicitement l'absence de confirmed.json.
        // Ne pas fabriquer d'override : les deux sources requises sont matching_current et fields.
        if (!File.Exists(path)) return;
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        foreach (var p in doc.RootElement.EnumerateObject())
        {
            Assert.False(p.Name.StartsWith("_"),
                $"{dir}/confirmed.json contient « {p.Name} » : la prod ne l'applique pas. Lire le nom tel qu'il est dans fields.json.");
            Assert.Equal(JsonValueKind.String, p.Value.ValueKind);
        }
    }

    // matching_current.json fait foi : confirmed.json ne peut que nommer un code ABSENT du matching, jamais en renommer un.
    [Theory]
    [MemberData(nameof(MappingDirs))]
    public void Confirmed_json_never_renames_a_code_of_the_matching(string dir)
    {
        var root = MappingScan.RepoRoot();
        using var m = JsonDocument.Parse(File.ReadAllText(Path.Combine(root, dir, "matching_current.json")));
        var codes = m.RootElement.EnumerateObject().Select(p => p.Name.Split('/').Last()).ToHashSet(StringComparer.Ordinal);
        var path = Path.Combine(root, dir, "confirmed.json");
        if (!File.Exists(path)) return;   // pont optionnel non livré, aucune entrée à renommer
        using var c = JsonDocument.Parse(File.ReadAllText(path));
        var renamed = c.RootElement.EnumerateObject().Where(p => codes.Contains(p.Name)).Select(p => p.Name).ToList();
        Assert.True(renamed.Count == 0, $"{dir}/confirmed.json renomme des codes du matching : {string.Join(", ", renamed)}");
    }

    [Theory]
    [InlineData("fields.json")]
    [InlineData("matching_current.json")]
    [InlineData("confirmed.json")]
    public void Data_and_server_mapping_copies_are_identical(string file)
    {
        var root = MappingScan.RepoRoot();
        var server = Path.Combine(root, "server-mapping", file);
        var local = Path.Combine(root, "data", file);
        Assert.Equal(File.Exists(server), File.Exists(local)); // absent dans les deux = extension facultative cohérente
        if (!File.Exists(server))
        {
            Assert.Equal("confirmed.json", file); // fields/matching sont obligatoires et ne peuvent manquer
            return;
        }
        Assert.Equal(File.ReadAllText(server).ReplaceLineEndings(), File.ReadAllText(local).ReplaceLineEndings());
    }

    // Sur le poste de dev, la référence extraite est à côté du dépôt : les copies doivent lui être identiques.
    [Theory]
    [InlineData("fields.json")]
    [InlineData("matching_current.json")]
    public void Shipped_copies_match_the_extracted_reference_when_present(string file)
    {
        var root = MappingScan.RepoRoot();
        var outDir = Path.Combine(root, "..", "GivreProxy", "pipelines", "mapping-extract", "out");
        if (!Directory.Exists(outDir)) return;   // hors poste de dev (CI) : rien à comparer
        var reference = Directory.GetDirectories(outDir)
            .Select(d => Path.Combine(d, "givre"))
            .Where(d => File.Exists(Path.Combine(d, "fields.json")))
            .OrderByDescending(d => File.GetLastWriteTimeUtc(Path.Combine(d, "fields.json")))
            .FirstOrDefault();
        if (reference is null) return;
        Assert.Equal(File.ReadAllText(Path.Combine(reference, file)).ReplaceLineEndings(),
                     File.ReadAllText(Path.Combine(root, "data", file)).ReplaceLineEndings());
    }

    private static string Render(IEnumerable<(string Message, string Field)> pairs)
    {
        var sb = new StringBuilder();
        sb.AppendLine("// <auto-generated>");
        sb.AppendLine("// Généré par GivreClone.Tests (MappingContractTests.RequiredMapping_manifest_matches_the_code) : NE PAS ÉDITER À LA MAIN.");
        sb.AppendLine("// Régénérer : GIVRE_UPDATE_MANIFEST=1 dotnet test --filter RequiredMapping");
        sb.AppendLine("// </auto-generated>");
        sb.AppendLine("namespace GivreClone.Protocol;");
        sb.AppendLine();
        sb.AppendLine("public static partial class RequiredMapping");
        sb.AppendLine("{");
        sb.AppendLine("    /// <summary>Toutes les recherches (message, champ) écrites en clair dans le code, lectures et forges.</summary>");
        sb.AppendLine("    public static readonly (string Message, string Field)[] Scanned =");
        sb.AppendLine("    {");
        foreach (var (m, f) in pairs) sb.AppendLine($"        (\"{m}\", \"{f}\"),");
        sb.AppendLine("    };");
        sb.AppendLine("}");
        return sb.ToString();
    }

    private static Dictionary<string, HashSet<string>> LoadFields(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var map = new Dictionary<string, HashSet<string>>(StringComparer.Ordinal);
        foreach (var msg in doc.RootElement.EnumerateObject())
            map[msg.Name] = msg.Value.EnumerateObject().Select(f => f.Name).ToHashSet(StringComparer.Ordinal);
        return map;
    }
}
