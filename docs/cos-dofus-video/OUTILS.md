# Outils a connecter dans Chat On Steroids

## Configuration recommandee

| Besoin | Choix | Necessite |
| --- | --- | --- |
| Lire/ecrire les rapports | Outils fichiers natifs CoS | Necessaire ; dossier de ce kit comme racine |
| Lire les paroles horodatees | `jkawamoto/mcp-youtube-transcript` | Recommande ; uv/uvx et Git pour la commande fournie |
| Inspecter la video et les sources | Navigateur CoS avec captures | Necessaire pour les affirmations visuelles |
| Navigateur si aucun outil CoS adapte | Microsoft Playwright MCP | Optionnel ; Node.js 18+ et navigateur disponible |
| Chercher et recouper | Recherche web CoS + lecture de pages | Recommande |
| Appliquer la methode | Skill fourni | Optionnel ; le prompt seul suffit |

Pas besoin d'ajouter un MCP fichiers si CoS peut deja lire et ecrire le dossier. Pas besoin d'exposer les comptes du bot, les cookies du jeu ou un MCP de pilotage Givre pour cette mission.

## Ajouter les serveurs

`mcp.example.json` reprend le format standard `mcpServers`. Si CoS propose des champs individuels plutot qu'un import JSON, renseigne nom, commande et arguments separement. Ce kit ne suppose pas le nom exact de ses menus et ne modifie pas sa configuration existante.

### YouTube Transcript

Commande : `uvx`

Arguments : `--from`, `git+https://github.com/jkawamoto/mcp-youtube-transcript`, `mcp-youtube-transcript`.

Outils documentes : `get_video_info`, `get_available_languages`, `get_timed_transcript`, `get_transcript`. Pour le francais, utiliser `lang: "fr"` si disponible ; parcourir `next_cursor` jusqu'a couverture du passage recherche. Les transcriptions automatiques peuvent mal reconnaitre les noms de sorts.

La commande Git suit la version du depot au lancement. Apres un premier essai reussi, conserver la revision effectivement utilisee dans les notes de recherche et envisager un verrouillage sur cette revision pour reproduire la collecte. Aucun numero de version non verifie n'est impose ici.

### Playwright MCP, seulement si necessaire

Commande : `npx`

Arguments : `-y`, `@playwright/mcp@latest`, `--caps=vision`.

Ce serveur offre un navigateur ; ce n'est pas un analyseur video continu. Ouvrir le passage, mettre en pause et capturer les images utiles si la lecture fonctionne. Les contraintes du navigateur, le consentement YouTube ou l'absence de lecture peuvent limiter cette voie. Un navigateur CoS deja fonctionnel est preferable a un doublon.

Sur Windows, si le client ne resout pas `npx`, utiliser le chemin absolu de l'executable effectivement installe, ou son lanceur `npx.cmd` si le client le prend en charge. Ne pas copier une configuration de shell au hasard. Le paquet doit apparaitre connecte et exposer ses outils pour considerer l'installation reussie.

## Controle de bon fonctionnement

1. Verifier que les outils exposes correspondent aux schemas reels, sans envoyer de secret.
2. Sur un lien fourni, obtenir titre et langue, puis un extrait horodate. Noter les erreurs d'acces exactes ; une transcription inaccessible reste inaccessible, pas reconstruite de memoire.
3. Ouvrir le meme passage dans le navigateur. Verifier que la capture montre bien la video au bon instant, pas seulement la page ou une miniature. Si impossible, limiter la fiche aux paroles et marquer les faits visuels non verifies.
4. Creer une premiere fiche dans `rapports/`, verifier qu'elle est lisible, puis traiter les autres liens.

Si les sous-titres manquent : utiliser une transcription ou des captures fournies par l'utilisateur, ou un outil de transcription audio local deja disponible. Indiquer langue, origine et limites de l'ASR ; une transcription audio seule ne donne pas l'etat du plateau. Aucune cle de service payant n'est requise par la configuration proposee.

## Sources de configuration consultees le 4 novembre 2026

- https://github.com/jkawamoto/mcp-youtube-transcript : installation uvx, outils, langues, pagination.
- https://github.com/microsoft/playwright-mcp : configuration standard, Node.js, capacites vision.
- https://dofensive.com/fr : base possible pour recouper, selon disponibilite et version de chaque fiche.
- https://doflinks.fr : decouverte de sources ; un lien d'annuaire n'est pas une preuve de mecanique.

Documentation verifiee ; lancement des serveurs et integration CoS non executes dans ce kit.
