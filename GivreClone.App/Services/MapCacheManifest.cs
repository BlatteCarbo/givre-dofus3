using System.Collections.Generic;
using System;
using System.IO;
using System.Security.Cryptography;

namespace GivreClone.App.Services;

// Manifeste du cache disque des cartes (%APPDATA%\GivreClone\mapcache\manifest.json). C'est l'EMPREINTE des bundles
// source (nom + taille + date + SHA256 du CONTENU) au moment ou le cache a ete construit. Au demarrage on recompare :
// si un patch Dofus a modifie / ajoute / retire un bundle, l'empreinte differe -> le cache est INVALIDE et on reextrait
// TOUT (la correspondance carte->bundle n'est PAS stable d'un patch a l'autre, cf MapCellReader). Serialise via
// AppJsonContext (STJ source-gen, compatible NativeAOT).
public sealed class MapCacheManifest
{
    public int Version { get; set; }
    public string Root { get; set; } = "";        // repertoire des bundles resolu (Dofus-dofus3 vs Dofus-experimental)
    public int MapCount { get; set; }              // nb de cartes reellement extraites
    public bool Complete { get; set; }              // jamais vrai si un bundle ou une carte a echoue
    public List<MapCacheBundle> Bundles { get; set; } = new();

    public bool Covers(int indexedMaps, int validDiskMaps) =>
        Complete && indexedMaps > 0 && MapCount == indexedMaps && validDiskMaps == indexedMaps;
}

public sealed class MapCacheBundle
{
    public string Name { get; set; } = "";         // nom de fichier du bundle
    public long Length { get; set; }               // taille en octets
    public long MTimeUtcTicks { get; set; }        // date de derniere ecriture (UTC, en ticks)
    public string Sha256 { get; set; } = "";       // empeche faux cache chaud si octets modifies sans toucher taille/date

    public static MapCacheBundle Inspect(string path)
    {
        var file = new FileInfo(path);
        using var source = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.Read,
            65536, FileOptions.SequentialScan);
        return new MapCacheBundle
        {
            Name = file.Name, Length = source.Length, MTimeUtcTicks = file.LastWriteTimeUtc.Ticks,
            Sha256 = Convert.ToHexString(SHA256.HashData(source))
        };
    }
}
