using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text.Json;

namespace GivreClone.App.Services.Supervisor;

/// <summary>Un superviseur enregistré : nom, script et option « Démarrer avec GivreClone ».</summary>
public sealed class SupervisorDef
{
    public string Id { get; set; } = Guid.NewGuid().ToString("N");
    public string Name { get; set; } = "";
    public string Code { get; set; } = "";
    public bool AutoStart { get; set; }
}

/// <summary>Fichier persisté (supervisors.json) : la liste des superviseurs, dans l'ordre d'affichage.</summary>
public sealed class SupervisorsFile
{
    public List<SupervisorDef> Supervisors { get; set; } = new();
}

/// <summary>Magasin des superviseurs, sur le modèle des autres magasins : lecture tolérante (fichier illisible mis de côté), écriture atomique.</summary>
public static class SupervisorStore
{
    private static readonly string FilePath = Path.Combine(MZP19LocalClone.DataRoot, "supervisors.json");
    private static SupervisorsFile _data = new();
    private static bool _writeAside;

    public static IReadOnlyList<SupervisorDef> All => _data.Supervisors;

    public static void Load()
    {
        var r = AtomicJson.LoadStore(FilePath,
            json => (SupervisorsFile?)JsonSerializer.Deserialize(json, typeof(SupervisorsFile), AppJsonContext.Default),
            () => new SupervisorsFile(), Logger.Warn);
        _data = r.Data;
        _writeAside = r.WriteAside;
        _data.Supervisors.RemoveAll(s => string.IsNullOrWhiteSpace(s.Id));
    }

    public static SupervisorDef Add(string name, string code)
    {
        var d = new SupervisorDef { Name = Unique(name), Code = code };
        _data.Supervisors.Add(d);
        Save();
        return d;
    }

    public static void Remove(string id)
    {
        if (_data.Supervisors.RemoveAll(s => s.Id == id) > 0) Save();
    }

    public static void Save()
    {
        try
        {
            AtomicJson.Write(AtomicJson.StorePath(FilePath, _writeAside),
                             JsonSerializer.Serialize(_data, typeof(SupervisorsFile), AppJsonContext.Default), keepBackup: true);
        }
        catch { /* best-effort */ }
    }

    /// <summary>Nom libre dans la liste courante (voir <see cref="SupervisorNaming"/>).</summary>
    public static string Unique(string? wanted) => SupervisorNaming.Unique(wanted, _data.Supervisors.Select(s => s.Name));
}
