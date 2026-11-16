using System.Diagnostics;
using System.Text;
using GivreClone.Engine.LuaTooling;
using Xunit;
using Xunit.Abstractions;

namespace GivreClone.Tests.LuaTooling;

[CollectionDefinition("Lua editor performance", DisableParallelization = true)]
public sealed class LuaEditorPerformanceCollection { }

[Collection("Lua editor performance")]
public class LuaToolingPerfTests
{
    private readonly ITestOutputHelper _out;
    public LuaToolingPerfTests(ITestOutputHelper output) => _out = output;

    // Script réaliste de ~10 000 lignes : fonctions, blocs imbriqués, tables, appels d'API, chaînes et commentaires.
    internal static string BigScript(int targetLines = 10_000)
    {
        var sb = new StringBuilder();
        sb.Append("MAX_PODS = 90\nELEMENTS_TO_GATHER = { 38, 289 }\n--[[ en-tête\n  sur plusieurs lignes\n]]\n");
        int lines = 5, n = 0;
        while (lines < targetLines)
        {
            n++;
            sb.Append($"local function helper{n}(a, b, ...)\n");
            sb.Append($"    local t = {{\n        name = \"étape {n}\", -- commentaire\n        cells = {{ 1, 2, 3 }},\n        f = function(x) return x * 2 end,\n    }}\n");
            sb.Append("    for i = 1, #t.cells do\n        if t.cells[i] ~= a and getPods() < MAX_PODS then\n            printSuccess(string.format(\"%d/%d\", i, getPodsMax()))\n");
            sb.Append("        elseif map:getId() == 0 then\n            local s = [[\n  texte long\n]] .. tostring(b)\n        else\n            repeat b = (b or 0) + 1 until b > 3\n        end\n    end\n");
            sb.Append("    while inventory:pods() > 100 do\n        if not gatherAll() then break end\n    end\n    return t.f(a)\nend\n\n");
            lines += 23;   // lignes ajoutées par itération
        }
        sb.Append("function move()\n    return { { map = \"-15,39\", path = \"right\", gather = true } }\nend\n");
        return sb.ToString();
    }

    [Fact]
    public void Lint_fold_and_format_of_10k_lines_under_300_ms()
    {
        var cat = LuaToolingTestData.Catalog;
        // Échauffement JIT sur un autre texte (l'éditeur tourne en régime établi).
        string warm = BigScript(300);
        LuaLinter.Lint(warm, cat); LuaFolding.Compute(warm); LuaFormatter.Format(warm);

        string big = BigScript();
        Assert.True(big.Split('\n').Length >= 10_000);
        var sw = Stopwatch.StartNew();
        var diags = LuaLinter.Lint(big, cat);
        long lint = sw.ElapsedMilliseconds;
        var folds = LuaFolding.Compute(big);
        long fold = sw.ElapsedMilliseconds - lint;
        var formatted = LuaFormatter.Format(big);
        sw.Stop();
        _out.WriteLine($"lint {lint} ms, fold {fold} ms, format {sw.ElapsedMilliseconds - lint - fold} ms, total {sw.ElapsedMilliseconds} ms, {diags.Count} diag, {folds.Count} replis");

        Assert.Empty(diags);                   // script valide et correct : aucun diagnostic
        Assert.True(folds.Count > 1000);
        Assert.Equal(big, formatted);          // déjà formaté : inchangé
        // La mesure du temps mur sur un PC partagé est non déterministe.
        // Les invariants restent bloquants en CI; le budget 300ms est évalué
        // séparément avec GIVRE_LUA_PERF_STRICT=1 (Release, machine libre).
#if !DEBUG
        if (string.Equals(Environment.GetEnvironmentVariable("GIVRE_LUA_PERF_STRICT"), "1", StringComparison.Ordinal))
            Assert.True(sw.ElapsedMilliseconds < 300, $"lint+fold+format Release = {sw.ElapsedMilliseconds} ms");
#endif
    }
}
