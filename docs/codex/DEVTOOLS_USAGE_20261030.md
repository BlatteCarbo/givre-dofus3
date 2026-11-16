# Outils installes - 30 octobre 2026

Depuis la racine du worktree :

```powershell
dotnet tool restore
dotnet tool run dotnet-trace -- --version
dotnet tool run dotnet-counters -- --version
dotnet tool run dotnet-stack -- --version
dotnet tool run dotnet-dump -- --version
dotnet tool run dotnet-trace -- collect --process-id <PID_QA> --duration 00:00:10 --output trace.nettrace
dotnet tool run dotnet-counters -- monitor --process-id <PID_QA> --counters System.Runtime
dotnet tool run dotnet-stack -- report --process-id <PID_QA>
dotnet tool run dotnet-dump -- collect --process-id <PID_QA> --output dump.dmp
dotnet test GivreClone.UiTests/GivreClone.UiTests.csproj -c Release
lua-language-server --version
```

Diagnostics : 10.0.750501 epingle dans `.config/dotnet-tools.json`.
Une collecte EventPipe reelle sur le banc ForgeUiStress isole a produit
6374918 octets, code 0. Ne jamais choisir le PID d'un compte en jeu pour
reproduire ce smoke test. Un dump contient la memoire du processus :
ces fichiers sont des preuves locales, pas du code a committer.

Headless : Avalonia 12.1.1, xUnit v3 isole, trois tests verts sur la vraie
SmithMagicView vide, captures Skia 1440 et 760 pixels. Voir
`HEADLESS_SETUP_20261030.md` pour contrats et limites.
La suite historique xUnit v2 reste a 3238 PASS, 2 SKIP preexistants.

LuaLS : 3.19.2 Windows x64 dans
`%LOCALAPPDATA%/Programs/LuaLS/3.19.2`, archive officielle SHA256 verifiee.
Lanceur dans `%USERPROFILE%/.local/bin/lua-language-server.cmd`.
`.luarc.json` configure Lua 5.2 pour MoonSharp ; aucune regle de diagnostic
n'est desactivee. Les globals propres a Givre ne disposent pas encore de
stubs semantiques complets ; ne pas assimiler cela a une validation de l'API.

La commande CLI et l'echange LSP initialize/didOpen/didChange/shutdown/exit
ont ete verifies : erreur detectee, correction sans diagnostic, sortie 0.
Controle apres relance : la version OmO 5.1.51 lit
`%USERPROFILE%/.omo/lsp-client.json` (repli `.pi/lsp-client.json`), pas
le chemin Codex indique par son skill. Le fichier utilisateur OmO declare
maintenant le chemin absolu de LuaLS. La configuration est prise en compte
sans nouvelle relance.

PASS du vrai moteur : `lsp_diagnostics` sur
`scripts/check-craft-plan-readonly.lua`, `severity=all`, retourne
`No diagnostics found`. Aucun script de jeu n'a ete execute pour ce controle.

Preuves, echecs corriges et nettoyage : `DEVTOOLS_INSTALL_NOTEPAD_20261030.md`.
