using System;
using System.IO;
using System.Runtime.CompilerServices;
using GivreClone.Protocol;

namespace GivreClone.Tests;

/// <summary>
/// Les forges ne devinent plus RIEN : code 3 lettres et numéros de champ viennent tous du mapping. On branche
/// donc le VRAI catalogue (data/ copié à la sortie du projet de test) une fois pour tout l'assembly, AVANT tout
/// test. Conséquence : les tests d'émission valident que le mapping livré est correct, au lieu de comparer à des
/// littéraux figés dans la forge (qui n'existent plus).
/// </summary>
internal static class TestSetup
{
    [ModuleInitializer]
    internal static void Init()
    {
        // Static app stores must never share userdata with another testhost or the app.
        Environment.SetEnvironmentVariable("GIVRE_CLONE_RUN_NAME",
            $"givre-tests-{Environment.ProcessId}-{Guid.NewGuid():N}");

        var dataDir = Path.Combine(AppContext.BaseDirectory, "data");
        if (!Directory.Exists(dataDir)) return;   // certains projets de test n'embarquent pas data/ : forges inertes, tests dédiés le gèrent
        var cat = ProtocolCatalog.LoadFromDirectory(dataDir);
        MoveForge.UseCatalog(cat.CodeForName, cat.FieldNumber);
        EnvelopeBuilder.UseCatalog(cat.FieldNumber);   // slots d'enveloppe (GameMessage/Request/Event/Response) résolus du mapping
        // Readers routés par le catalogue : on les branche AUSSI dans les tests -> les rejeux de trames valident le
        // chemin CATALOGUE tel que la prod le voit (fields.json + pont code->nom de confirmed.json, SANS section _aliases ni
        // _fields : MappingContractTests le garantit). Un champ absent du bundle fait échouer le test de décodage.
        FightStatsReader.UseCatalog(cat.FieldNumber);
        MapFightReader.UseCatalog(cat.FieldNumber);
        FightPrepReader.UseCatalog(cat.FieldNumber);
        FightResultReader.UseCatalog(cat.FieldNumber);   // Lot C2 : bilan de combat (FightEndEvent.results imbriqués)
        MapInteractivesReader.UseCatalog(cat.FieldNumber);
        TeleportReader.UseCatalog(cat.FieldNumber);
        LotEEventsReader.UseCatalog(cat.FieldNumber);   // Lot E : demandes reçues, options de combat, respawn, variantes, popup
        // Lecteurs routés vers le catalogue (migration "son mapping = le nôtre") : rejeux de trames validés sur le
        // chemin CATALOGUE (fields.json v4), pas seulement le repli littéral.
        InventoryReader.UseCatalog(cat.FieldNumber);
        CharacterInfoReader.UseCatalog(cat.FieldNumber);
        CharacterSpellsReader.UseCatalog(cat.FieldNumber);
        MapActorsReader.UseCatalog(cat.FieldNumber);
        PartyReader.UseCatalog(cat.FieldNumber);   // Lot G : événements de groupe (membres Character, party_id)
        QuestReader.UseCatalog(cat.FieldNumber);   // Lot I : quêtes (QuestActive/Details/QuestObjective/Completion par nom)
        LotMReader.UseCatalog(cat.FieldNumber);   // Lot M
        HouseReader.UseCatalog(cat.FieldNumber);   // Lot J : maisons de la carte (House/HouseOnMap/HouseInstance/AccountTag par nom)
    }
}
