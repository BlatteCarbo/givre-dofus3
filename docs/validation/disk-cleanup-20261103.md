# Owned temporary build cleanup

The user requested disk space recovery on 2026-11-03. C: initially reported
6,698,512,384 bytes free (bash_782). No GivreClone process was observed;
the isolated Jondo client remained active and was preserved.

Deleted only these inspected, generated temporary build outputs:

- Temp/givre-live-20261102/out2 through out21: twenty superseded live builds.
  out22, mcp, bin, obj and other evidence remained.
- Temp/givre-recovery-combat-artifacts
- Temp/givre-recovery-primary-artifacts
- Temp/givre-recovery-spell-probe-artifacts
- Temp/givre-recovery-tactical-artifacts
- Temp/givre-resolver-validation

The five artifact roots contained only bin/obj directories. Each deletion
checked that no active executable resided underneath and rejected reparse
points. bash_787 and bash_789 exited 0. Sources, repositories, journal files,
user data, official client and Jondo laboratory were not removed.

Final observation bash_790 exited 0: 8,347,951,104 bytes free; out22/GivreClone.exe
still exists. Net observed recovery: 1,649,438,720 bytes (1.65 GB decimal).
Free-space changes include any concurrent machine activity.

Read-only scan also found Downloads/givre-GivreActive contains only Videos,
17,426,869,660 bytes. A question is pending before any deletion of those videos.
Downloads/givrework (13.42 GB) and the active Jondo laboratory (14.87 GB) were
retained; size alone is not grounds to delete source or active installations.

## Authorized .t3 obsolete build cleanup

After the read-only inventory, the user explicitly requested cleanup.
The bounded script Temp/givre-t3-owned-build-cleanup-20261103.ps1 inspected
234 bin/obj directories directly under named Givre projects in old
worktrees. It excluded fix-f01-f16-final-20261024, combat-planner-slice1,
GivreLauncher and GivreProxy, all artifacts and source trees. Git
ls-files was read-only for each candidate worktree; no tracked candidate
files were found. Process executable paths and command lines matched none
of the targets. Reparse points and recognizable user-data files/directories
would cause exclusion. Inspection bash_826 exited 0 with no skipped targets
and 24,238,610,825 logical bytes selected.

Deletion bash_827 re-ran those checks, removed all 234 validated directories
and exited 0. C: changed from 8,275,980,288 to 32,534,560,768 bytes free:
observed recovery 24,258,580,480 bytes (24.26 GB decimal), including any
concurrent machine activity. Source worktrees, Git metadata, T3 state and
secrets, reverse/validation artifacts, active builds, clients and Videos
were outside the deletion scope. No Git commit or history change occurred.
