# MZP22 — état vérifié des outils REA et du binaire Givre

Le 16 octobre 2026, le catalogue des connecteurs accessibles au prime ne
présentait pas deux sessions MCP REA ni les opérations directes
`open_binary`/`analyze_function`. Le client `rea` n'était pas dans PATH ;
`npx -y rea-agents@6.4.0` fonctionne comme CLI. `rea doctor --client codex`
a indiqué une dérive d'enregistrement MCP dans le fichier utilisateur
`%USERPROFILE%\.codex\config.toml`. Aucun changement de ce fichier n'a été
effectué. `rea providers` confirme la présence d'un Ghidra 12.1.4 Windows x64
avec JDK 25 ; IDA n'est pas enregistré et Hopper est absent.

Les observations REA **réussies** sont stockées dans
`artifacts/MZP22PrimeEvidence/` :

- `OriginalGivreClonePeResources.json` : résultat canonique REA
  `inspect_pe_resources`, Evidence
  `ev_e18dbf049d83df99c33978b05fd3f7f705186abb256d4d226d3e45e16ff40144`,
  SHA-256 authentifié du binaire **détaché** :
  `21c9de5ba4d15763e9daf029fd7432e3574ea113c9d1f44175fa9e0590290758`,
  PE32+ et 10 ressources statiques observées. La couverture des ressources
  est complète **pour ce parseur uniquement**.
- `CloneManagedIdentity.json` : REA `inspect_managed_artifact` réussi
  sur **la DLL du clone .NET**. Ce résultat ne décrit pas le Givre natif original.

Un `rea analyze` profond avec `--provider ghidra` sur la copie détachée
`offline-evidence/v131-app/verified-binaries/GivreMcp.exe` s'est terminé avec
code **1** et sans dossier exploitable écrit dans
`GivreMcpNativeOverview.json` (fichier vide). Son échec n'autorise aucune
conclusion sur la structure interne de `GivreMcp` et ne constitue pas une
décompilation achevée. Aucune analyse de fonctions du GivreClone natif original
n'a abouti dans ce lot.

La construction locale `GivreClone.App` est indépendante et conserve ses sources,
mais les modules natifs protégés, le chemin réseau, l'authentification du client
Dofus TEST, le gameplay, les ACK et les fonctionnalités entières demeurent
non certifiés. Les rapports `MZP22_FEATURE_TESTS.md`,
`MZP22_REAL_LAUNCHER_TEMPLATE_20261016.md` et
`MZP22_CLONE_UI_SMOKE_20261016.md` détaillent les vérifications réussies.
