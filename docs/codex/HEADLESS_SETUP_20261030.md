# Isolated Avalonia headless UI tests - 2026-10-30

## Scope and isolation

`GivreClone.UiTests/GivreClone.UiTests.csproj` references the real App project
(`GivreClone` assembly). It instantiates the compiled, native
`GivreClone.App.Views.SmithMagicView` AXAML and code-behind, not a copied view or
a stock-control substitute.

The test-only `TestApp` loads Fluent, the production `Theme.axaml`,
`Icons.axaml`, `Controls.axaml`, and Amber accent resources. It deliberately does
not invoke `Program.Main`, production `App.OnFrameworkInitializationCompleted`,
`MainViewModel`, `SmithMagicViewModel`, account/configuration stores, transport,
or any gameplay action. DataContext remains null: these tests cover the real
empty workshop's layout, rendering, and responsive code-behind, not populated
game data, commands, or live bindings.

This is a separate xUnit v3 executable test project. The existing
`GivreClone.Tests` xUnit 2 project is unchanged; no solution or global
configuration changes are required.

## Pinned packages

| Package | Version |
| --- | --- |
| Avalonia.Headless.XUnit | 12.1.1 |
| xunit.v3 | 3.2.2 |
| Microsoft.NET.Test.Sdk | 18.0.1 |
| xunit.runner.visualstudio | 3.1.5 |

The real App provides Avalonia, Desktop, Fluent, and Inter at 12.1.1.
Headless.XUnit 12.1.1 uses xUnit v3 extensibility; the xUnit/Test SDK/adapter
versions above match Avalonia's 12.1.1 dependency manifest.

## Commands

Run from the worktree root with .NET 10:

```powershell
dotnet restore GivreClone.UiTests/GivreClone.UiTests.csproj -m:1 --disable-parallel
dotnet build GivreClone.UiTests/GivreClone.UiTests.csproj --no-restore -m:1
dotnet test GivreClone.UiTests/GivreClone.UiTests.csproj --no-build --no-restore -m:1 --logger "console;verbosity=normal"
```

The App reference retains its normal build dependencies and content-copy
requirements, including sibling GivreLauncher/GivreProxy projects and
native runtime payloads. Headless testing does not replace or bypass those
build contracts.

## Test contracts

- 1440 x 900 and 760 x 900: actual workshop layout selects the correct host,
  history width, and inventory column; the active inventory fits horizontally;
  visible explicit scroll viewports stay bounded and disable horizontal
  scrolling.
- Both widths use real Skia rendering (`UseHeadlessDrawing = false`), capture a
  frame at the requested pixel size, and require multiple rendered colors.
- Resize 1440 -> 760 -> 1440 -> 760: the very same inventory control moves
  between hosts without duplicated parents or lost search state; expanding
  while the compact inventory tab is selected restores the object tab.

Dispatcher jobs are flushed synchronously with `Dispatcher.UIThread.RunJobs()`;
capture drives rendering through the headless API. There are no sleeps,
polling delays, network calls, accounts, or game actions. Windows close in
`finally`, and xUnit parallelization is disabled.

Rendered PNGs are written under the test output's `TestResults` directory as
`smith-magic-1440.png` and `smith-magic-760.png`. They are diagnostic captures,
not approved pixel baselines.

## Documentation consulted

- https://docs.avaloniaui.net/docs/testing/headless-xunit
- https://docs.avaloniaui.net/docs/testing/setting-up-the-headless-platform
- https://raw.githubusercontent.com/AvaloniaUI/Avalonia/12.1.1/src/Headless/Avalonia.Headless.XUnit/Avalonia.Headless.XUnit.csproj
- https://raw.githubusercontent.com/AvaloniaUI/Avalonia/12.1.1/Directory.Packages.props

Context7 was requested through the tool catalog but is unavailable in this
child session. The primary documentation and pinned upstream source above
were consulted directly instead.

## Verification limits

Verification parent effectuee :
- `dotnet test GivreClone.UiTests/GivreClone.UiTests.csproj -c Release
  --no-restore -m:1 --logger "console;verbosity=normal"` : code 0, 3 tests
  reussis, aucun ignore.
- Captures 1440x900 et 760x900 ouvertes et inspectees : vue non vide,
  inventaire dans la colonne large puis dans l'onglet compact.
- La premiere compilation a detecte un ResourceInclude invalide pour Amber
  (AVLN2000). Changer seulement la casse ne le corrigeait pas : les themes
  de production sont des dictionnaires x:Class charges par type, comme
  l'indique `GivreClone.App/Themes/ThemeDictionaries.cs`. Le TestApp utilise
  maintenant `<themes:Amber />`, sans modifier l'application.

C# LSP diagnostics were attempted before build. The tool reported
`LSP server 'csharp' for .cs is NOT INSTALLED` / `Command not found: csharp-ls`.
No language server or unrelated tool was installed.

This suite does not prove production startup, OS window chrome, accessibility
screen-reader behavior, populated inventory/history layout, service-backed
bindings, command effects, desktop DPI scaling, NativeAOT, or gameplay.
