using System;
using System.Collections.Generic;
using System.IO;
using System.Text.Json;

namespace GivreClone.App.Services.Combat;

/// <summary>Threat of one monster grade, compiled from the Dofensive bestiary (partner data): its own AP/MP,
/// its longest spell reach and the most damage its spells can deal in one turn within its AP and per-turn caps.
/// Damage includes the monster's elemental characteristic, but not the target's resistances, so it stays conservative.</summary>
public sealed record MonsterThreat(int Ap, int Mp, int Reach, int TurnDamage)
{
    public bool AttackKnown => Reach > 0 && TurnDamage > 0;
}

public sealed class MonsterThreatCatalog
{
    private readonly Dictionary<(int Monster, int Grade), MonsterThreat> _threats;
    private MonsterThreatCatalog(Dictionary<(int, int), MonsterThreat> threats) => _threats = threats;

    public static MonsterThreatCatalog Empty { get; } = new(new());
    public int Count => _threats.Count;

    public static string DefaultPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
        "GivreReplica", "dofensive", "monster-threat.json");

    public MonsterThreat? Find(int monster, int grade)
    {
        if (_threats.TryGetValue((monster, grade), out var exact)) return exact;
        MonsterThreat? below = null, any = null; int belowGrade = int.MinValue, anyGrade = int.MaxValue;
        foreach (var ((m, g), t) in _threats)
        {
            if (m != monster) continue;
            if (g <= grade && g > belowGrade) { below = t; belowGrade = g; }
            if (g < anyGrade) { any = t; anyGrade = g; }
        }
        return below ?? any;
    }

    /// <summary>Format: { "monsterId": { "grade": [ap, mp, reach, turnDamage] } }. A missing or unreadable file yields
    /// <see cref="Empty"/>; the planner then keeps its observed or conservative estimate.</summary>
    public static MonsterThreatCatalog Load(string path)
    {
        if (!File.Exists(path)) return Empty;
        try { return Parse(File.ReadAllText(path)); }
        catch (Exception e) when (e is IOException or JsonException or UnauthorizedAccessException or FormatException)
        {
            GivreLauncher.Core.Transport.TransportLog.Line($"[bestiaire] {path} illisible : {e.Message}");
            return Empty;
        }
    }

    public static MonsterThreatCatalog Parse(string json)
    {
        var threats = new Dictionary<(int, int), MonsterThreat>();
        using var doc = JsonDocument.Parse(json);
        foreach (var monster in doc.RootElement.EnumerateObject())
        {
            int id = int.Parse(monster.Name, System.Globalization.CultureInfo.InvariantCulture);
            foreach (var grade in monster.Value.EnumerateObject())
            {
                var v = grade.Value;
                if (v.GetArrayLength() < 4) continue;
                threats[(id, int.Parse(grade.Name, System.Globalization.CultureInfo.InvariantCulture))] =
                    new(v[0].GetInt32(), v[1].GetInt32(), v[2].GetInt32(), v[3].GetInt32());
            }
        }
        return new(threats);
    }
}
