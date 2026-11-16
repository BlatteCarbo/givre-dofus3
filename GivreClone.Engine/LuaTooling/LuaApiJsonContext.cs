using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace GivreClone.Engine.LuaTooling;

// Contexte STJ source-generated (NativeAOT, TrimMode=full) : lecture de docs/portal/lua-api-doc.json sans réflexion.
[JsonSourceGenerationOptions(PropertyNamingPolicy = JsonKnownNamingPolicy.CamelCase,
                             ReadCommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true)]
[JsonSerializable(typeof(LuaApiDocRoot))]
internal sealed partial class LuaApiJsonContext : JsonSerializerContext { }

// ---- Schéma de lua-api-doc.json (champs inconnus ignorés) ----

internal sealed class LuaApiDocRoot
{
    public List<LuaApiDocNote>? Intro { get; set; }
    public List<LuaApiDocCategory>? Categories { get; set; }
}

internal sealed class LuaApiDocNote
{
    public string? Type { get; set; }
    public string? Title { get; set; }
    public string? Body { get; set; }
}

internal sealed class LuaApiDocCategory
{
    public string? Id { get; set; }
    public string? Title { get; set; }
    public string? Blurb { get; set; }
    public List<LuaApiDocNote>? Notes { get; set; }
    public string? Code { get; set; }
    public List<LuaApiDocMethod>? Methods { get; set; }
    public List<LuaApiDocGlobal>? Globals { get; set; }
    public List<LuaApiDocField>? Fields { get; set; }
    public List<LuaApiDocStub>? Stubs { get; set; }
}

internal sealed class LuaApiDocMethod
{
    public string? Id { get; set; }
    public List<LuaApiDocSig>? Sig { get; set; }
    public string? Desc { get; set; }
    public string? Example { get; set; }
    public List<string>? Badges { get; set; }
}

internal sealed class LuaApiDocSig
{
    public string? Ns { get; set; }
    public string? Name { get; set; }
    public string? Args { get; set; }
    public string? Returns { get; set; }
    /// <summary>« unavailable » : la liaison existe mais lève une erreur Lua explicite (F07) au lieu de simuler un succès.</summary>
    public string? ImplementationStatus { get; set; }
    public string? Throws { get; set; }
}

internal sealed class LuaApiDocGlobal
{
    public string? Name { get; set; }
    public string? Type { get; set; }
    public string? Def { get; set; }
    public string? Role { get; set; }
}

internal sealed class LuaApiDocField
{
    public string? Name { get; set; }
    public string? Type { get; set; }
    public string? Role { get; set; }
}

internal sealed class LuaApiDocStub
{
    public string? Label { get; set; }
    public List<string>? Items { get; set; }
}
