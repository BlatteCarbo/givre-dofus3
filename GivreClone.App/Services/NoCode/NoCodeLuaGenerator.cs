using System;
using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;

namespace GivreClone.App.Services.NoCode;

/// <summary>Entrées du constructeur visuel. Aucun état de jeu ni dépendance réseau.</summary>
public sealed record NoCodeRouteOptions(
    string Maps, bool Gather, bool Fight, string GatherElementIds,
    string MaxPods, string MinMonsters, string MaxMonsters);

public sealed record NoCodeGenerationResult(bool IsValid, string Lua, string Message)
{
    public static NoCodeGenerationResult Error(string message) => new(false, "", message);
}

/// <summary>Génère exclusivement le DSL déclaratif compris par GivreLuaHost.Route.</summary>
public static class NoCodeLuaGenerator
{
    private static readonly HashSet<string> Directions = new(StringComparer.Ordinal)
        { "top", "bottom", "left", "right" };

    public static NoCodeGenerationResult Generate(NoCodeRouteOptions input)
    {
        if (input is null) return NoCodeGenerationResult.Error("Configuration manquante.");
        if (input.Maps is null || input.Maps.Length > 4096)
            return NoCodeGenerationResult.Error("Trajet trop long (4 096 caractères maximum).");

        var lines = input.Maps.Split('\n', StringSplitOptions.TrimEntries | StringSplitOptions.RemoveEmptyEntries);
        if (lines.Length is < 1 or > 24)
            return NoCodeGenerationResult.Error("Indiquer de 1 à 24 cartes, une par ligne.");

        var steps = new List<(string Map, string Direction, (int X, int Y)? Coords)>();
        var seen = new HashSet<string>(StringComparer.Ordinal);
        foreach (var line in lines)
        {
            if (line.Length > 75 || line.Count(c => c == ':') != 1)
                return NoCodeGenerationResult.Error("Format attendu : x,y:direction ou mapId:direction.");
            var parts = line.Split(':', 2, StringSplitOptions.TrimEntries);
            string direction = parts[1].ToLowerInvariant();
            if (!Directions.Contains(direction))
                return NoCodeGenerationResult.Error("Direction inconnue : top, bottom, left ou right.");

            string map;
            (int X, int Y)? coords = null;
            if (parts[0].Contains(','))
            {
                var xy = parts[0].Split(',', StringSplitOptions.TrimEntries);
                if (xy.Length != 2 || !TryBoundedInt(xy[0], -1000, 1000, out int x)
                    || !TryBoundedInt(xy[1], -1000, 1000, out int y))
                    return NoCodeGenerationResult.Error("Coordonnées invalides : utiliser des entiers x,y entre -1000 et 1000.");
                map = x.ToString(CultureInfo.InvariantCulture) + "," + y.ToString(CultureInfo.InvariantCulture);
                coords = (x, y);
            }
            else
            {
                if (!TryBoundedInt(parts[0], 1, int.MaxValue, out int id))
                    return NoCodeGenerationResult.Error("Identifiant de carte invalide (entier positif requis).");
                map = id.ToString(CultureInfo.InvariantCulture);
            }

            if (!seen.Add(map)) return NoCodeGenerationResult.Error("Carte répétée : une seule étape par carte dans ce mode.");
            steps.Add((map, direction, coords));
        }

        // Pour les coordonnées explicites, interdire des sauts ou sorties contradictoires.
        // Le dernier pas et les ids numériques exigent toujours une vérification sur le graphe réel.
        for (int i = 0; i < steps.Count - 1; i++)
        {
            var first = steps[i];
            var next = steps[i + 1];
            if (first.Coords is not { } a || next.Coords is not { } b) continue;
            var expected = first.Direction switch
            {
                "top" => (a.X, a.Y - 1),
                "bottom" => (a.X, a.Y + 1),
                "left" => (a.X - 1, a.Y),
                _ => (a.X + 1, a.Y),
            };
            if (expected != b)
                return NoCodeGenerationResult.Error($"Étapes {i + 1} et {i + 2} : sortie {first.Direction} incohérente avec la carte suivante.");
        }

        if (!TryBoundedInt(input.MaxPods, 1, 100, out int pods))
            return NoCodeGenerationResult.Error("MAX_PODS doit être entre 1 et 100.");

        string ids = "";
        if (input.Gather)
        {
            if (string.IsNullOrWhiteSpace(input.GatherElementIds) || input.GatherElementIds.Length > 350)
                return NoCodeGenerationResult.Error("Renseigner les ElementTypeId à récolter (1 à 20 identifiants).");
            var elements = input.GatherElementIds.Split(',', StringSplitOptions.TrimEntries);
            if (elements.Length is < 1 or > 20) return NoCodeGenerationResult.Error("1 à 20 identifiants de récolte sont autorisés.");
            var values = new HashSet<int>();
            foreach (string raw in elements)
            {
                if (!TryBoundedInt(raw, 1, 1_000_000, out int id) || !values.Add(id))
                    return NoCodeGenerationResult.Error("Identifiants de récolte invalides ou répétés (entiers positifs).");
            }
            ids = string.Join(", ", values.OrderBy(v => v));
        }

        int min = 0, max = 0;
        if (input.Fight && (!TryBoundedInt(input.MinMonsters, 1, 8, out min)
                            || !TryBoundedInt(input.MaxMonsters, 1, 8, out max) || min > max))
            return NoCodeGenerationResult.Error("Nombre de monstres : min/max entre 1 et 8, avec min ≤ max.");

        var lua = new StringBuilder();
        lua.AppendLine("-- Généré par l'assistant sans code. Vérifier l'existence des cartes,");
        lua.AppendLine("-- les sorties et les ElementTypeId dans les données de jeu avant exécution.");
        lua.AppendLine("-- Prévisualisation seule : aucune action en jeu n'a été lancée.");
        lua.AppendLine("MAX_PODS = " + pods.ToString(CultureInfo.InvariantCulture));
        if (input.Gather) lua.AppendLine("ELEMENTS_TO_GATHER = { " + ids + " }");
        if (input.Fight)
        {
            lua.AppendLine("MIN_MONSTERS = " + min.ToString(CultureInfo.InvariantCulture));
            lua.AppendLine("MAX_MONSTERS = " + max.ToString(CultureInfo.InvariantCulture));
        }
        lua.AppendLine();
        lua.AppendLine("function move()");
        lua.AppendLine("  return {");
        foreach (var step in steps)
            lua.AppendLine("    { map = " + (step.Coords.HasValue ? "\"" + step.Map + "\"" : step.Map)
                + ", path = \"" + step.Direction + "\", gather = " + Bool(input.Gather)
                + ", fight = " + Bool(input.Fight) + " },");
        lua.AppendLine("  }");
        lua.AppendLine("end");
        return new NoCodeGenerationResult(true, lua.ToString(),
            $"{steps.Count} carte(s) validée(s) sur leur format et les transitions explicites. Topologie et ressources réelles à vérifier.");
    }

    private static string Bool(bool value) => value ? "true" : "false";

    private static bool TryBoundedInt(string? text, int min, int max, out int value)
    {
        value = 0;
        return text is { Length: > 0 and < 16 }
               && int.TryParse(text, NumberStyles.AllowLeadingSign, CultureInfo.InvariantCulture, out value)
               && value >= min && value <= max;
    }
}
