using System.Collections.Generic;
using System.Text.Json.Serialization;

namespace GivreClone.App.Services;

// Contexte STJ source-generated : (dé)sérialisation compatible NativeAOT, sans réflexion runtime.
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(AppConfigData))]
[JsonSerializable(typeof(CombatProfilesFile))]
[JsonSerializable(typeof(PlanningFile))]
[JsonSerializable(typeof(TeamsFile))]
[JsonSerializable(typeof(BotSettingsFile))]
[JsonSerializable(typeof(HdvProfilesFile))]
[JsonSerializable(typeof(GivreClone.App.Services.Supervisor.SupervisorsFile))]
[JsonSerializable(typeof(NotifFile))]
[JsonSerializable(typeof(MapCacheManifest))]
[JsonSerializable(typeof(Dictionary<string, AdapterSettingsStore.Entry>), TypeInfoPropertyName = "AdapterMap")]
[JsonSerializable(typeof(Dictionary<string, string>), TypeInfoPropertyName = "StringMap")]   // LuaMemoryStore (memory:/sharedMemory:)
internal sealed partial class AppJsonContext : JsonSerializerContext { }
