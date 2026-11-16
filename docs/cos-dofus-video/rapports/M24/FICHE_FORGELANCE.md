# M24-F01 — Forgelance multi Feu/Terre/Air : ancre Lance et zones dépendantes

## Identité

- ID `M24-F01`, Dofus PC Unity **cible locale annoncée 3.7.13.17**, analyse du **04/11/2026**, mécanique d'un tour **Fer Rouge (pose conditionnelle) → Effondrement (attraction) → Pluie d'Airain (retrait Résistance Poussée) → Ydra (rappel et défense)**.
- **Mécanismes tierce 3.7.12** : [Dofus Grimoire Forgelance](https://dofus-grimoire.fr/classes/forgelance/), [Fer Rouge](https://openddb.dev/spells/23756-fer-rouge), [Pluie d'Airain](https://openddb.dev/spells/24219-pluie-d-airain), [Ydra](https://openddb.dev/spells/23723-ydra), [Charge Héroïque](https://openddb.dev/spells/23824-charge-heroique), [variantes OpenDofusDB](https://openddb.dev/classes/20-forgelance). [BackfromAFK 3.5→3.7](https://backfromafk.com/classes/forgelance/depuis-3-5/) confirme modifications d'équilibrage postérieures à la plupart des vidéos.
- **Métadonnées MCP 2026** : Timtoobias [Forgelance Air do pou](https://www.youtube.com/watch?v=VsPTunlZ6kQ), 14/09/2026, ~17 min ; Phiroshima [3 modes PvM 200](https://www.youtube.com/watch?v=sHKpzNsWPZU), 22/06/2026, ~17 min. Langues FR auto disponibles, **get_timed_transcript(fr) bloqué par IP YouTube pour les deux** ; pas de parole horodatée ni visuel vu. Chapitres Phiroshima [01:19](https://www.youtube.com/watch?v=sHKpzNsWPZU&t=79s), [05:54](https://www.youtube.com/watch?v=sHKpzNsWPZU&t=354s), [10:37](https://www.youtube.com/watch?v=sHKpzNsWPZU&t=637s) = **description seulement**.
- **Paroles accessibles** : XAWPIYA, [team quatre 2026](https://www.youtube.com/watch?v=VCVjlE8pVcI&t=216s), 08/06/2026, ~9 min, **294 segments FR auto, curseur null**, passage [03:36–03:53](https://www.youtube.com/watch?v=VCVjlE8pVcI&t=216s) : Forgelance pour contrôle de zone (`declare-video`). Tenmalexis, [team quatre 2025](https://www.youtube.com/watch?v=YFlyjdSo4Ek&t=29s), 02/05/2025, ~16 min, 3.1 annoncée, **225 segments FR**, [00:29–00:48](https://www.youtube.com/watch?v=YFlyjdSo4Ek&t=29s) sur Muspel/Ydra, en contexte d'ajustement 3.1. Aucune cellule/PA visuellement observée.
- **Statuts probatoires** : effets précis `corrobore-source` par références de sorts, déclarations de rôle `declare-video`, géométrie/initiative `hypothese`; aucun `observe-video` ni `valide-test` ni `valide-live`.

## Règles atomiques

| ID | Règle indépendante | Preuve et statut | Précondition non déterminée |
| --- | --- | --- | --- |
| **M24-R01** | **Armé** : Lance en main ; **Désarmé** : Lance plantée dans une **case libre**. **Fer Rouge 3 PA / 0–5 PO** inflige dégâts et marque dommages reçus **×107 %** en zone, mais plante la Lance uniquement si case est/devient libre. | `corrobore-source` [Fer Rouge](https://openddb.dev/spells/23756-fer-rouge), [Grimoire](https://dofus-grimoire.fr/classes/forgelance/) ; runtime `hypothese` | Préjudice visuel si impact sur cible occupante, ordre dégâts/libération/pose, auras. |
| **M24-R02** | Sous **Désarmé**, **Effondrement 3 PA**, zone **étoile de 2**, attire les adversaires **jusqu'à 3 cases vers la Lance**, dégâts Terre non dégressifs. | `corrobore-source` [Grimoire](https://dofus-grimoire.fr/classes/forgelance/) | Collisions, états anti-déplacement, ordre de résolution et cellule de chaque E. |
| **M24-R03** | **Pluie d'Airain 2 PA**, ciblée sur Lance quand Désarmé : zone **carré de 2**, dégâts Air et **−40 Résistance Poussée (2 tours)** sur cibles touchées. | `corrobore-source` [Pluie](https://openddb.dev/spells/24219-pluie-d-airain) | Ce n'est **pas** un retrait de PM ni une poussée immédiate. Ne pas ajouter automatiquement des dégâts de collision. |
| **M24-R04** | **Ydra 4 PA**, **relance 2 tours**, **variante exclusive de Muspel**, frappe depuis la Lance et **rappelle** celle-ci, permettant retour à **Armé** après effet confirmé. | `corrobore-source` [Ydra](https://openddb.dev/spells/23723-ydra), [variantes](https://openddb.dev/classes/20-forgelance) | Réduction dommage subis « par ennemi » sans formule fiable ; Garde guide ≈85 % uniquement si Armé et compatible. |
| **M24-R05** | **Charge Héroïque 3 PA** rapproche F et **repousse si F termine au contact** de la cible ; c'est une branche alternative au combo 12PA, conditionnelle à la cellule obtenue. | `corrobore-source` [Charge Héroïque](https://openddb.dev/spells/23824-charge-heroique) | Portée/rang/LoS/tacle/états, dommage collision et déplacement effectif inconnus. |
| **M24-R06** | E1 placé par P **avant** F entre dans la couverture du carré de centre L ; s'il arrive après F, aucune zone déjà résolue ne peut le toucher rétroactivement. | `hypothese` de planificateur, V24-3 pour principe de zones `declare-video` | Vraie initiative et écart tour/réactivité des cibles ; aucun replay. |

**Variantes exigées simultanément** : `Fer Rouge` (vs Jormun), `Effondrement` (vs Lance-cyclone), `Pluie d'Airain` (vs Lance à Incendie), `Ydra` (vs Muspel), `Épilogue` facultatif (vs Parade). Niveau **>=185** pour Fer Rouge ; rang/relances réellement admis au combat. Un plan mentionnant **Muspel et Ydra** le même tour est impossible dans ce choix de variantes.

## État initial de fixture commune M23/M24

```text
P Pandawa        (0,0)       12 PA / 6 PM, Sobre
E1 ennemi       (1,0)       portable, mobile
E2 ennemi       (5,1)       mobile
E3 ennemi       (4,2)       mobile
F Forgelance    (2,3)       12 PA / 6 PM, Armé ; Ydra prêt
R réception     (5,0)       LIBRE
L centre Lance  (4,1)       LIBRE

Si P agit avant F :
  P porte E1(1,0), marche jusqu'à (3,0), jette E1→R(5,0),
  Brassage Sobre sur E1 (12PA/6PM → 8PA/3PM).
  F voit E1(5,0), E2(5,1), E3(4,2), donc dans le carré de L.
Si F agit avant P :
  F voit E1(1,0) (hors carré de L), E2/E3 seulement à proximité.
```

**Fixture** : positions relatives, trajectoire orthogonale libre, pas de tacle/collision de mouvement avant attraction, pas d'invulnérabilité ni immunité, portée/LoS légales pour Fer Rouge, lance encore en main au départ, variantes, sorts appris, aucun compagnon/challenge restrictif. Les 12PA/6PM de F sont **hypothèses de build**, pas des données de vidéo. Les cellules E peuvent changer **par Effondrement** : on réévalue l'aire des sorts ultérieurs, on n'utilise pas figée leur position après F1.

## Tour de 12 PA — ordonné et contrôlé

| Étape | Acteur | Cast / cible | Validation d'admission | Ressources F | Effet/transition après preuve serveur |
| --- | --- | --- | --- | --- | --- |
| **F1** | F | Fer Rouge sur cellule **L=(4,1)** | F Armé, niveau, cible L **libre ou libérable**, 0–5 PO, LoS/rang, limite 1/tour | **9PA/6PM** | LanceOwner=F, LanceCell=L, **Désarmé**, dommages Feu sur cibles dans carré 1 et `x107%` sur ennemis réellement touchés |
| **F2** | F | Effondrement sur **Lance=L** | F **Désarmé**, Lance existante, variante Effondrement ; la cible est **la Lance**, pas E1 | **6PA/6PM** | Zone **étoile de 2** ; attire jusqu'à 3 cases vers L si légal ; `E1/E2/E3.actualCell` à relire, dommage Terre |
| **F3** | F | Pluie d'Airain sur **Lance=L** | Lance pas rappelée/détruite, variantes et relance disponibles | **4PA/6PM** | Zone **carré de 2** recalculée après F2 ; dommages Air et `pushResistanceDelta=-40` 2 tours **aux cibles réellement frappées** |
| **F4** | F | Ydra sur **Lance=L** | F Désarmé, Ydra hors relance, Lance cible valide, cibles vivantes | **0PA/6PM** | Zone **étoile de 2** selon état après F3, effet défensif sur F par cible ; Lance rappelée → **Armé**, Garde seulement si confirmée |

**Détails du séquencement** : la description de zone de Fer Rouge (« carré de 1 ») comprend les trois ennemis préplacés dans la fixture, mais il faut en outre vérifier **l'absence de règles d'exception** au client 3.7.13.17. L'attraction d'Effondrement peut être partiellement empêchée par l'occupation des cellules autour de L, les autres ennemis ou des états ; **ne pas fixer leurs cellules finales par arithmétique**. Ni taux de critique ni dégâts finaux ni multiplicateurs combinés de Brassage et Fer Rouge ne sont calculés. L'impact de Pluie retire une **résistance à la poussée**, pas un PM ; dans ce tour à 0 PA restant, aucune poussée supplémentaire n'est prétendue.

## Trois scénarios négatifs / de divergence

| ID | Variation et entrée exacte | Actions | Résultat discriminant / fallback | Preuve |
| --- | --- | --- | --- | --- |
| **M24-T+** | Fixture libre, P joue avant F ; Fer Rouge/Ydra prêts | P jette E1 et applique Brassage ; F1–F4 | F **0PA/6PM** ; après F1 Lance présente L et F Désarmé, puis après F4 Lance rappelée et F Armé ; cibles effectives et modifs confirmées, jamais dégâts inventés | `propose, non execute`, R01–R04 |
| **M24-T-occupied** | **L=(4,1) occupée** par une entité qui ne peut devenir libre via F1 | Fer Rouge sur L puis éventuel Effondrement | **Interdire la transition LancePos=L sans confirmation** ; même si dégâts Fer Rouge, pas de chaîne « Lance » sans objet ; chercher case libre alternative / sort Armé | `propose, non execute`, R01 |
| **M24-T-order** | **F → P** avec E1 initial (1,0) et R libre | F1–F4 puis P jette E1 | E1 **hors zone** pendant F, aucun dégât rétroactif ; comparer zone réelle 2 ennemis à zone conditionnelle 3 ennemis | `propose, non execute`, R06 |
| **M24-T-pull-block** | Ajouter obstacle entre E3 et L et état anti-déplacement sur E3 | F2 Effondrement puis F3 | E3 ne doit **pas** être déplacé fictivement jusqu'à L ; zone F3 calculée selon effet réel, non selon « attire de 3 » brut | `propose, non execute`, R02 |
| **M24-T-cooldown** | Ydra en relance, mais Épilogue équipé, F a dépensé 8PA sur F1–F3 | Tenter Ydra puis secours Épilogue | Pas de Ydra/Armé inventé ; **Épilogue 1PA** si légal, F termine **3PA/6PM**, Armé si rappel confirmé | `propose, non execute`, R04 |
| **M24-T-variants** | Forgelance a choisi **Muspel** et/ou **Lance à Incendie** | Demander Ydra / Pluie | Cast absent de build, pas de substitution implicite | `propose, non execute`, R04 |
| **M24-T-charge** | F pousse via **Charge Héroïque**, mais sa cellule finale n'est **pas au contact** | Tenter Charge | Aucun recul de cible ne doit être prévu si condition finale absente ; tenir compte de chemin/collision et relance | `propose, non execute`, R05 |

## Différenciation solo / duo / trio / 4 / 5+

| Taille / initiative hypothétique | Suite de coups, PA/PM individuels | Risques / valeur |
| --- | --- | --- |
| **Solo F** | F 12PA/6PM, F1–F4 sur groupe existant ; si une seule cible hors portée, choisir ancrage autrement (aucun PA allié). | Armé à la fin seulement si Ydra résolu ; ne pas jeter la Lance dans une case occupée. |
| **Duo P → F** | P 4PA/3PM dépensés ; F 12PA/0PM sur L, E1 rejoint la zone ; **F → P** empêche ce bonus de couverture. | P peut exposer la cible à la zone mais aussi l'amener près d'un allié fragile ; calcul survie avant maximisation de cibles. |
| **Trio P → Énutrof → F** | Énu peut entraver les ennemis packés ; ses PA/PM **unknown** ; F zones après état de retrait confirmé. | Ennemi peut esquiver le retrait ; ne pas ajouter de dégât ou d'occupation fictive. |
| **Quatuor P → Féca → Énutrof → F** | P place, Féca protège un destinataire réel, Énu ralentit, F frappe ; seuls budgets P et F fixés dans la fixture. | Une protection postérieure aux dégâts n'agit pas rétroactivement ; la lance Désarmée modifie la survie. |
| **5+** | Chaque acteur (y compris plusieurs Forge) a son `LanceOwnerId`, ses variantes, relances, positions, zones ; ordonnancement des placeurs et frappeurs. | Collision d'objet Lance, portails/allies bloquant une case, invocations/challenges ; évaluer les collisions sur les positions réelles. |

## Passage développeur / reprise

**États** : `lanceOwnerId`, `lanceCell`, `isArmed`, `isDisarmed`, `castSourceCell`, `spellTargetType`, `AoEShape`, `affectedEntityIds`, `pullDistance`, `pushResDebuffPerTarget`, `cooldown`, `variant`, `fightEpoch`, `actorPA/PM`, `serverActionOutcome`. Ce sont **noms proposés**, aucune API connue ici. Les événements `SpearSpawned` et `SpearRecalled` doivent être certifiés séparément de l'ACK de commande. Lorsqu'une action refuse ou reste `Unknown`, invalider la suite dépendant de sa Lance, ses positions ou son debuff.

**Inconnues bloquantes** : ordre dégâts → attraction → collision pour Effondrement ; formule de Garde/Ydra et de dégâts; possibles révisions 3.7.12→3.7.13.17 ; portée/LdV si paramètres source divergent ; compatibilité des zones/variants sur combat réel ; preuve visuelle des vidéos dédiées. Capturer plus tard une vraie action de pose/attraction/rappel avec position serveur et tourner les tests proposés sans jeu en ligne.
