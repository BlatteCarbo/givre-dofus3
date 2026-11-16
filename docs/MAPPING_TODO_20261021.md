# Plan de mapping — à faire à partir du 21/10/2026

Document de travail : **comment** relever et mapper un message du protocole Dofus 3 (build 3.7.12.12), **quoi** mapper, dans quel ordre, avec les
captures précises à faire. Complété par `docs/MAPPING_INVENTORY_20261021.md` (tous les messages par catégorie, avec leurs champs nommés / non
nommés) et par l'outil `tools/pktdump_extract.py`.

Rien ici n'est certifié : chaque fiche dit ce qui est **prouvé** (capturé sur le vrai client), ce qui est **déduit**, et ce qui reste **inconnu**.

---

## 0. Où on en est

### Prouvé sur le vrai client 3.7.12.12 (capture MITM du 21/10/2026)
- **Création de personnage** : `CharacterCreationRequest` (code `kru`) = `modeling_information` #1 → sous-message
  `CharacterRemodelingInformation` : nom #2, sexe #3 (0 = homme, valeur par défaut omise), classe #4 (= `BreedData.id`, soit l'index de classe + 1),
  tête #5 (= `HeadData.id`), corps #7 (= `BodyData.id`, champ `gccm`), couleurs #8 (6 varints packés, valeur = RVB + 1).
  Réponse `CharacterCreationResultEvent` (`ksn`) : `result` absent = succès. Puis `CharacterFirstSelectionRequest` { character_id #1, do_tutorial #3 }.
  Le flux marche **en Socket** depuis le 21/10 (personnage « Rivith » créé sur Brial, entré en jeu).
- Suggestion de nom : `CharacterNameSuggestionRequest` (`ktb`, vide) → `CharacterNameSuggestionEvent` (`ksp`, `suggestion` #1).
- Déplacements, confirmations, identification, sélection de serveur / personnage, contexte, cartes : déjà confirmés en jeu (session socket du 20/10).

### Déduit mais **non prouvé** (à vérifier en jeu)
- `AttackMonsterRequest` (`hms`) et `MapChangeRequest` (`joc`) en 3.7.12 : le `.proto` est identique à 3.7.11 (même SHA), les 1 337 codes du mapping
  sont présents dans le client, mais aucune capture ne prouve ces deux codes précis. Premier combat en jeu = test.
- Création : classes 1–8, 10–18, 20 (Forgelance) autres que celles déjà vues ; sexe féminin ; couleurs personnalisées ; « Au hasard ».

### Connu comme **manquant**
- Suppression de personnage (messages présents, champs `fzly`/`fzlz`/`fzlx`/`fznb` et algorithme de `secret_answer_hash` inconnus).
- Pseudo de compte, boutique d'ogrines, Bourse kamas↔ogrines, clic droit joueur, dialogue / boutique PNJ côté UI, atelier, base d'indices de chasse.
- Environ **3 000 messages sur 3 725** ont au moins un champ non nommé ; ~8 400 champs non nommés au total (heuristique, voir l'inventaire).

---

## 1. Méthode (à appliquer à chaque message)

### 1.1 Capturer
1. Fermer tout `Dofus.exe` ouvert (sinon il ne passe pas par notre MITM).
2. Lancer GivreClone, **Réglages → Capture de découverte (diagnostic) = Activé** (licence développeur). *Capture exhaustive* seulement pour re-lier un
   patch (très verbeux).
3. Lancer le compte en **MITM** (« Lancer », pas « Socket »). Faire **UNE action isolée** dans le vrai client, de préférence sur un compte / personnage de test.
4. Noter l'heure exacte de l'action (elle sert à retrouver les lignes dans le log).
5. Le log est `%LOCALAPPDATA%\GivreReplica\TestRuns\givre-app-offline\userdata\logs\transport-<date>.log`. Les lignes utiles : `[pkt-dbg] C->S|S->C Nom [code] …`.
   Les trames **sortantes** du client (≤ 200 octets) portent `hex=` : c'est la vérité terrain.

### 1.2 Décoder
```bash
python tools/pktdump_extract.py <transport.log> --name <Message> --dir C2S [--json out.json]
```
Sortie : enveloppe `#4 { uid ; #2 { 'type.ankama.com/<code>' ; <charge utile> } }` décodée en arbre (varint / texte / sous-message / varints packés).
Les numéros de champ sont **bruts** : les rapprocher de `data/fields.json` (nom → numéro) et du `.proto`
(`DofusReverseIndexer/local-output/historical-extractions-v3/3.7.12.12/dofus-3.7.12.12.proto`, noms obfusqués).

### 1.3 Interpréter (sans deviner)
- Croiser **chaque valeur** avec les tables du jeu (`dofus.sqlite`) : un id de classe, de tête, de corps, d'objet, de sort, de zone se retrouve par SQL.
  Exemple qui a levé l'inconnue de la création : `select id,breed,gender,label from BodyData where id=22` → classe 9, femme, « base ».
- Refaire l'action **deux fois en changeant un seul paramètre** (autre classe, autre sexe, autre couleur) : le champ qui bouge est celui qu'on cherche.
- Les booléens / enums à 0 sont **omis** (proto3) : une valeur absente n'est pas une valeur inconnue.
- Une valeur qu'on ne peut pas expliquer reste `inconnu` dans la fiche : on ne l'envoie jamais « au jugé ».

### 1.4 Coder
1. **Encodeur pur** (sans réseau, sans Avalonia) dans `GivreClone.App/Services/…`, champs reçus par **nom** depuis le mapping (jamais de numéro en dur).
2. **Test d'octets réels** : l'encodeur doit reproduire exactement les octets capturés (`CharacterCreationEncoderTests` est le modèle).
3. Brancher dans le flux (socket : `SocketGameClient` ; MITM : `LaunchService`), derrière les garde-fous (confirmation pour tout ce qui détruit ou dépense).
4. Noms obfusqués (4 lettres) : à ne **pas** mettre dans le manifeste des champs requis (ils changent à chaque patch) ; les lire via une constante + refuser
   proprement si absents (voir `CreationBodyField`).
5. Régénérer le manifeste : `GIVRE_UPDATE_MANIFEST=1 dotnet test GivreClone.Tests --filter RequiredMapping`.
6. Tests : `dotnet test GivreClone.PureTests` (rapide) puis `dotnet test GivreClone.Tests` (complet, ~25 s). Un test Lua (`RuntimeTraceProjection`, regex)
   est instable sous charge : le relancer.

### 1.5 Règles
- Aucun paquet deviné. Compte de test pour toute action irréversible (suppression, achat, échange).
- Actions qui dépensent (ogrines, kamas) ou détruisent : **confirmation explicite** dans l'UI, jamais d'automatisme.
- Un message non prouvé reste derrière le mode « revue » ; le premier envoi réel se fait sur un compte jetable.
- Après un patch Dofus : le reçu de revue est invalidé (empreinte d'installation) ; refaire l'admission avant d'ouvrir le pilotage complet.

---

## 2. Chantiers, par priorité

Légende : **P0** = demain, **P1** = cette semaine, **P2** = ensuite, **P3** = confort. Effort : S < 2 h, M < 1 j, L > 1 j.

### P0-1 — Suppression de personnage (+ code secret) — L
Messages : `PrepareCharacterDeletionRequest` (`ksm`: character_id #1, **fzly #2, fzlz #3, fzlx #5 inconnus**), `CharacterDeletionPrepareEvent` (`ksw`:
need_secret_answer #1, name #2, secret_question #3), `CharacterDeletionRequest` (`kso`: **fznb #1**, character_id #2, secret_answer_hash #3),
`CharacterDeletionErrorEvent` (`kst`: reason #1), et la réponse de succès (probablement `CharacterListEvent`, **à confirmer**).
Captures à faire (compte de test, MITM, capture activée) :
1. Supprimer un perso **sans code secret** : cliquer « supprimer », taper le nom demandé, confirmer.
2. Supprimer un perso sur un compte **avec code secret** : noter la question affichée, répondre, confirmer.
3. Une suppression avec **mauvaise réponse** (erreur) : relève `CharacterDeletionErrorEvent.reason`.
À trouver : sens de fzly/fzlz/fzlx/fznb ; **algorithme du hash** (hypothèse classique Dofus 2 : MD5 de `<id>~<réponse>` — à vérifier en comparant le hash capturé avec
des candidats calculés localement **sur des valeurs connues**, pas à deviner). Livrables : encodeur + test d'octets, commande Lua `account:deleteCharacter`,
UI (carte perso → « Supprimer » avec double confirmation + champ code secret). Risque : irréversible → confirmation forte, désactivé en MITM-observation.

### P0-2 — Valider la création en jeu (toutes classes / sexes / couleurs) — M
Fait pour Enutrof homme (« Rivith »). À capturer / tester : Forgelance (id 20 ; index 19), féminin pour chaque classe, couleurs personnalisées (vérifier RVB + 1 sur
d'autres valeurs), « Au hasard », corps `retro` / `new_age` (`BodyData.availableAtCreation`, `payable`), nom refusé (résultat 2) / trop de personnages (3).
Livrable : tableau de vérification dans ce dossier + correction éventuelle (mapping index→id des classes, `availableAtCreation`/`payable`).

### P0-3 — Création : icônes et rendu — M à L
Aujourd'hui : classes en texte. Le client a les icônes de classes (`Stores.ClassIcons`, déjà utilisées dans la fenêtre « Choisissez un personnage »).
Têtes et corps : `HeadData` / `BodyData` portent `assetId` + `skins` ; il faut les **rendre** (looks Unity). Pistes :
- `PyDofus/d3-ts-renderer` (TypeScript / WebGL, rendu de « looks » depuis les bundles `Content/Characters/Bones` + skins, couleurs appliquées dans le shader,
  export PNG/WebP en Node avec `gl` + `sharp`). Extraction des assets : `pydofus3` ou serveur « live extract » (`game_path=… npm run liveExtract`).
  **Licence : aucun fichier LICENSE vu ; consigne « pas de skinator concurrent, pas de site payant »** → usage **local** uniquement, ne rien redistribuer,
  relire le README avant d'intégrer. Le look d'un perso se décrit par une chaîne `{bonesId|skins…|couleurs|taille}`.
- `superdofus` (même idée) : à évaluer pareil.
Plan : (1) script hors-ligne qui, pour chaque (classe, sexe, tête/corps), construit le look, rend un PNG 128×128 dans un cache
`%LOCALAPPDATA%\GivreClone\creation-icons\<breed>_<gender>_<head|body>_<id>.png` ; (2) l'app charge le PNG par id (fallback texte si absent) ;
(3) UI : grille de tuiles (classe : icône + nom ; tête : vignette ; corps : vignette pleine hauteur), sélection visible, aperçu du perso habillé avec les 6 couleurs
(skin shader). À prouver d'abord : la chaîne de look exacte pour un perso **de création** (comparer avec un `EntityLook` reçu d'un vrai perso : `CharacterAppearanceCollectionEvent`, `OutfitEntityLookChangedEvent`).

### P0-4 — Basculer MITM ↔ Socket sans déconnexion — L
Constat : le bouton refuse si le compte tourne. GivreActive faisait un **transfert à chaud** (`EasyBot/Sockets/SwitchSocket.vb`, 4 323 lignes) avec génération
de pipeline, vérification du processus propriétaire, tampon de rattrapage borné et reprise sur état serveur complet ; le doc
`GivreActive/MIZAN_PORTAGE_ANIMATION_MITM_SOCKET.md` (§2, §8, §9) décrit les phases. Version intermédiaire possible : lancer le client en MITM, attendre « en jeu »,
couper la session socket (brève reconnexion automatique, script repris). À décider avant de coder.

### P1-1 — Boutique en jeu, ogrines, abonnement, Bourse kamas↔ogrines — L (lecture d'abord)
Catégorie « Boutique » de l'inventaire (30 messages, 14 avec champs non nommés) : `Bak*` (déjà utilisé : `BakApiTokenRequest`/`BakApiKeyEvent`), `Haapi*`, achats
d'articles (abonnement : articles 7 jours / 1 mois d'après le changelog MizanBot, **non vérifiés chez nous**), Bourse.
Capture (lecture seule, sans acheter) : ouvrir la boutique, parcourir les catégories, ouvrir la fiche d'un article, ouvrir la Bourse (lire le cours). Puis, sur
un compte de test **uniquement**, un achat à faible montant pour relever le message d'achat + son accusé.
Fonctions visées : (a) afficher le solde d'ogrines et le cours kamas/ogrine ; (b) coût d'un mois d'abonnement en kamas ; (c) alerte Discord « cours bas » ;
(d) achat d'abonnement en ogrines **avec confirmation manuelle**. Contraintes : la Bourse est payante pour limiter les bots (source communautaire, à
vérifier sur le site officiel) ; un échange automatique expose le compte → manuel seulement. Ne jamais saisir de moyen de paiement.

### P1-2 — Dialogue PNJ et boutique PNJ — M
Déjà présent côté Lua (`dialog`, `sale:buyToPnj/sellToPnj`). Manque l'UI et la couverture : `NpcDialogCreationEvent`, `NpcDialogQuestionEvent`, `NpcDialogReplyRequest`,
`NpcGenericActionRequest`, ouverture boutique (`ExchangeStartOkNpcShop…`). Captures : parler à un PNJ (banquier : « Consulter son coffre »), choisir une réponse,
ouvrir une boutique, acheter / vendre 1 objet, fermer. Livrable : fenêtre de dialogue (texte + réponses cliquables) et boutique (icônes, prix, quantité) comme dans
le jeu ; commande Lua équivalente.

### P1-3 — Clic droit sur la carte (joueur, PNJ, zaap, atelier) — L
Joueur : échange (`ExchangePlayerRequest`…, 90 messages « Exchange/Trade » dont 44 à champs non nommés), invitation de groupe (`PartyInvitationRequest`…), message privé.
PNJ : actions du jeu (parler / acheter / vendre). Zaap / zaapi / prisme / havre-sac : liste de destinations (déjà en partie : `ZaapKnownListEvent`). Atelier : recettes,
nombre faisable, « Fabriquer » (`Craft*`, 32 messages). Captures : une action par type, dans le vrai client, avec la cible nommée dans le log.

### P1-4 — Chasse au trésor de bout en bout — L
Existant : commandes Lua `treasure:*` (état décodé par nom, `TreasureHuntEvent`, drapeau, creusage, abandon) mais **pas de base d'indices** (`nextMapId` = 0).
À mapper / construire : prise de la quête quotidienne (PNJ Dhain, dialogue), indices (`TreasureHuntStep*` ; le nom de l'indice → carte), phorreurs, drapeaux faux,
chasse légendaire (carte au Pupitre, fragments, Table de planification), combat final (cible = le vigicoffre marqué). Sources d'indices : données du jeu
(`PointOfInterest`, indices par carte dans `maps.sqlite` / bundles) à comparer avant toute API tierce. Capture : une chasse complète avec console de capture.

### P1-5 — Reconnexion automatique du bot bloqué — fait (à valider en jeu)
_Mise à jour 21/10 03:00 : aussi faits — création de personnage Socket (formulaire, icônes de classes, tuiles), atelier éco (`docs/ATELIER_ECO.md`), alertes de revente et d'abonnement, copie des réglages de messagerie, décor de saison._
Commit `e36d09a` : dernier cran de l'anti-blocage, 3 / heure / compte, jamais en combat / modérateur 2 h / pods ≥ 97 %. Reste : signaux supplémentaires (déplacement
refusé en boucle, perso absent de sa carte), délai de 20 s avant la reconnexion, test en jeu.

### P2 — Social, guildes, alliances, montures, maisons
| Domaine | Messages (inventaire) | Intérêt | Capture à faire |
|---|---|---|---|
| Guilde | 131 msgs, 76 avec champs non nommés | gestion membres / coffre / percepteurs | ouvrir la fenêtre de guilde, onglets, droits |
| Amis / groupe | 63 / 29 | invitations, suivi d'équipe | ajouter un ami, inviter / quitter un groupe |
| Alliance / prismes | 82 / 31 | alertes de prisme | liste des prismes (déjà reçue : `PrismListEvent`) |
| Montures / enclos | 21 / 11 | élevage automatisé | ouvrir l'enclos, nourrir, équiper |
| Havre-sac / maisons / coffres | 63 / 25 | téléportation, coffre | ouvrir / fermer, sauvegarder le zaap |
| Quêtes | 20 / 10 | bouton « Y aller » de l'onglet Infos | suivre une quête, valider une étape |
| Succès | 23 / 9 | récupération auto (déjà faite) | récupérer un succès |
| Almanax / anomalies / bonus | 59 / 15 | offrande, anomalies (déjà : `AnomalySubareaInformationEvent`) | offrande Almanax |
| Kolizéum / arène | voir inventaire | hors périmètre actuel | — |

### P3 — Combat et confort
Sorts à zone derrière un mur (case voisine dont la zone atteint l'ennemi), liste des sorts qui épargnent les alliés (`spellsdataroot`), ×N/cible, stats par heure,
thème saisonnier, mode économe en mémoire, import de scripts `.lua`, assistant IA. Voir le changelog MizanBot 1.3.2 collé le 21/10 comme référence fonctionnelle.

---

## 3. Dette technique et vérifications

- [ ] Premier **combat Socket** en 3.7.12.12 (`hms`, `joc`) : le valider, puis retirer la mention « non prouvé ».
- [ ] Pousser les commits (rien n'est poussé : branche locale `integration-opus-20261020`, ~14 commits après `4c8b501`).
- [ ] Scinder ou documenter le gros commit `cb61691` (il contient ~250 lignes de modifications antérieures de `SocketGameClient.cs`).
- [ ] Test Lua instable (`RuntimeTraceProjection.Project`, délai de regex) : borner la regex ou allonger le délai.
- [ ] Vérifier l'effet des nouveautés non testées en jeu : effets d'objets, `effectsdataroot`, banque, grilles, infobulles (liste du handoff du 20/10).
- [ ] Alerte de revente : seuils (30 %, 100 000) sans champ dans l'UI ; prix très périmés si l'HDV n'est pas rouvert.
- [ ] Valeur des zones / rentabilité des donjons : moyennes simples, sans fréquence d'apparition ni kamas lâchés.
- [ ] Capture exhaustive : sa sortie est volumineuse ; prévoir une rotation / un filtre par message.
- [ ] Mapping : ajouter dans `docs/` la table « index de classe → BreedData.id » vérifiée.

## 4. Checklist « demain matin » (ordre conseillé)
1. `git status` / sauvegarde, puis push de la branche si vous êtes d'accord.
2. Lancer l'app, vérifier le **combat Socket** sur un perso de test (ouvre aussi la capture `[pkt-dbg]` des attaques).
3. **Suppression de personnage** : trois captures (sans code, avec code, mauvaise réponse) → `tools/pktdump_extract.py` → fiche → encodeur + test.
4. Création : 3–4 variantes (autre classe, féminin, couleurs perso) pour confirmer RVB + 1 et les ids.
5. Icônes de création : essayer `d3-ts-renderer` en local (Node) sur **une** classe + un corps + une tête ; décider du pipeline.
6. Boutique / Bourse : captures en lecture seule.
7. Dialogue PNJ + boutique PNJ, puis clic droit joueur.
8. Chasse au trésor : une chasse complète en capture.
9. Mettre à jour `docs/MAPPING_INVENTORY_20261021.md` (regénérer : `scratchpad/gen_inventory.py` — à déplacer dans `tools/` si on le garde).

## 5. Annexes

Commandes utiles :
```bash
# extraire un message de la capture
python tools/pktdump_extract.py "$LOCALAPPDATA/GivreReplica/TestRuns/givre-app-offline/userdata/logs/transport-<date>.log" --name CharacterDeletionRequest --dir C2S
# retrouver un id dans les données du jeu
python -c "import sqlite3;c=sqlite3.connect('file:dofus.sqlite?mode=ro',uri=True);print(c.execute('select * from BodyData where id=22').fetchall())"
# régénérer le manifeste des champs requis
GIVRE_UPDATE_MANIFEST=1 dotnet test GivreClone.Tests --filter RequiredMapping
```
Données de référence : `dofus.sqlite` (tables `BreedData`, `HeadData`, `BodyData`, `ItemData`, `MonsterData`, `SubAreaData`…), `maps.sqlite`, proto
`3.7.12.12`, `data/fields.json`, `data/matching_current.json`, Cytrus (version publiée), bundles du client installé (`%LOCALAPPDATA%\Ankama\Dofus-dofus3`).
