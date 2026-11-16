using System.Collections.Generic;
using System.Text.Json.Serialization;
using GivreClone.Engine.Accounts;
using GivreClone.Engine.Proxies;

namespace GivreClone.Engine;

// Contexte STJ source-generated (NativeAOT) : (dé)sérialisation des stores de l'Engine sans réflexion runtime.
[JsonSourceGenerationOptions(WriteIndented = true)]
[JsonSerializable(typeof(List<Account>), TypeInfoPropertyName = "AccountList")]
[JsonSerializable(typeof(List<ProxyEntry>), TypeInfoPropertyName = "ProxyList")]
internal sealed partial class EngineJsonContext : JsonSerializerContext { }
