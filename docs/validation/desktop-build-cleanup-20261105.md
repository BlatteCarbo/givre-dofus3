# Desktop build cleanup - 2026-11-05

User explicitly requested freeing disk space and removing Desktop/other builds.
Scope executed: binary files in 46 previously inspected GivreClone-* build
directories under %USERPROFILE%/Desktop, dated October 23-29. No directory outside
these 46 was deleted in this cleanup.

Removed 4,701 files totaling 28,018,836,736 logical bytes: root DLL/EXE/PDB
outputs (2,526,327,584 bytes) and native DLL/EXE/PDB/SO/DYLIB dependencies
under their runtimes subdirectories (25,492,509,152 bytes). No links were
followed. Data, WebAuth, documents, configuration JSON and other directories
were retained; these old Desktop copies are no longer runnable builds.

Verification: all 4,701 planned file paths are absent. All 46 data directories
remain at their original individual byte totals, totaling 82,051,164 bytes.
This is size verification, not a content hash comparison; no operation targeted
those directories. Sources, reports, screenshots and current build were not
touched. No process was terminated.

C: free space before: 394,760,192 bytes. After: 28,404,805,632 bytes.
Observed free-space increase: 28,010,045,440 bytes (28.01 decimal GB).
Other processes may write concurrently, so free-space delta differs slightly
from the sum of logical file lengths.

The running GivreClone process PID 14216 was verified before and after at
%USERPROFILE%/AppData/Local/Temp/givre-journal-final-20261105/bin/GivreClone.App/release/GivreClone.exe.
Its output directory was preserved. No further broad temporary cleanup was
needed after this recovery. Earlier cleanup of 2.48 GB in parent-owned Temp
builds is separate and is not included in these figures.
