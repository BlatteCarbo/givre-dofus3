# DofusDB — attestation locale des droits IA/MCP

## Déclaration et limites

L'utilisateur a explicitement déclaré détenir un partenariat DofusDB couvrant l'utilisation IA/MCP. Cette déclaration est une autorisation de l'opérateur, pas une vérification indépendante de son contrat : aucun contrat signé, aucune pièce de signature ou confirmation externe n'a été contrôlé dans ce lot. Les droits du site public, ceux de l'API, les usages commerciaux et la redistribution peuvent être différents. Une autorisation de scraping ne vaut pas à elle seule permission de transmettre un export à un modèle tiers.

Le nouveau programme tools/dofusdb-partner-catalog/partner_review_contract.py conserve une attestation locale **d'un seul fichier source**, strictement liée à la version du jeu, au SHA-256 des octets, à la surface site ou API, aux flags IA et MCP, à une date de validité et à une référence interne du partenariat. Il ne stocke aucun secret ni identité personnelle requise, ne contacte aucun serveur, ne déclenche pas de scrape et n'accorde aucun droit d'exécution Dofus.

## Commandes et contrat

Opération attest : requires --source-file, --surface site|api, --game-version X.X.X.X, --operator-label, --agreement-reference, --statement exact, --ai, --mcp, --site-scope ou --api-scope selon la surface, --valid-until YYYY-MM-DD et --output pour un nouveau fichier local. Phrase d'attestation obligatoire :

    I attest I have DofusDB partner permission for the specified site/API AI and MCP use.

Exemple à adapter avec des chemins réels locaux, une date et une version *prouvée* :

    python tools/dofusdb-partner-catalog/partner_review_contract.py attest --source-file C:/partner/data.json --surface api --game-version 3.7.11.11 --operator-label operator-local --agreement-reference partner-scope-2026 --ai --mcp --api-scope --valid-until 2027-01-01 --statement "I attest I have DofusDB partner permission for the specified site/API AI and MCP use." --output C:/partner/private-receipt.json

Opération verify (lecture seule) :

    python tools/dofusdb-partner-catalog/partner_review_contract.py verify --receipt C:/partner/private-receipt.json --source-file C:/partner/data.json --surface api --game-version 3.7.11.11

API Python : create_attestation(...) construit un reçu déclaratif avec id déterministe de la forme operator- plus 32 hex. verify_attestation(receipt, source_path, required_surface, required_version) retourne allowed et status; en cas de correspondance stricte, status=OPERATOR_ATTESTED_NOT_VERIFIED, independentlyVerified=false et runtimeAuthorized=false. Une mauvaise portée, mutation de fichier, version inconnue, reçu expiré, fausse prétention de vérification indépendante, absence de consentement IA/MCP ou JSON altéré revient à BLOCK. Un hash de reçu n'est **pas une signature cryptographique d'une organisation**, et peut être recalculé par le détenteur du fichier.

Le fichier source fait au maximum 128 Mio ; l'attestation JSON fait au maximum 8 Kio. Les champs inconnus et les doublons JSON sont rejetés. Aucun reçu n'est mis dans Git ni transmis automatiquement à un service IA. L'opérateur choisit explicitement un nouveau chemin de sortie, écriture create-only et sans écrasement.

## Contrat d'intégration GivreWork

Le prime doit appeler le vérificateur de fichier dans son propre service local, puis former un ProfessionMcpPartnerGrant côté application avec la version, le SHA-256 et l'id du reçu correspondant à la provenance réelle (sourceKind=dofusdb, jamais renommé local-owned). Le reçu doit provenir d'un chargement sécurisé par l'application, jamais directement du JSON d'un client MCP. L'extension de prévisualisation ProfessionMcp ne divulgue pas de plan partenaire sans preuve complète de correspondance et expiration. La façade ProfessionMcp existante demeure fail-closed pour DofusDB jusqu'au raccordement approuvé par prime.

Une collection DofusDB dont sourceGameVersion=UNKNOWN ne doit pas être marquée artificiellement 3.7.11.11/3.7.12.12. La permission ne certifie ni vérité de recette, ni gain XP, ni prix HDV, ni trajets, ni ACK réseau.

## Vérification

    python -m unittest discover -s tools/dofusdb-partner-catalog -p test_partner_review_contract.py -v

18 tests synthétiques hors réseau : droits absents, site/API non interchangeables, version, modification SHA, expiry, JSON dupliqué, fausse certification et autorisation runtime refusées. Le lot n'a modifié aucun collecteur, outil MCP ou module client Dofus.
