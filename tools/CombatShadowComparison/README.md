# Comparateur de décisions de combat — harnais hors ligne

Le comparateur est adapté de `GivreActive/tools/JondoCompat/Combat/ShadowDecisionComparer.cs`.
Il compare une décision **proposée** à un lancer **effectivement observé et confirmé**,
pour le même combat, la même génération de tour, le même round, la même révision
de snapshot et la même version de client.

**Entrée d'intégration autonome :** `ShadowDecisionComparer.cs`. Ce fichier ne dépend
d'aucun autre code de Givre, de lib native, de serveur, de transport ou de
géométrie propre à une version. Le harnais compile uniquement les deux sources
de ce dossier, via son propre projet ; aucun projet partagé n'est modifié.

## Exécution

```powershell
dotnet run --project tools/CombatShadowComparison/CombatShadowComparison.Harness.csproj --configuration Release
```

Le processus se termine avec le code 0 quand toutes les assertions passent ;
il n'ouvre aucun client et n'accède à aucune ressource réseau ou à des comptes.

## Contrat

- `ShadowBoard` fournit le build exact, l'identité du tour et le **nombre de cellules
  connu depuis la fixture**. Aucun `MapGeometry3776` ou mapping réseau n'est copié.
- `ShadowAdvice` fournit l'état `Planned` (ou une raison sans plan), le sort, le
  type de cible, la case d'origine, la case visée et un chemin non vide se terminant
  sur l'origine du lancer. Un lancer immobile a un chemin à une seule case.
- `ObservedCombatCast` doit venir d'une observation complète avec `CastConfirmed=true`.
  Une intention envoyée, un ACK incertain ou une observation partielle ne suffit pas.
- `ShadowTarget.Actor(id)` compare la cible acteur ; `ShadowTarget.GroundCell`
  permet les poses sur case vide (bombe, glyphe, portail) et `ShadowTarget.NoActor`
  désigne un lancer sans acteur cible. `Unknown` est rejeté.
- `ShadowComparison.Kind` donne le résultat principal, tandis que
  `Differences` énumère **tous** les écarts typés `Spell/Target/Origin/Aim`.
  Les prédicats `*Agrees` valent `null` quand la comparaison est refusée.

Les refus `InsufficientEvidence`, `VersionMismatch`, `StaleTurn` et `NoAdvice`
ne sont pas comptés comme des erreurs de stratégie. Un accord ne démontre ni
la légalité, ni la qualité du plan, ni une performance en combat.

## Pour intégration ultérieure

Le PRIME peut rattacher **le seul fichier** `ShadowDecisionComparer.cs` aux
replays/observations déjà disponibles dans GivreWork. Construire explicitement
les snapshots depuis les preuves hors ligne ; conserver le type de cible et les
identités sans inférer une réussite d'après une requête locale. Aucun envoi de
paquet ni modification de `CombatTurnPlanner`, `ClassMechanics` ou
`LaunchService` n'est requis.
