# N10 — Instrumentation future, sans invocation native

1. Corréler **dans les anciens logs uniquement** `fight ID, session/epoch, actor ID, request ID, sequence ID` et séparer `request_tx`, fin de séquence, ACK **du bon événement**, barrière fermée, premier tour ennemi et réponse de fin de combat. Un identifiant d'action recyclé ne doit pas fusionner deux commandes (voir `DIAGNOSTIC_COMBAT_20261023.md`).
2. Pour la phase native, capturer hors jeu **l'état du bail actif pour le bon combat**, sa génération, propriétaire, timestamp monotone, chargement du module et timeScale *lu* (pas simplement demandé). Ne pas lire la présence d'un fichier lease comme preuve d'application si la génération/epoch est périmée.
3. Produire une sortie `requestToSequenceMs / sequenceToAckMs / requestToBarrierMs / enemyTurnLatencyMs / restoreTimeMs / nativeReadyMs` avec origine temporelle claire et `Unknown` si le marqueur manque. **Ne jamais attribuer tout le temps écoulé au CPU, au serveur ou à l'animation sans mesure indépendante.**
4. Comparer MITM vs Socket seulement sur une distribution de combats/sorts/équipements/machine égaux, with jitter, refus et relances documentées. Ne pas attribuer le comportement du premier tour à Turbo sans séparation de disponibilité du planificateur et montée du module natif.

Le [JSON de chronologie](turbo_chronologie_anonymisee.json) et son [extracteur reproductible](extract_turbo.py) sont fournis pour préparer cette corrélation. **Aucun code production, process ou compte manipulé.**
