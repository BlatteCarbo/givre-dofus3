# Ordered caster positions in compound displacement

## Scope

The planner checked every push/pull alignment against the caster position
before the entire cast. The resolver executes effects in order and already
updates that position after a swap. A supported compound swap/pull can therefore
be rejected despite a valid melee follow-up. This is a synthetic descriptor
contract, not proof of a particular learned class spell or official-server cast.

Parent read the actual planner validation and neighboring separate-cast
displacement tests after read-only investigation st_01a1ad69. The regression
uses the existing planner boundary, no new production seam: caster at (8,0),
ally at (10,0), enemy at (10,3); swap then pull two cells, then a range-one
attack. Expected actions are setup spell 20 at the ally followed by attack 10
at the enemy's changed cell (10,1), for 40 damage.

## Evidence chronology

- First reproduction used invalid map coordinates and failed with duplicate
  cell -1. That fixture error is not evidence of the production defect.
- Corrected valid-grid reproduction mon_16RHT1KSZXHMBNKM / bash_68,
  compound-movement-valid-red.trx: one failed, exit 1, 4.0 seconds. Expected
  action IDs [20,10], actual empty. Parent read the actual failing TRX.
- Minimal correction tracks caster position through ordered movement events,
  updating it only for movements targeting self. Existing blocked-movement,
  mobility-policy and forced-movement safety gates remain unchanged.
- Related planner/resolver verification mon_MVMWMNHRPKHZ1XZQ / bash_69 passed
  235/235, zero failed/skipped, exit 0, 32.0 seconds,
  compound-movement-green.trx. Existing compiler/analyzer warnings remain.

No live fight was run. Broad forced-movement and class-mechanics tasks remain
open. C# LSP still crashes while scanning the inaccessible pypdf artifact
directory; compiler and tests are the available diagnostics.

## Integrated verification

The first integrated run (route-compound-integrated.trx) had 4763 passed,
one failed and two existing skipped cases. The sole failure was the Lua API
inventory source hash made stale by the travel correction. Only that
sourceHashes entry was updated from the complete source bytes; no API entries
or tests were changed. The subsequent freshly built run
mon_TS1WW2F0CCBCED4D / bash_71 passed 4764/4766, zero failed, two existing
skips, 156.1 seconds, exit 0 (route-compound-final.trx). This covers the current
travel and compound-movement changes together, not live server acceptance.

## Delegated caster follow-up

Parent independently read action 792's delegated-caster branch. The new
Delegated_push_checks_alignment_from_the_actual_subspell_caster regression
uses an allied delegate aligned with the enemy while self is not aligned.
Before correction, delegated-push-red.trx failed with an empty action list.
The planner now tracks positions by actor ID through the ordered movements,
checking each push/pull against its actual CasterId. Related verification
mon_6NMERB5DVKRGKP9Y / bash_73 passed 236/236, zero failed/skipped, 19.3 seconds,
exit 0, delegated-push-green.trx. The earlier integrated result predates this
follow-up. No unsupported class spell is inferred from this synthetic case.

## Official-client preparation

The current Release app build succeeded (mon_31HNGEZQ35R52WR9, exit 0).
App PID8212 uses the existing givre-app-offline profile and explicit pipe
Givre-live-parent-20261105. Only ArcherbotLeRetour was launched in MITM;
the official client PID8664 reports version 3.7.13.17. Other accounts remained
offline. Initial launch response timed out, but independent account/process
observations confirmed launch; no duplicate command was sent.

Unity exposes no internal AX controls and screenshot pixels are unavailable
to the parent model. Read-only image text extraction confirms the actual
server-selection screen, with Mikhal and Dakal listed. This is not in-game
combat evidence. The user was asked to select Mikhal and Bastionbot; no guessed
coordinates or blind keyboard activation were sent. Combat validation awaits
entry into the world.
