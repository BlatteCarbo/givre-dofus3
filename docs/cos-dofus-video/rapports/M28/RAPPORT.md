# M28 — Solar : cycle 3.7, tour global / tour du boss et Songes

Recherche documentaire du 4 novembre 2026. **Deux modes à traiter séparément** : Tour de Solar (donjon) et apparition de Solar dans les **Songes infinis**. Objectif pour GivreClone : spécifier un automate de phase, les effets de Nécronyx/Pyrozomb, les fenêtres de dommages et le caractère décisif de la 3.7 sur le **déclencheur temporel** en Songes. Aucune action en jeu, ni données serveur ni test exécuté.

## Sources effectivement croisées

- **S1 — annonce de changement 3.7** : [Dofus pour les Noobs, « Mise à jour 3.7 »](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html), publiée le **4 octobre 2026**, sections « Bethel, Solar, Calcinés et Submergés » et « Solar » : (a) **Nécronyx ne passe plus le tour du personnage qui le lance** ; (b) **les monstres ressuscités ne sont plus invulnérables un tour** ; (c) **« le changement des états de Solar se fait désormais au tour global et non plus au tour de Solar. Cette modification impacte uniquement les songes infinis »**. La règle (c) ne doit **pas** être appliquée indistinctement à la Tour de Solar. S1 est un guide communautaire qui synthétise le patch, sans log du client précis.
- **S2 — guide du donjon actualisé 3.7** : [Dofus pour les Noobs, « Tour de Solar »](https://www.dofuspourlesnoobs.com/tour-de-solar.html), mis en ligne le **4 décembre 2017**, **dernière mise à jour le 5 octobre 2026**. Détaille Pyrozomb, Nécronyx, quatre états, fenêtres de 3 tours, invulnérabilité hors Nadir, danger du coup fatal, glyphe périphérique, succès et expéditions. Il **explicite la modification Nécronyx 3.7**. La description de la Tour de Solar **ne fournit pas de replay Songes 3.7**, donc le déclencheur exact se prend de S1.
- **S3 — recoupement octobre 2026** : [Dofuserie, « Tour de Solar »](https://www.dofuserie.com/donjons/tour-de-solar/), mis à jour **octobre 2026**. Reprend fenêtres Aurore/Zénith/Crépuscule/Nadir, nécessité de Nécronyx avant le kill, distinctions **Expédition de l'Audace / Bravoure** et mécanismes de glyphe. Complément pédagogique, pas journal serveur.
- **S4 — fiche synthétique communautaire** : [Dofus Grimoire, boss Solar](https://dofus-grimoire.fr/boss/?q=Solar). Indique trois tours par phase, vulnérabilité uniquement Nadir et **Nécronyx pour progresser** dans la séquence. Ne tranche pas un événement de combat réel sur la build 3.7.13.17.
- **V1 — vidéo 2026 vérifiée via MCP** : **Deplai**, [« FARM 100 DONJONS SOLAR 🧠 COMBIEN CA RAPPORTE ? | TEAM SADI 3.6 »](https://www.youtube.com/watch?v=3JEP1fidMkw), **15 juin 2026**, durée annoncée **9 min**, version affichée **3.6**. Description : chapitre « SALLE BOSS » [3:15](https://www.youtube.com/watch?v=3JEP1fidMkw&t=195s), « SCREENS » 6:35, « MEILLEUR TEMPS » 7:38. get_video_info et get_available_languages réussis ; seule langue détectée : **en (auto-generated)** ; get_timed_transcript(lang=fr) échoue avec « YouTube is blocking requests from your IP ». **Aucun segment horodaté réel fourni, aucune image de plateau vérifiée**, donc pas d'assertion de règle issue de la vidéo. Tutoriel **donjon 3.6**, incompatible comme preuve du timing Songes 3.7.

**Cible** : dernière référence du kit client **3.7.13.17** ; les sources S1/S2 décrivent **3.7** (pas vérification du détail de cette sous-build). Les propriétés temporelles en Songes doivent être lues dans les événements reçus : l'annonce S1 décrit le **type d'horloge** mais ne donne pas la phase exacte de l'événement de début/fin de round ni l'ordre exact de la résolution de Nécronyx dans les Songes.

## Registre atomique versionné

| ID | Règle ou risque | Preuve exacte | Statut |
| --- | --- | --- | --- |
| **SOL-M28-01** | Quatre états ordonnés **Aurore → Zénith → Crépuscule → Nadir** ; dans le donjon la description prévoit **3 tours** par état, Aurore T1–3, Zénith T4–6, Crépuscule T7–9, Nadir **à partir de T10** sans accélération. | [S2 §« Principe du combat »](https://www.dofuspourlesnoobs.com/tour-de-solar.html), [S3](https://www.dofuserie.com/donjons/tour-de-solar/) | **corrobore-source** ; reprise/rebouclage selon événements |
| **SOL-M28-02** | Solar est **invulnérable** en **Aurore**, **Zénith**, **Crépuscule** ; ne peut être endommagé normalement qu'en **Nadir**. | [S2 §« Principe du combat »](https://www.dofuspourlesnoobs.com/tour-de-solar.html), [S4](https://dofus-grimoire.fr/boss/?q=Solar) | corrobore-source |
| **SOL-M28-03** | **En Songes infinis 3.7 exclusivement**, changement des états de Solar au **tour global** au lieu du **tour de Solar**. | [S1 §« Solar »](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html) | **corrobore-source versionné 3.7** ; instant précis round-start/end unknown |
| **SOL-M28-04** | **Dans le donjon**, garder le déclencheur **tour du boss** décrit par S2/S3 ; la note S1 limite explicitement le nouveau déclencheur aux Songes. | [S1](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html), [S2](https://www.dofuspourlesnoobs.com/tour-de-solar.html) | corrobore-source pour distinction ; point exact de phase à confirmer |
| **SOL-M28-05** | **Nécronyx** lancé sur Solar fait avancer son état au prochain moment de transition (S2 : **prochain tour de Solar** dans donjon), puis l'état Nécronyx est consommé. Il peut accélérer le passage à Nadir ; une application ne saute **qu'un état**, non trois. | [S2 §« Principe du combat »](https://www.dofuspourlesnoobs.com/tour-de-solar.html) | corrobore-source dans le donjon ; sous Songes 3.7, transition sur horloge globale à observer |
| **SOL-M28-06** | En **3.7**, lancer **Nécronyx ne termine plus le tour du lanceur**. | [S1 §« Bethel, Solar »](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html), [S2 note « ne passe plus votre tour »](https://www.dofuspourlesnoobs.com/tour-de-solar.html) | corrobore-source 3.7 ; ne pas supposer PA/relance d'un sort non enregistré |
| **SOL-M28-07** | En **3.7**, un monstre **ressuscité** n'obtient plus l'**invulnérabilité 1 tour** qui existait avant 3.7. | [S1](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html), [S2](https://www.dofuspourlesnoobs.com/tour-de-solar.html) | corrobore-source, différent de l'invulnérabilité propre des états de Solar |
| **SOL-M28-08** | **Pyrozomb** sur les monstres provoque résurrections, renvoi de dégâts dans un carré de taille 1 autour de la cible touchée et dégâts de mort dans cercle de 2 ; Nécronyx sur l'ennemi **supprime ces effets** et empêche le ressuscité de jouer si l'ennemi meurt sous Nécronyx. | [S2 §« État spécial Pyrozomb »](https://www.dofuspourlesnoobs.com/tour-de-solar.html), [S3](https://www.dofuserie.com/donjons/tour-de-solar/) | corrobore-source ; détailler effet au niveau de cible, et l'horloge de réactivation |
| **SOL-M28-09** | **Solar doit mourir sous Nécronyx** ; sinon une sanction létale est annoncée contre toute l'équipe. Une application sur Solar en Nadir supprime également le renvoi de Pyrozomb pendant la fenêtre prévue, mais le boss peut redevenir Aurore à son prochain tour. | [S2 §« Attention » et §« Principe »](https://www.dofuspourlesnoobs.com/tour-de-solar.html) | corrobore-source, exactitude d'effet à valider sur 3.7.13.17 |
| **SOL-M28-10** | Nécronyx sur Solar ou Calciné applique en contrepartie **20 % d'érosion supplémentaire pendant 3 tours** au lanceur dans S2 ; la fiche décrit que le sort est lançable **tous les 2 tours**. | [S2 §« État spécial Pyrozomb »](https://www.dofuspourlesnoobs.com/tour-de-solar.html) | corrobore-source du guide ; disponibilité réelle par personnage et ordre des ticks à vérifier |
| **SOL-M28-11** | Le glyphe périphérique en donjon inflige au **début du tour de l'acteur** des pénalités différentes : Aurore **−300 puissance**, Zénith **20 % érosion**, Crépuscule **−2 PA**, Nadir **−4 PO**. | [S2 §« Principe du combat »](https://www.dofuspourlesnoobs.com/tour-de-solar.html) | corrobore-source ; classes de glyphe à distinguer des expéditions |
| **SOL-M28-12** | Les effets naturels de chaque phase dans S2 sont distincts : en Aurore Solar soigne et donne +1 PA aux personnages ; en Zénith boost des monstres +200 puissance et +2 PA aux personnages ; Crépuscule −1 PA aux personnages ; Nadir −2 PA aux personnages. | [S2 §« Principe du combat »](https://www.dofuspourlesnoobs.com/tour-de-solar.html) | corrobore-source ; restrictions exactes de cibles et moment de tick unknown |
| **SOL-M28-13** | **Expédition Audace** : bonus à tous selon état **au début du tour de Solar**, au changement d'état **bonus de l'état précédent** (Aurore +1000 PB, Zénith +300 Puissance, Crépuscule +3 PA, Nadir +3 PM) ; **Bravoure** : glyphes spéciaux autour des personnages/invocations avec cumul. | [S2 §« Expéditions de guilde »](https://www.dofuspourlesnoobs.com/tour-de-solar.html), [S3](https://www.dofuserie.com/donjons/tour-de-solar/) | corrobore-source pour **mode expédition**, **pas** une règle Songes ni donjon normal |
| **SOL-M28-14** | Les anciennes stratégies 3.6 qui comptent le nombre de tours joués **par Solar** pour déclencher la phase **en Songes** ou qui terminent le tour du joueur après Nécronyx sont **obsolètes en 3.7**. | [S1](https://www.dofuspourlesnoobs.com/mise-a-jour-307.html), [V1 3.6](https://www.youtube.com/watch?v=3JEP1fidMkw) | obsolete-pour-cible pour l'ancien modèle ; aucune affirmation orale tirée de V1 |

## Machine à états proposée : deux horloges non interchangeables

**États du boss :** Phase=Aurore/Zénith/Crépuscule/Nadir/Unknown ; pendingNecroOnBoss (source acteur, tour d'application, délai réel, éligibilité), necroActiveAtDeath (booléen confirmé ou Unknown), bossAlive, bossInvulnerableByPhase, bossPhaseTicks, roundGlobal et solarTurnNumber distincts, compteur de changements reçus, condition de re-bouclage si Nadir finit.

**Modes** :
- **Tour de Solar/donjon standard** : source S2 place les changements à des **débuts de tour de Solar**, au fil de trois tours par état, sauf accélération Nécronyx. L'événement solaire de référence est un **SolarTurnStarted/PhaseChanged**.
- **Songes infinis 3.7** : S1 dit que le changement se produit désormais au **tour global**. Conserver un événement **GlobalRoundBoundary/PhaseChanged** distinct du **SolarTurnStarted**, qui peut survenir à un autre instant. Il faut observer l'événement qui déclenche exactement la transition au sein du round. Pour tout cas ambiguous, **ne pas consommer un Nécronyx à l'avance** et maintenir la phase Unknown si la transition n'est pas confirmée.
- **Expédition de guilde** : indicateur supplémentaire Audace/Bravoure avec effets propres ; jamais automatiquement Songes. Ne pas fusionner les boosts « +1000 PB Audace » avec les « +1000 d'esquive PA/PM Aurore » de Solar en donjon.

**Garde de létalité** : BossKillingBlow doit vérifier **Nadir** et **Nécronyx actuellement actif** et la cible vivante. Si ce second critère est Unknown, la stratégie ne doit pas provoquer une mort en la supposant sûre. Un ACK d'émission de Nécronyx ne vaut pas son application.

## Tours de base et usage de Nécronyx

| Repère naturel en donjon sans accélération | Etat | Vulnérabilité | Tactique et risque |
| --- | --- | --- | --- |
| Tours **1–3** | **Aurore** | Invulnérable | Survie/positionnement ; +1 PA personnages et soins selon S2 |
| Tours **4–6** | **Zénith** | Invulnérable | Forts dégâts de Solar ; +200 Puissance sur monstres, −6 PO possible par Éruption solaire |
| Tours **7–9** | **Crépuscule** | Invulnérable | Préparer un burst pour Nadir ; −1 PA personnages |
| **À partir de T10** (fenêtre Nadir) | **Nadir** | **Vulnérable** | Nécronyx sur Solar avant kill, mais ne pas l'appliquer prématurément si le boss rejoue et reboucle |

**Ne pas copier cette numérotation sans contrôle en Songes 3.7** : la même succession d'états existe, mais le **compteur déclencheur est le tour global**, pas les activations du tour de Solar. Le détail « trois tours chacun » est tiré de S2 pour donjon ; sa durée Songes 3.7 doit être confirmée par données d'états réellement reçues.

**Enchaînement proposé** : préparer sur les phases invulnérables ; si Solar devient Nadir, un premier acteur **A** (qui possède réellement Nécronyx disponible) lui applique l'état ; si l'effet est confirmé et les PA d'A subsistent selon le patch, A puis B/C/D frappent dans le même round. Si l'équipe ne peut pas le tuer avant qu'il reprenne un tour/boundary qui le ramène Aurore, il faut recalculer la fenêtre et l'état Necro plutôt que maintenir une prédiction de kill. En cas de résurrection d'un ennemi, l'invulnérabilité supplémentaire d'un tour a été supprimée en 3.7 ; cela ne garantit pas automatiquement qu'il est vulnérable à un sort de l'équipe, car d'autres états peuvent rester.

## Plans solo, duo, trio, quatuor (fixtures futures)

| Equipe | Plan documenté de principe | Contre-effet et ressource |
| --- | --- | --- |
| **Solo A** | A survit pendant Aurore/Zénith/Crépuscule, conserve Nécronyx et son burst pour Nadir ; vérifie expiration/relance avant le coup final. | Aucun PA/dégât inventé ; **Nécronyx 3.7 ne passe pas le tour**. Si pas assez de dommages, autre fenêtre. |
| **Duo A+B** | A pose Nécronyx **avant** B (si A joue avant B) ; B fait le burst en Nadir ; tenir Moribombe à distance ou l'éliminer avec Nécronyx. | Si B joue avant A, un kill B ne peut présupposer l'application ultérieure ; renvoi Pyrozomb/risque de wipe. |
| **Trio A+B+C** | A contrôle/retient les ressuscités, B applique Nécronyx, C frappe. En donjon S2 recommande Moribombe d'abord pour réduire le risque. | Ressuscité en 3.7 **pas invulnérable 1 tour** mais risque de Pyrozomb si mort sans Nécronyx ; cooldown par acteur. |
| **Quatuor A+B+C+D** | A tacle, B prend le premier tour et applique Nécronyx, C/D font le burst en Nadir ; suivre chaque acteur et les invocations. En Songes, observer la **frontière de round global** avant toute prédiction de fenêtre. | Nombre d'acteurs ne doit pas devenir un compteur de phase ; renvoi en zone carré1, glyphe bord, initiative et phase peuvent invalider le kill. |

## Contre-exemples et vérifications à proposer

| Cas | Entrées / action | Ce que doit produire l'automate |
| --- | --- | --- |
| **N1 Songes, horloge fausse** | Mode Songes 3.7 ; changement de round global confirmé ; Solar n'a pas encore rejoué | Nouvelle phase **selon l'événement global** même sans SolarTurnStarted ; un plan indexé sur ses tours reste périmé |
| **N2 donjon, horloge fausse** | Mode Tour de Solar 3.7 ; round global avance mais Solar n'a pas encore son événement de phase | Ne pas imposer la transition de Songes par le seul round ; attendre état/tour du boss réel |
| **N3 hors Nadir** | A tente d'attaquer Solar en Zénith | Dommages refusés par invulnérabilité normale ; Nécronyx sur Solar ne garantit pas immédiatement Nadir |
| **N4 kill sans Nécronyx** | Solar Nadir, il lui reste peu de PV ; C tente coup fatal sans état Necro | Danger de **mort de l'équipe** annoncé ; ne pas compter cette victoire comme sûre |
| **N5 retour Aurore** | Nadir, Nécronyx appliqué avant la fin mais boss rejoue avant coup fatal | Phase peut reboucler vers Aurore, invulnérabilité et effet Necro recalculés ; annuler le plan |
| **N6 Nekro ancien coût temps** | En 3.7 A lance Nécronyx avec actions légales restantes | **Son tour continue**, à l'inverse des anciennes mécaniques ; PA exact restant dépend du coût du sort |
| **N7 ressuscité** | Monstre tué puis ressuscité sous Pyrozomb | **Pas** d'invulnérabilité d'un tour héritée de l'ancien patch ; distinguer le comportement de résurrection et l'état Nécronyx |
| **N8 expédition Audace** | Solar passe Aurore→Zénith lors de son tour, mode Audace | Boost de l'**état précédent** à ce changement, pas celui que montre son nouvel état ; à vérifier hors Songes |
| **N9 expédition Bravoure** | Multiples invocations, glyphe de phase sur leurs cases | Glyphes spéciaux, cumul et départ de tour, ne pas les projeter sur mode donjon ou Songes sans paramètre |

## Contradictions et reprise

Les changements S1 sont particulièrement importants : une **vidéo Solar version 3.6**, même authentique, ne démontre pas le **tour global en Songes 3.7**. Le seul tutoriel spécialisé retrouvé est le récap vidéo Deplai [Solar 15/06/2026](https://www.youtube.com/watch?v=3JEP1fidMkw), chapitre boss [3:15](https://www.youtube.com/watch?v=3JEP1fidMkw&t=195s) annoncé dans sa description. Le connecteur MCP n'a pas délivré sa transcription (IP bloquée). **Aucune preuve de plateau ni citation de propos datés**. Les valeurs, comptes de tours et déclencheurs viennent de S1/S2/S3.

**À capturer ultérieurement dans le client 3.7.13.17** : même Solar en Songes et donjon, snapshot au début/fin du tour global et au début/fin du tour de Solar ; événements de pose/consommation Nécronyx et changement de phase, morts/ressurrections, états Pyrozomb, renvoi, sanction au kill sans effet, et différences d'expéditions. Hors de cette preuve, l'horloge globale a un **rôle** établi mais son **instant exact** de basculement et certaines durées sont **Unknown**.
