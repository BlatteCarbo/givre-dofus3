# M23 — Passage développeur Pandawa

Référence : [RAPPORT.md](RAPPORT.md), [FICHE_PANDAWA.md](FICHE_PANDAWA.md). **Documentaire, tests non exécutés**. Sources 3.7.12 [Grimoire Pandawa](https://dofus-grimoire.fr/classes/pandawa/) et [OpenDofusDB Karcham](https://openddb.dev/spells/12787-karcham), [Brassage](https://openddb.dev/spells/12816-brassage), historique [BackfromAFK 3.7](https://backfromafk.com/classes/pandawa/depuis-3-4/). Vidéo 2026 d'équipe [XAWPIYA, 01:06–01:28](https://www.youtube.com/watch?v=VCVjlE8pVcI&t=66s), **transcript FR auto 294 fragments**, `declare-video` sur rôle Pandawa ; vidéos dédiées 2025 SAPEUH métadonnées seulement, transcript YouTube bloqué IP. Cible **3.7.13.17 non certifiée**.

## Trois règles P1

1. **M23-R01 : portage et jet atomiques.** P **Sobre**, E1 adjacent, portable, 1 PA : état **porté** confirmé ; P marche avec la cible, puis lance pour **1 PA** vers une **case libre** à portée depuis sa cellule effective. [Karcham](https://openddb.dev/spells/12787-karcham) et [Grimoire](https://dofus-grimoire.fr/classes/pandawa/). `Chamrak` est une variante **exclusive**, son bypass LdV n'est pas ajouté à Karcham. Le résultat du jet doit inclure E1 à sa nouvelle cellule et suppression du portage ; pas de déplacement « optimiste » sur simple ACK.
2. **M23-R02 : état de lanceur et bénéficiaire de Brassage.** [Brassage](https://openddb.dev/spells/12816-brassage) coûte **2 PA** ; en **Sobre** cible ennemie E1 = dommages subis ×110 % indiqués ; en **Saoul** = Pesanteur / état Brassage 3 tours, **pas** la vulnérabilité. Le boost tactique bénéficie aux attaquants qui frappent **E1 après P**, ni aux tirs antérieurs ni à E2/E3 sans application.
3. **M23-R03 : dépendance au frappeur.** Fixture : P(0,0), E1(1,0), E2(5,1), E3(4,2), F(2,3), réception R(5,0) et ancrage L(4,1) libres. **P → F** : P porte E1 (**1PA**), marche P(3,0) (**3PM**), jette E1→R (**1PA**), Brassage Sobre E1 (**2PA**). P finit **8PA/3PM** sur 12/6 ; F peut ensuite lancer une zone sur L et compter E1 **si position confirmée**. **F → P** : E1 est encore à (1,0) lors du tour de F, pas de gain de cible T1. Exacte géométrie de la zone F dans M24.

## Contrat de transitions

`PandaStance(Sobre|Saoul|Unknown)`, `CarryRequested`, `CarryResolved(P,E1)`, `MovementResolved(P,actualCell)`, `ThrowResolved(E1,actualCell,carrierCleared)`, `DebuffResolved(E1,modifier,duration)`, `TargetZoneRecomputed`. Ce sont des noms de transitions de conception, **pas du code existant**. Pour chaque action : distinguer **validée**, **refusée**, **Unknown** ; un ACK de requête ne prouve pas un effet de serveur. Les comptes de lancers de Karcham et les variantes exactes sont à lire du client **3.7.13.17**.

## Tests proposés, non exécutés

| Cas | Entrée / action | Attendu |
| --- | --- | --- |
| **Positif M23-T+** | P Sobre 12PA/6PM, E1 portable, trajet 3PM et R libres, **P → F**, portage→marche→jet→Brassage | P 8PA/3PM, E1 R **si résolutions réelles**, trois cibles potentiellement dans carré L avant F. |
| **Négatif M23-T-occupied** | R=(5,0) déjà occupée | Jet R refusé ou autre résultat réel ; **jamais E1 à R sans preuve**, ne pas lancer zone F calculée pour faux E1. |
| **Négatif M23-T-order** | **F → P**, même plateau | Pas de dégâts F sur E1 au même tour car E1 n'a pas encore été déplacé à R. |
| **Négatif M23-T-saoul** | P Saoul sans retour Sobre | Karcham portage interdit, Brassage ne produit pas vulnérabilité Sobre. |

## Risque, inconnues et relais

- Si la case R est prise ou que E1 n'est pas portable, trouver un jet légal **alternatif**, ou laisser E1 hors de la zone et recalculer. Si P bouge en portant mais que P3 échoue, garder **l'état réellement porté** jusqu'à événement de fin ; ne pas considérer qu'E1 a disparu.
- Collision, interdictions de portage, compteurs de lancer/tour/cible, temps de vie de Brassage, effets de dommages/poussées, challenges liés aux compagnons, tacle et ordre de résolution **non établis** dans les images ni sur client 3.7.13.17.
- Les **PA/PM d'Énu, Féca, F** sont des budgets **distincts**, jamais un pool. Le tableau de tour chiffre **uniquement P**, M24 chiffre **F** séparément.
- Prochaine preuve : trace serveur/capture autorisée de `CarryResolved` puis `ThrowResolved` et de l'application de Brassage, puis contrôle de la table 3.7.13.17. Statuts vidéo : `declare-video` pour guide team 2026, `metadata-only` pour deux SAPEUH 2025 ; **aucun `observe-video`, `valide-test` ni `valide-live`**.
