# DofusDB partenaire — catalogue large objets/panoplies/recettes

Objectif opérateur : collecter par pages publiques et conserver une provenance
signée par SHA256, puis revoir hors ligne des exports partenaires explicitement
reçus. L'utilisateur déclare un partenariat de **scraping du site**. La licence
publique de l'API DofusDB (LPNC-IA 1.0) interdit séparément certains usages
IA/MCP sans accord écrit spécifique. L'outil ne diffuse pas ses données dans
GivreWork, MCP ou une IA et **ne télécharge pas l'API publique**.

L'utilisateur a demandé CloakBrowser. La machine connectée n'a montré ni
commande CloakBrowser disponible ni onglet CloakBrowser attachable. Le
logiciel CloakBrowser public masque des signatures de bot ; aucun contournement
de 403, robots ou CAPTCHA n'a été effectué.

## Interface CLI

```sh
python tools/dofusdb-partner-catalog/collector.py crawl-site --output tools/dofusdb-partner-catalog/.cache/capture --pages 20
python tools/dofusdb-partner-catalog/collector.py review-partner-export --input X:/partner/authorized.json --output tools/dofusdb-partner-catalog/.cache/review
python -m unittest discover -s tools/dofusdb-partner-catalog -p test_collector.py
```

Le parcours public se limite aux pages HTTPS de base de données, carte et
outils ; les liens suivent les ancres de ces pages (SPA, donc **ne garantit
pas** la découverte de la base complète). Les téléchargements sont
html-only, bornés à 2 MiB/page, 1 requête/seconde maximum, sans redirection,
token, proxy, empreinte furtive ou CAPTCHA. Le collecteur lit
`robots.txt` et s'arrête si l'accès est refusé. Les SHA de captures sont
vérifiés à la reprise, checkpoint atomique. Une URL API et les pages
d'identification sont explicitement hors périmètre.

La commande `review-partner-export` attend un JSON **proposé par le projet**,
pas une réponse réseau DofusDB authentifiée, avec sections
`items, sets, recipes, drops, resources, maps, transitions` et
`gameVersion`. Elle ne corrige pas, ne fusionne pas et **n'invente
pas** les recettes, composants, proportions, probabilités de drop ou liens
mine/caverne. Elle valide dimensions et SHA puis produit seulement
`review-manifest.json` avec `aiMcpAllowed=false`.

**État :** outil de collecte/revue prêt, pas de scraping exhaustif effectué,
pas de catalogue réel Bouftou admis en production, droits IA partenaire
non vérifiés et aucun usage MCP ou gameplay des exports publics.
