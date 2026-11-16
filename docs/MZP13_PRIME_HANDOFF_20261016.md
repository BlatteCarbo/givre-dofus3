# MZP13 — premier vrai état carte du moteur historique

## RESULT

Le projet historique GivreClone.App et le vrai moteur GivreClone.Engine ont été modifiés pour traiter un événement de carte authentiquement *admis par le contrôleur MZP1*. L'état initial n'invente plus une map : MapKnown reste false avant admission, puis MapId est rempli uniquement à partir d'un champ wire connu de fields.json. Un changement de carte annule CellId ; une révocation efface la totalité de l'état courant.

**Cela ne constitue ni le même code source que GivreClone v1.3.1, ni un clone jouable parfaitement vérifié.** L'index MZP1_BINARY_FINDINGS démontre qu'aucune copie détachée de GivreClone.exe v1.3.1 n'a été retrouvée à ce stade. Les nouveaux fournisseurs GivreCore et GivreLauncher.Core sont des reconstructions, non des fichiers décompilés originaux. Aucune validation du jeu Dofus 3.7.11.11 sur une session TEST authentifiée.

## CHANGES

- GivreClone.Engine/MZP13VerifiedMapReader.cs : lecteur source de production, accepte seulement MapCurrentEvent et MapComplementaryInformationEvent avec nom/code strictement mappés, sens ServerToClient et numéro map_id résolu par le vrai ProtocolCatalog.FieldNumber. Aucun tag beta codé en dur, aucune heuristique sur le texte ou le contenu.
- GivreClone.Engine/GameState.cs : Apply() unverified reste volontairement inerte. ApplyGuardedInbound() est internal et n'est invoquée que depuis MZP1GuardedEngineWiring après admission ; support MapKnown/FightKnown et invalide la cellule lorsqu'une nouvelle carte arrive ; ClearVerifiedSession() réinitialise personnage, cellule, map et combat.
- GivreClone.Engine/Recovery/MZP1GuardedEngineWiring.cs : branche la vraie projection après admission et purge l'état au moment de RevokeUnsafe.
- GivreClone.Engine/MZP13TestVisibility.cs : accès aux membres internal réservé au seul harness offline nommé.
- tools/MZP13StateHarness/ : vraies DLL de production, tests stricts (mauvais sens, nom inventé, champ non confirmé, ID zéro/overflow, doublon, changement de carte, révocation).
- tools/MZP1GuardedWiringHarness/MZP13GuardedStateTests.cs : intégration réelle de MZP1 (autorités de test indépendantes simulées OFFLINE) avec entrée PID attribuée, refus de PID erroné, transition carte, et purge si l'autorité est révoquée.
- Harness MZP1 et MZP2 historiquement source-linked mis à jour pour compiler aussi le nouveau lecteur de production.

## VALIDATION

- MZP13StateHarness : 19 assertions offline PASS.
- MZP1GuardedWiringHarness : 30 tests xUnit PASS (29 précédents + nouveau test intégré).
- MZP2HostAdmissionHarness : 41 tests xUnit PASS.
- GivreClone.App Windows x64 Release : **0 erreur, 143 avertissements** historiques lors du rebuild.
- GivreClone.App Windows x64 Debug : **0 erreur, 144 avertissements** (dont le warning explicite data/ absent).
- MZP13Release/package.py : sortie historique Windows x64 + source delta, liste blanche, manifest SHA par contenu et ZIP CRC. Vérification effectuée deux fois avec comparaisons SHA-256 byte-identiques (**BINARY_REPRODUCIBLE=True**, **SOURCE_REPRODUCIBLE=True**). Archives sous artifacts/MZP13-*.zip.

## BLOCKERS

Il manque toujours une copie v1.3.1 détachée dont SHA et correspondance ont été vérifiées ; aucune rétro-ingénierie complète de l'original ne peut donc être certifiée. Surtout, aucun propriétaire de session TEST n'a démontré sa provenance réseau, aucun décodage inbound serveur exact 3.7.11.11 n'a été comparé à deux vrais décodeurs, aucun ACK ou personnage/map réel n'a été observé. La carte n'est pliée que sur une admission MZP1 explicitement fournie.

La parité 100 % implique encore comptes, lancement officiel, MITM/Socket, carte et combat, inventaire, banque, HDV, Lua, planning, UI, supervision et H24. Ne pas toucher à l'original GivreClone actif et ses trois bots, ni utiliser le mot de passe transmis dans le chat.
