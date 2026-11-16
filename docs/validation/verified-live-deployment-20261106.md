# Verified live build replacement

Full validation receipt companion-owner-restored-full.trx:4809 passed,
zero failed,2 existing skips,4811 total,3m45s,exit0.

Parent verified both connected characters were outside combat and all five
roster entries had scriptRunning=false before graceful replacement. Old
PID36248 matched the expected origin-live executable. Initial restart guard
rejected slash-style differences before closing anything; normalization with
Path.GetFullPath corrected that comparison without removing identity checks.

bash_248 completed exit0: new PID7132, executable
%TEMP%/givre-caster-side-effect-20261106/bin/GivreClone.App/release/GivreClone.exe,
SHA256 C9BE6103D1D1F7C63C77063890B2B5F0DA9597D3D50494CFF22D0C240C828FB4.
Profile givre-app-offline,pipe Givre-live-parent-20261105 preserved.
QA PID12692 was not closed. WaitForInputIdle preceded window creation;
initial pipe read returned ENOENT, so readiness was not assumed. Native
window watch subsequently confirmed handle6751846 and list_bots returned
the same five account IDs,all offline,no scripts running.

New transport journal:logs/transport-2026-11-06_08-03-26.log.
Standing monitor mon_6MHCDD0XW1CCT4AX was armed before reconnecting.
connect_bot accepted Sylvanbot and Bastionbot on Mikhal with exact account IDs.
Accepted requests do not establish full in-game readiness. No Dakal account
was connected and no saved farm script was automatically resumed.

Fresh list_bots confirmed both characters fully ingame at full HP with scripts
stopped. Virtual PIDs reversed after restart:Sylvanbot1073741825,Bastionbot1073741827;
all subsequent requests use account IDs rather than assuming old PID identity.

Parent launched inspected minerBase+minerBound for Sylvanbot:stop at twelve move
callbacks or first completed fight. Fresh state confirmed actual scriptRunning
and movement;baseline3953pods,111380kamas,2429/2429HP. Monitor then confirmed
one real harvested map188484100 (other early maps yielded nothing).

For Bastionbot,parent inspected the full saved multi-profession script:it enables
automatic fight/spells but defines no custom fight turn handler. It also has
commerce operations and is not resumed for this acceptance. Instead the
previously inspected noncommercial Frigost harvest source plus tracked
bastionbot-bounded-harvest-20261106.lua was accepted:three move callbacks maximum
or first completed fight,retaining source defeat and regeneration handling.
Baseline3227pods,3545733kamas,3063/3063HP. Acceptance is not completion.

Bastionbot's bounded run completed at08:12:38.242 with exactly three passages:
maps54168355/54169377/54169888 yielded0/1/0 harvests. Parent readback confirmed
scriptRunning=false,outside combat,on54169888(-72,-32),3063/3063HP,
3229pods and3544753kamas. Delta:+2pods,-980kamas. No protector occurred;
this validates bounded harvesting/travel/stop on the deployed build,not combat
fixes,profitability or the broader Pandala farming objective.
Sylvanbot continues the independently bounded twelve-passage run;no final receipt yet.

Sylvanbot subsequently stopped at08:16:45.674:console cursor182 confirms
ACCEPTANCE SYLVANBOT arret borne passages=12,cursor183 script termine.
Fresh get_bot_state confirms scriptRunning=false,outside combat,map104860169,
2429/2429HP,4494pods,110520kamas. Relative to baseline:+541pods,-860kamas.
No protector occurred. This proves twelve-passage travel/harvest/stop on the
deployed build,not profitability,bank-at-capacity or full miner1-200 coverage.
