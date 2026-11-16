# User-controlled challenge abandonment

The user chooses the per-character ChallengeAbandonHp percentage. Zero disables
abandonment. Observed HP strictly below a nonzero threshold drops the shared
challenge plan for the remainder of the fight, including after healing. The
latch resets with the fight state. This releases modeled movement, target,
end-cell, element, spell-limit and geometric challenge restrictions together.
It does not send a fictitious challenge-failure event: the server still decides
whether the subsequent actions fail the challenge.

Source checks: LaunchService.Combat.Turn.cs applies the threshold at turn setup
and before each planner decision. Crossing the threshold invalidates a cached
continuation. LaunchService.Combat.Challenges.cs handles the shared plan without
dispatching on a challenge name, so Statue, Zombie and Nomade use the same gate.

Causal red: retaining the plan below the threshold instead of returning null
made one of 18 ChallengeTests fail. Implementation restored afterward.
Green receipts: challenge-hp-full.trx: 4836 passed, 0 failed, 2 skipped;
challenge-hp-ui.trx: 218 passed, 0 failed. These runs preceded the final removal
of implicit catalog refusals and do not validate that later change.

Final catalog adjustment: DefaultAccepted accepts every challenge, preserving
explicit profile exclusions. Support classifications and likelihood estimates
remain unchanged; accepting an unsupported challenge does not model its rules.
Full main-suite verification of this adjustment passed: 4836 passed, 0 failed,
2 skipped, receipt challenge-hp-all-accepted.trx. Deployed PID32616, DLL SHA256
192C5C956D868EDBEEE057BD4AEEC91E9F7D705CAFAF0A8EA6ABE1D5183420B8.
Reconnection requests accepted for Bastionbot/Sylvanbot on Mikhal and Shadowbot on Dakal.
Native UI window7931500 confirmed Bastionbot selected, Combat tab, localized
threshold spinner enabled and on-screen at1555,980,124,36. Its existing value
was20; the parent did not type or change that value. This is preserved profile
state, not a new default. Expedition restarted through its native Start button:
23 maps, groups1-3, gather/bank/HDV. Control confirmed gathering, scriptRunning
true on205520903. Live threshold crossing remains pending; monitor bash_319
watches the new12-43-19 journal for abandonment and errors.

Follow-up source inspection found two older policies not covered by the first
threshold regression: Statue dropped NoMove after two blocked turns, and
Zombie/Nomade set the shared abandonment latch after three idle turns. Those
paths violated the user's choice even above the HP threshold. Removed their
automatic counters, calls and expensive Statue-only approach probe; kept
Statue's original-cell restoration and the common HP gate. Full validation is
running separately as bash_320 (challenge-user-only-full.trx). This follow-up
passed: 4836 succeeded, 0 failed, 2 skipped, exit0. Deployed follow-up PID18080,
DLL SHA256 B4EC966923C7C55FA0C4B7FFF5377CC8FBE16EA1B735AC5467AAB11C671D20A3.
Bastionbot, Sylvanbot and Shadowbot reconnect requests accepted on their original servers.
Native window8128494 confirmed Bastionbot selected and restarted her Expedition
with unchanged23-map/groups1-3/gather/bank/HDV settings. Control confirmed an
active fight and scriptRunning on205521414. Monitor bash_322 now watches the
12-58-33 journal for natural threshold abandonment. No live HP crossing proved
yet; do not mark the overall threshold task complete until stronger evidence.

Diagnostics limitation: csharp-ls crashes while enumerating the unrelated
artifacts/kourial-tools/pypdf directory (UnauthorizedAccessException). Compiler
and test results are available; language-server diagnostics are not.

Follow-up whole-file read of the same12-58-33 transport journal scanned78324
lines and found zero occurrences of the actual abandonment log signature
`défi abandonné : PV`. This does not prove the HP threshold was crossed or
that the policy failed: no natural crossing is established. No profile value,
account activity or HP was changed to manufacture the event. Live acceptance
and the overall threshold task remain open.
