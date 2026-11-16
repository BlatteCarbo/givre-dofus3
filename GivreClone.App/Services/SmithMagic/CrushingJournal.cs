using System;
using System.Collections.Concurrent;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;
using System.Threading.Tasks;

namespace GivreClone.App.Services.SmithMagic;

/// <summary>
/// Enregistrement persistant d'une opération de brisage d'objet.
/// Stocke l'item brisé, son niveau, son coût/prix HDV, le coefficient de brisage obtenu,
/// la liste des runes générées et leur valeur marchande totale avec ROI (Retour sur Investissement).
/// </summary>
public sealed record CrushingJournalEntry(
    string TimestampUtc,
    string CharacterName,
    int ItemGid,
    string ItemName,
    int ItemLevel,
    int QuantityCrushed,
    long EstimatedItemCost,
    double CoefficientPercent,
    IReadOnlyList<CrushedRuneYield> RunesYielded,
    long TotalRuneValueKamas,
    long NetProfitKamas,
    double ReturnOnInvestmentPercent);

public sealed record CrushedRuneYield(
    int RuneGid,
    string RuneName,
    int Quantity,
    long UnitPriceKamas,
    long TotalPriceKamas);

/// <summary>
/// Registre et mémoire d'analyse économique du brisage (Crushing & Recycling Ledger).
/// </summary>
public static class CrushingJournal
{
    private static readonly object _fileGate = new();
    private static readonly string StoragePath = Path.Combine(MZP19LocalClone.DataRoot, "crushing-journal.json");
    private static readonly List<CrushingJournalEntry> _entries = new();

    static CrushingJournal()
    {
        Load();
    }

    public static IReadOnlyList<CrushingJournalEntry> GetAllEntries()
    {
        lock (_fileGate) return _entries.ToList();
    }

    public static void Record(CrushingJournalEntry entry)
    {
        lock (_fileGate)
        {
            _entries.Add(entry);
            Save();
        }
    }

    private static void Load()
    {
        lock (_fileGate)
        {
            try
            {
                if (File.Exists(StoragePath))
                {
                    string json = File.ReadAllText(StoragePath);
                    var list = JsonSerializer.Deserialize<List<CrushingJournalEntry>>(json);
                    if (list is not null)
                    {
                        _entries.Clear();
                        _entries.AddRange(list);
                    }
                }
            }
            catch { }
        }
    }

    private static void Save()
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(StoragePath)!);
            string json = JsonSerializer.Serialize(_entries, new JsonSerializerOptions { WriteIndented = true });
            File.WriteAllText(StoragePath, json);
        }
        catch { }
    }

    /// <summary>
    /// Calcule le coefficient moyen et la rentabilité historique pour un GID donné.
    /// </summary>
    public static (double AvgCoef, double AvgRoi, int Count) GetStatsForItem(int itemGid)
    {
        lock (_fileGate)
        {
            var matches = _entries.Where(e => e.ItemGid == itemGid).ToList();
            if (matches.Count == 0) return (0, 0, 0);
            return (matches.Average(m => m.CoefficientPercent), matches.Average(m => m.ReturnOnInvestmentPercent), matches.Count);
        }
    }
}
