# P05 — Sources, méthode et manque

## Empreintes et origine

- Client officiel 3.7.13.17 : `rapports-v2/N01/PREUVES.md:3-20`. `GameAssembly.dll` SHA256 `ad013415455d93bd865aef70fcf1b7c6f704c799f85e2ef301aa7fa610ef2591`; metadata `ad2880162d7e0aee219fdc81e4a3817cc86cca4aba3df3ce86fd470c1bb9a6af`.
- Export 4 classes N01 `EFFECTS_3.7.13.17.csv`, SHA256 `cbd685e8852966961a7d155c17d7a21dd23ec8e45cac70aa31be06300f89384b`, grade 1 `12914,12921,13444` vérifié dans `N01/PREUVES.md:34-43`; les autres grades sont dans CSV.
- `GivreClone.Protocol/FightMarks.cs:6-35`, SHA256 `60b92f5ce836b63ba6c1b3fef950e95b727814f6bc1785923a2f0db4df992c70`.
- `GivreClone.Protocol/MapFightReader.cs:92-125`, SHA256 `53495b20ca67f53ca8d3e878e6744c1838e7c2f98f6c2e88adc57a2e7168d6fb`.
- `GivreClone.App/Services/LaunchService.Packets.cs:1076-1104`, SHA256 `9f335a79a6f0d53f8551fa2ffd217d661471f709da46e3d8146b09f2251ab57b`.
- `GivreClone.App/Services/Combat/SpellResolver.cs:289-314`, SHA256 `0d31722c41f62b1ed175a533ec73b1790391e3be60c25f045aeb6e05536cd2df`.
- `rapports/M01/FICHE_SRAM.md:14-45`, SHA256 `9213474e024b98e75f34d7ce56577dc7015805a5c51950bf086a1b99899330fa`.
- `rapports/M02/FICHE_ROUBLARD.md:11-48`, SHA256 `0347431915d68b5f4e36ef2d34cc573419f5cbe227cd28025c21550b3da7139d`.

Empreintes des sources vérifiées lors de cette passe par `Get-FileHash -LiteralPath <fichier> -Algorithm SHA256`, **exit 0** (les sources sont modifiables par d'autres travaux, recalculer avant import). Les lectures CoS `read` ont vérifié les sections référencées. Les analyses N05 `rapports-v2/N05/PREUVES.md:7-41` et `PASSAGE_DEVELOPPEUR.md:5-26` précisent que l'association d'un trigger à un ID de marque et les règles des bombes restent inconnues.

## Origine externe et portée

M01 : vidéo [Pchen 2025 08:57–10:08](https://www.youtube.com/watch?v=hQExxOlRS-o&t=537s), [Shurikn 2025 04:08–05:32](https://www.youtube.com/watch?v=2aFjInvshkE&t=248s), [Grimoire Sram 3.7.12](https://dofus-grimoire.fr/classes/sram/), attestés dans `rapports/M01/FICHE_SRAM.md:8-23`. M02 : [Titiloo, 7 juin 2026, 06:40–07:05](https://www.youtube.com/watch?v=zzdrk6gUG58&t=400s), [Grimoire Roublard 3.7.12](https://dofus-grimoire.fr/classes/roublard/), attestés dans `M02/FICHE_ROUBLARD.md:6-22`. **Aucune nouvelle vidéo ni capture visuelle n'a été inspectée en P05** ; ces intervalles sont des références historiques requalifiées. L'ordre décrit reste `declare-video/hypothèse`.

## Séparation formelle des preuves

- `extrait-client` : sorts/grades/action IDs N01, version et hashes.
- `lecture-code` : pose/activation/retrait séparés; identité de marque; dérivation simulée des `SpellMark` et `Summon` placeholders.
- `documentaire-ancien` : géométries relatives M01/M02, ordre de pièges, Aimantation, propagation et Poudre. Ces prédictions ne sont **pas** un reçu de DOFUS 3.7.13.17.
- `synthetic_not_executed` : les huit cas de `transitions.json`; les coordonnées ne sont ni des `cellId` ni des observations.
- `Unknown` : marque identifiée dans `trigger_glyph_trap`, ordre des effets et retraits, géométrie de propagation, bombes/owner/Combo réellement décodés, dommages et transitions de 3.7.13.17.

Les **seules commandes shell exécutées** dans la collecte P05 sont les contrôles `Get-FileHash` regroupés et les recherches `rg` déjà décrites ; aucune simulation, benchmark ou test serveur nouveau ne l'a été. Le schéma de sortie vise une future validation développeur par replays anonymisés corrélés à l'ID de combat et au reçu serveur.
