# Native forgemagie workshop layout - 2026-10-30

## Scope and visual system

Only `SmithMagicView.axaml`, its layout-only code-behind, and this note are owned by this change. The global palette and history batching are separate parent/child work.

Inspected the supplied 1067 x 618 reference image, shared `Theme.axaml` / `Controls.axaml`, and five sibling panels. Also read `DOFUS3_UI_REFERENCE_20261030.md`: the recovered header/history/stats/storage structure supports this native Avalonia composition. No game assets, fake rune slots, new data, dependencies, or game processes are introduced.

The view uses shared slate-purple surface/text/stroke resources, shared corner radii, and `WorkshopAccentBrush` / `WorkshopAccentTextBrush` for lime primary actions. Local density tokens define a 4/8/12 spacing scale, 11.5/12/13/18 type scale, and 32px controls. There are no local hexadecimal colors.

## Layout and control map

- Fixed top card: loaded object, reliquat, status, session summary, start, one recommended rune, and stop. Stop stays discoverable even while disabled.
- Left rail: bounded recent passages with aggregate counts and technical console under **Log**. History clear remains available.
- Center **Bilan**: persistent objective budget and measured sessions, with session refresh. Moved here on 2026-10-31 because the narrow rail made the accounting unreadable.
- Center **Objet**: aligned effect/max/current/objective/ceiling columns, row removal and progress; perfect-roll action; expandable custom/exo target editor.
- Center **Mode**: objectives / profession leveling, adaptive planner, job, target level, manual leveling rune and bank note.
- Center **Options**: per-session/per-line rune limits, session budget, smaller-rune policy, buying limits, and expandable purchase/fusion comparison.
- Right **Sac / File / Brisage / Runes**: existing search and Enter handler, click/shift-range selection, all selection/queue actions, load objectives/removal, crusher trial and continuation, rune stock refresh.

At view widths below 1040px the same inventory control is moved into the center **Inventaire** tab; it is not duplicated and its selection/bindings remain intact. History narrows from 176px to 152px. At wider widths inventory has a 264px independent right column. Returning to wide mode from a selected compact inventory tab restores **Objet** instead of leaving a hidden tab selected.

Every scroll viewer has a maximum: recent history/console 480px, sessions/effects/options/mode 520px, inventory/queues/rune stock 320px, custom target editor 192px. Finite parent grid constraints can shrink those viewports further. No log collection can increase the root grid height. Full status/history-note text remains available by tooltip when capped.

## Verification and parent rendering concerns

Source-level parity audit: all 24 original XAML commands are retained; no original simple binding is missing and no new VM property is required. Existing search Enter and pick/shift handlers remain unchanged.

The assigned child did not build, run tests, launch a native/game process, or render the changed view, per the shared-worktree instruction. AXAML diagnostics are unavailable (no server configured); C# diagnostics are unavailable (`csharp-ls` not installed). No language-server dependency was installed.

Parent must compile and render the real view with `tools/ForgeUiStress` before visual acceptance:

1. At the 960px native-window minimum and a wider desktop size, verify that the fixed action/session header remains visible with 10000 offline events, recent/session/log regions scroll internally, and no panel is pushed below the viewport.
2. Resize across the 1040px **view-width** breakpoint, including while **Inventaire** is selected. Verify the inventory survives reparenting, selection persists, and wide **Objet** becomes selected.
3. Check dense six-digit objective/ceiling editors, long French effect/item names, all three left-tab headers at compact width, and all four inventory tabs for clipping.
4. Exercise custom targets, leveling/adaptive fields, buying/comparison expanders, both queues, crusher trial continuation, Enter search and shift-range selection through the native surface.
5. Confirm lime button hover/disabled contrast under the parent theme and that grouped history text supplied by the batching child remains readable in the narrow rail. This view does not change event aggregation or the stored collections.

## Native controls and manual preselection - 2026-10-31

The existing shared slate palette now supplies Fluent expander, button and tab states.
The appearance card's equipment expander uses those same resources rather than a black
Fluent header; its equipment details scroll within the shared 192px detail viewport.
No look, equipment, pet or game asset is replaced by this presentation change.
The separate taskbar/reactivation freeze is owned by the parent's measured SQL/history
work; this headless presentation harness does not validate live-window reactivation.

In **Mode**, adaptive leveling now exposes the agreed preselection contract:

- **Choisir les supports** opens the existing **Sac** controls, including the compact
  inventory tab. Checked compatible non-rune `Picks` remain the support selection;
  no second support collection or new selection logic is introduced.
- **Limiter les supports a acheter** edits `AdaptiveAllowedSupportGids`:
  automatic (`null`), no support purchase (empty), or explicit purchase-only GIDs.
  This does not change which checked owned supports may be used.
- **Limiter les runes (stock et achats)** edits `AdaptiveAllowedRuneGids`;
  an empty explicit set authorizes no rune. Both fields accept comma-separated
  positive GIDs, including objects/runes absent from stock, with 32 IDs per field.
  Invalid manual input fails closed rather than enabling unrestricted purchases.
- The picker starts collapsed. Opening it resolves only explicit IDs against
  already-loaded item names; it never loads or enumerates the rune/item catalog.
  Preview scrolls are bounded to 192px. Stock changes do not erase explicit GIDs.
- Selection controls are disabled during a running session. The parent adaptive
  runner snapshots these selections at startup and applies the purchase restrictions.

Layout: unchanged workshop list-detail/scroll-body shell; activity, effect table,
inventory and mode remain bounded. Purchase/rune previews add only bounded internal
viewports inside the existing Mode region. The shared primary button
template is reused for **Demarrer**, retaining its lime foreground/background states.
No dependency, animation loop or game command is added.

`NativeThemeTests` loads the real production appearance card without a bot model.
`SmithAdaptiveSelectionTests` loads the production workshop and exercises its actual
GID-field events and pointer support navigation at 1440px and 760px, in the shipped dark
palette and a Fluent light host. The application itself still requests Dark and has
accent themes, not a separate user-facing light palette. Captures are written to
`GivreClone.UiTests/bin/Debug/net10.0/TestResults/`; final results must be recorded
after the current source has compiled and the images have actually been viewed.

### Verification parent - 31 octobre, lot performances

- Suite metier : 3369 PASS, 2 SKIP preexistants, aucun echec.
- Suite headless : 14 PASS, aucun echec ni ignore. Le test de minuterie
  Bots est rouge avant correction, vert apres detachement effectif.
- 62 captures fraiches des onglets et etats auto/vide/restreint, 1440 et
  760 pixels, palette sombre et hote Fluent clair. Toutes vues dans six
  planches ; equipement et selections compacte/large ouverts aussi a taille
  native. Pas de superposition ni debordement constate. Les panneaux vides
  correspondent aux fixtures sans session, pas a un rendu manquant.
- Le premier test equipement utilisait FindResource sur son ancien parent
  detache, donc UnsetValue. La fixture cherche maintenant la meme ressource
  sur la fenetre attachee ; l'assertion de couleur reste identique.
- Binaire Release compile exit0, processus live 22172. Fenetre restauree
  depuis reduction via UI Automation avec autorisation bureau active :
  acquittement13ms, appel MCP suivant3ms, capture complete sans corps vide.
  Cette mesure ne correspond pas a un demarrage a froid.
- Lecture MCP d'une ligne d'historique :327ms sur le binaire corrige,
  contre expiration15002ms avant. Mesures ponctuelles, pas une moyenne.
