# Round 6 — vérification de version offline

Outil : `tools/Upgrade-Check.ps1`. Lit uniquement le marqueur de version local de Dofus, la déclaration `data/bundle-build.json`, et éventuellement deux fichiers `.proto` déjà disponibles. Dépose les rapports dans `artifacts/R6-upgrade-check`. N'ouvre pas Dofus, ne lit pas de compte, n'exécute pas de réseau et ne promeut aucun mapping vers les encodeurs runtime.

Commande simple :

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Upgrade-Check.ps1
```

Résultat vérifié le 18 octobre 2026 : `VERSION_MATCH_METADATA_ONLY` ; installé `3.7.11.11` = déclaré `3.7.11.11`. `protocolCertified=false` et `gameplayCertified=false`, même lorsque les versions sont égales. Pour une nouvelle version, `VERSION_MISMATCH` est affiché, et la liaison au mapping actuel ne doit pas être utilisée.

Si deux extractions `.proto` locales et attribuées à leurs builds exacts existent, lancer l'audit DRI :

```powershell
powershell -NoProfile -ExecutionPolicy Bypass -File tools/Upgrade-Check.ps1 `
  -RunProtoMap -BaseVersion '3.7.11.11' -TargetVersion '<VERSION_INSTALLEE>' `
  -BaseProto 'C:\chemin\ancien.proto' -TargetProto 'C:\chemin\nouveau.proto'
```

Le script appelle `python -B -m dri proto-map` avec `PYTHONPATH` temporairement dirigé vers le DofusReverseIndexer **en lecture seule**. Il génère un JSON `proto-map.review.json` et un CSV `used-messages-review.csv` limité aux messages utilisés par `RequiredMapping.Scanned` du clone. Les statuts `STRUCTURAL_CANDIDATE`, `REVIEW_TAGS` et `UNRESOLVED_OR_AMBIGUOUS` imposent une revue humaine, y compris pour des numéros de champs inchangés. Le rapport n'autorise aucune connexion de gameplay.

L'écran de diagnostic pourra consommer `upgrade-summary.json` mais son branchement visuel reste distinct : le dépôt possède déjà `MZP11CytrusVersionProbe` et `MappingCatalog.LocalEditionBinding`, qui rejettent les écarts de build. Voir `docs/codex/R5_VERSION_ADMISSION.md` pour la limite de provenance du mapping publié.
