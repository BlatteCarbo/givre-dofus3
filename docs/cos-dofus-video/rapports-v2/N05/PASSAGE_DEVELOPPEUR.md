# N05 — Passage développeur : séparer les transitions prouvées des mécaniques Unknown

Les résultats audités se trouvent dans [RAPPORT.md](RAPPORT.md) et la matrice [PREUVES.md](PREUVES.md). **Ne pas écrire de nouveau décodeur depuis ce rapport sans recouper le client et les traces.** Travail documentaire seulement. Version cible vérifiée par hash du client : metadata `ad2880162d7e0aee219fdc81e4a3817cc86cca4aba3df3ce86fd470c1bb9a6af`, GameAssembly `ad013415455d93bd865aef70fcf1b7c6f704c799f85e2ef301aa7fa610ef2591` ; détail `METADATA-3.7.13.17.md:8-17`.

## Trois priorités ordonnées

**1. Préserver les trois transitions de marque déjà distinctes.** `MapFightReader.cs:96-125`, `FightMarks.cs:14-34` et `LaunchService.Packets.cs:1092-1104` prouvent qu'une pose, une activation et un retrait disposent de branches séparées. `data/fields.json:3348-3386` attribue **9** (`mark_cells`), **24** (`activate_glyph_trap`) et **30** (`unmark_cells`) ; le tag **21** est `trigger_glyph_trap` **distinct**. Ne pas convertir `Active=false` ou un `trigger` en `unmark`. Une activation sur ID inconnu ne doit pas inventer auteur ou cellules.

**2. Résoudre la relation trigger → marque avant tout calcul de dommages/consommation.** `data/fields.json:7895-7899` connaît seulement `TriggerGlyphTrap.triggering_character_id=4`, ses autres tags étant `fvil/fvim`. Rattacher les champs inconnus au **mark_id réel** avec sérialiseur/descripteur 3.7.13.17 ou un replay expurgé ; seule une action `unmark_cells` avec ID identique autorise la suppression du registre. Pour Sram, demander ordre déplacement→trigger→dégâts→unmark et deux marques superposées. Pour Féca, distinguer entrée/aura, début/fin de tour et déclenchement manuel ; `M09/RAPPORT.md:35-47` est **guide externe**, pas preuve client.

**3. Séparer bombe Roublard de l'occupation d'invocation.** `FightStatsReader.cs:274-330` et `LaunchService.Combat.cs:728-752` suivent ID/cellule/propriétaire, sans établir bombes ni murs. `SpellResolver.cs:297-313` crée des placeholders d'invocation avec 1/1 PV et des marques génériques de provenance différente. Pour un mur, demander d'abord **preuve d'identité/type/owner/cellule/état de vie**, puis deux bombes même classe/élément, alignement et événement effectif. `M02/PASSAGE_DEVELOPPEUR.md:12-33` reste une proposition antérieure sans preuve de formule.

## Oracles discriminants proposés, non exécutés

| Cas | Entrée/variation | Oracle indépendant et sortie fiable |
| --- | --- | --- |
| **N05-A activation ≠ retrait** | Pose confirmée A, B ; `activate_glyph_trap(A,true)`, puis `activate_glyph_trap(A,false)`, aucun `unmark_cells` | Le registre **source** conserve A et B avec état A mis à jour. Réel serveur : attendre snapshots/trames ; ne pas déduire disparition. |
| **N05-B trigger inconnu** | Deux pièges au même centre/zone, activation ou `trigger_glyph_trap` pour A, pas de `unmark_cells` identifiable | **Unknown** pour A/B si l'association aux tags obfusqués manque ; ne supprimer ni l'un ni l'autre. Une vraie séquence doit différencier trigger, dommages et retrait. |
| **N05-C glyphe Féca** | Cible arrive dans la zone avant son tour, puis y commence son tour ; même marque/horizon | Comparer `trigger_glyph_trap`/PM/PV au moment de l'entrée et du start-turn ; l'aura de PM n'établit pas que les dégâts start-turn ont été appliqués. |
| **N05-D bombes opposées** | Deux summons alignés, propriétaire/type différents vs identiques | Occupation connue, présence du **mur et formule** restent Unknown tant qu'une trame d'identité et d'effet les établit. |

## Contrat minimal d'intégration à documenter

Un `ObservedFightMark` a **Id, Author, Type, Cells, Active**, source `FightMarks.cs:6`. Le `SpellMark` simulé stocke `ActionId, SpellId enfant, OwnerId, Cells, SourceSpellId, SourceGrade, Duration`, source `SpellResolver.cs:18-20`. **Ne pas joindre ces types par `SpellId` ou position** : il n'existe pas de clé serveur commune prouvée. Maintenir `Unknown` pour rôle exact de `mark_type`, `TriggerGlyphTrap` champs obfusqués, causes de retrait, expiry, zones effets, class/rang Sram/Féca et toute formule Roublard. Ne pas introduire un ID fictif de sort ou de message.

Pour la validation suivante : fournir de vrais messages **pose → trigger → dégâts → éventuel unmark** et deux bombes avec leur propriétaire/classification, sur le client hashé, plus la source temporelle et le message brut à l'offset. Les tests déjà rapportés `official-client-371317-20261103.md:1211-1233` vérifient le câblage **synthétique** ; aucun nouveau test de N05 n'a été lancé. Rien ne peut être marqué `valide-live`.
