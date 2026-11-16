# Installed Sram invisibility immediate utility

The parent reviewed the test-only delivery from st_01a1aecc in
SpellModifierRealDataTests.cs. The theory loads spell12913 at grades1/2/3
through the actual GameNameResolver and application-assembly Combat types.
Strict learned rotation must include it. A synthetic three-cell corridor and
range-one attack isolate its immediate MP utility: starting with zero MP,
the plan must cast12913 on self, walk one cell, then attack for ten damage.

The same installed descriptor is resolved directly to require self MP gains
1/2/2, action128 points events and action150 invisibility. The test does not
copy the two effect rows into its fixture. Existing hand-authored dispatch
tests cover the packet adapter separately; this test owns the installed-data
conversion boundary. No production behavior was changed for this increment.

Independent Release compilation/test bash_190 completed with exit0: all three
grade cases passed, zero failed or skipped, 39.5 seconds reported test duration.
The build reported 256 warnings, not suppressed. This repository's installed-data tests return early when the
client directory is absent, so an executed test count alone is insufficient
on a machine without the client. The installed client path was present in
the preceding real-data investigations on this workstation. The parent also
checked it during this run: StreamingAssets/version reports3.7.13.17 and
Content/Data contains data_assets_spelllevelsdataroot.asset.bundle.

The parent independently checked that GameNameResolver retains globalCooldown
as descriptor uncertainty rather than dropping the descriptor. IsAdmitted
allows modeled immediate rows with empty state criteria, so this test does
not assert that global cooldown itself is simulated or coordinated.

No live cast, cooldown reset, invisibility expiry, reveal-on-attack or
defensive invisibility scoring is established by this test. The broad Sram
mechanic task remains open.

Combined installed-data and Sram dispatch verification bash_191 completed
with exit0: 19 passed, zero failed or skipped, 95.2 seconds. This includes
the three installed grades and the existing manual-restriction/productive-MP
adapter controls. It is not a full-suite result or live dispatch acceptance.
