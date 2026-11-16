# MZP14 — sélection de personnage typée, historique GivreClone.Engine

## RESULT

Nouvelle verticale dans la vraie source GivreClone.Engine : une sélection de personnage reçue par l'ingress MZP1 déjà admis peut renseigner CharacterId et CharacterName. Un changement d'identité purge immédiatement carte, cellule et combat de l'ancien personnage. Sans autorité indépendante, le legacy GameState.Apply demeure inerte. Ce n'est pas une preuve de Dofus en jeu ni un clone v1.3.1 identique.

## CHANGES

- GivreClone.Engine/MZP14VerifiedCharacterReader.cs : exige CharacterSelectionEvent, direction inbound et code du catalogue ; champs success, Character.id, character_basic_information et CharacterBasicInformation.name réellement fournis par fields.json. Parcours imbriqué borné, unicité, ID strict, nom UTF-8 strict, refus ambiguïtés et contenu invalide ; aucun numéro fixé dans le code.
- GameState.ApplyGuardedInbound : sélection admise, CharacterKnown explicite, invalidation de l'ancien état carte/combat lors d'une autre identité ; ClearVerifiedSession efface l'identité.
- tools/MZP14CharacterHarness : 17 tests négatifs/positifs contre les véritables DLL Core/Protocol/Engine. Autorité TEST uniquement simulée, aucun Dofus ou compte.
- Harness MZP1/MZP2 source-linked synchronisés avec le lecteur de production.

## VALIDATION

- MZP14CharacterHarness : 17 PASS.
- MZP13StateHarness : 19 PASS lors du précédent lot ; à rejouer si nécessaire.
- MZP1GuardedWiringHarness xUnit : 30 PASS.
- MZP2HostAdmissionHarness xUnit : 41 PASS.
- Build Windows Release et Debug : résultats dans les outils du présent lot, aucune authenticité game revendiquée.

## BLOCKERS

La version exacte Cytrus est 3.7.11.11 mais aucun corpus inbound authentifié ni preuve indépendante double décodeur/ACK n'est disponible ; l'ID personnage et la map dans les tests sont fictifs. Aucun original GivreClone v1.3.1 détaché certifié n'a été trouvé pour une décompilation donnant le code exact. Les trois sessions de l'original ne doivent pas être lues, arrêtées ou modifiées. Les fonctionnalités gameplay restent non vérifiées.
