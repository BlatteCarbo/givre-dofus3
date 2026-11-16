using System.Collections.Generic;
using System.Globalization;
using System.Linq;
using System.Text;
using Avalonia.Media;
using Avalonia.Media.Imaging;

namespace GivreClone.App.Services;

/// <summary>Un Hôtel de Vente : nom lisible, carte du hôtelier, catégorie.</summary>
public sealed record HdvLocation(string Name, long MapId, string Type)
{
    public override string ToString() => Name;
    /// <summary>Pastille de catégorie (repli si l'icône du jeu n'est pas chargée).</summary>
    public IBrush TypeBrush => Type switch
    {
        "resources"   => Brushes.MediumSeaGreen,
        "consumables" => Brushes.CornflowerBlue,
        "equipment"   => Brushes.IndianRed,
        "cosmetics"   => Brushes.MediumPurple,
        _             => Brushes.Gray,
    };

    /// <summary>Nom de l'asset d'icône du jeu (auctionHouse*) déduit du LIBELLÉ — plus fin que <see cref="Type"/> qui
    /// fusionne créature/rune/âmes. Normalisation d'accents pour matcher « Créature/Rune/Âmes/Cosmétique/… ».</summary>
    public string IconKey
    {
        get
        {
            var n = StripAccents(Name).ToLowerInvariant();
            if (n.Contains("creature")) return "auctionHouseCreatures";
            if (n.Contains("rune"))     return "auctionHouseRunes";
            if (n.Contains("ame"))      return "auctionHouseSoulStones";   // « Âmes »
            if (n.Contains("cosmet"))   return "auctionHouseCosmetics";
            if (n.Contains("consom"))   return "auctionHouseConsumables";
            if (n.Contains("equip"))    return "auctionHouseEquipments";
            return "auctionHouseResources";                               // Ressources + défaut
        }
    }

    /// <summary>Icône DU JEU (Texture2D auctionHouse*), ou null tant que le bundle n'est pas chargé (repli pastille).</summary>
    public Bitmap? Icon => Stores.UiIcons.Get(IconKey);
    public bool HasIcon => Icon is not null;

    private static string StripAccents(string s)
        => new string(s.Normalize(NormalizationForm.FormD)
            .Where(c => CharUnicodeInfo.GetUnicodeCategory(c) != UnicodeCategory.NonSpacingMark).ToArray());
}

/// <summary>
/// Liste EMBARQUÉE des Hôtels de Vente (mapId du hôtelier + catégorie). Dérivée de docs/hdv-locations.json (source
/// Un bot tiers) — embarquée en dur car docs/ n'est PAS distribué en prod, et sert aussi de source au sélecteur de l'UI
/// et (à terme) au voyage automatique goToHdv(). ⚠️ mapId À REVALIDER sur le patch Dofus courant (recoupable avec
/// les hints catégorie 2 « Hôtels de vente »).
/// </summary>
public static class HdvLocations
{
    public static readonly IReadOnlyList<HdvLocation> All = new HdvLocation[]
    {
        new("HDV Ressources Pandala",   206308355, "resources"),
        new("HDV Consommable Pandala",  206308354, "consumables"),
        new("HDV Équipement Pandala",   206307330, "equipment"),
        new("HDV Créature Pandala",     206438914, "equipment"),
        new("HDV Rune Pandala",         206439426, "resources"),

        new("HDV Équipement Astrub",    191106052, "equipment"),
        new("HDV Cosmétiques Astrub",   192417794, "cosmetics"),
        new("HDV Ressources Astrub",    191104004, "resources"),
        new("HDV Consommable Astrub",   191102976, "consumables"),

        new("HDV Ressources Bonta",     212601350, "resources"),
        new("HDV Équipement Bonta",     212600837, "equipment"),
        new("HDV Créature Bonta",       212601345, "equipment"),
        new("HDV Consommable Bonta",    212600839, "consumables"),
        new("HDV Rune Bonta",           212601859, "resources"),
        new("HDV Cosmétique Bonta",     217064452, "cosmetics"),
        new("HDV Âmes Bonta",           220200961, "resources"),

        new("HDV Ressources Brâkmar",   212861952, "resources"),
        new("HDV Équipement Brâkmar",   212860930, "equipment"),
        new("HDV Consommable Brâkmar",  212863491, "consumables"),
        new("HDV Runes Brâkmar",        212861957, "resources"),
        new("HDV Créatures Brâkmar",    212862469, "equipment"),
        new("HDV Cosmétiques Brâkmar",  214697994, "cosmetics"),
        new("HDV Âmes Brâkmar",         219677441, "resources"),

        new("HDV Équipements Éleveur",  73531904,  "equipment"),
        new("HDV Consommables Éleveur", 73400321,  "consumables"),
        new("HDV Ressources Éleveur",   73400322,  "resources"),

        new("HDV Équipement Sufokia",   90702849,  "equipment"),
        new("HDV Runes Sufokia",        90703617,  "resources"),
        new("HDV Consommable Sufokia",  95424516,  "consumables"),
        new("HDV Créature Sufokia",     90704643,  "equipment"),
        new("HDV Ressources Sufokia",   90707714,  "resources"),
        new("HDV Cosmétiques Sufokia",  91750927,  "cosmetics"),

        new("HDV Équipement Otomai",    154644,    "equipment"),
        new("HDV Ressources Otomai",    155155,    "resources"),
        new("HDV Consommable Otomai",   154641,    "consumables"),

        new("HDV Équipement Frigost",   54172968,  "equipment"),
        new("HDV Ressources Frigost",   54172456,  "resources"),
        new("HDV Consommable Frigost",  54173481,  "consumables"),
        new("HDV Créature Frigost",     54170402,  "equipment"),
        new("HDV Runes Frigost",        54530067,  "resources"),
        new("HDV Cosmétiques Frigost",  54658069,  "cosmetics"),
    };
}
