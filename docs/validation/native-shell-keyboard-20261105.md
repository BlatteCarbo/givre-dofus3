# Current native shell keyboard observation

The parent launched the freshly compiled managed application from
%USERPROFILE%/AppData/Local/Temp/givre-parent-observed-grade-20261105/bin/GivreClone.App/debug/GivreClone.exe
with GIVRE_CLONE_RUN_NAME=parent-compact-20261105. The native window is
5375934, process 21408, title GivreClone v1.3.3.11, at 1460 by 920 pixels.
No account was added or connected. This is the current build, not the older
window previously inspected in this workstream.

Native observation established a French language choice, the shell with zero
bots, and opening quick search. The parent entered Expedition using the native
keyboard, pressed Enter and observed quick search closed and Back enabled.
The page body was deeper than the bounded AX tree, so the destination's exact
controls were not certified. Ctrl+K followed by Escape was also exercised;
the final focusedElement returned the window, not an actionable control.
This does not prove focus restoration to the invoking button.

AX continues exposing language and other modal descendants after entry to the
shell. A French button returned IsOffscreen=false even after selecting French.
That property alone does not establish visibility, Tab eligibility or activation
of closed descendants. The parent read OnPickLanguage, EnterApp and the quick
navigation key handler; a focused headless investigation is pending under
st_01a1abf0. Do not change modal gates on AX exposure alone.

The screenshot tool returned image omitted because the model does not support
images. No visual verdict, compact-width acceptance, complete keyboard tour,
focus-ring judgement or light/dark acceptance follows from this observation.
The window remains open in the isolated test profile for subsequent checks.

## Concrete native Tab failure

The parent subsequently exercised actual Tab input and read focusedElement
after every key, rather than inferring focus from the AX tree. From the shell,
eight Tabs reached the chrome buttons in order (Back through Close). The next
30 Tabs reached sidebar navigation, Mon compte, Reglages and Ouvrir les Comptes,
then the closed confirmation buttons Annuler/Confirmer, then the closed language
buttons Francais/English/Espanol. Subsequent Tabs reached closed Shield/account/
proxy text fields and buttons. These were focus transitions only; no hidden
confirmation, login, save or proxy action was activated.

This proves closed descendants participate in the real shell Tab order. It
supersedes the earlier uncertainty about keyboard eligibility, but does not
prove activation side effects or visual appearance. Same investigator
st_01a1abf0 received the trace and is addressing language/confirmation first.
Other closed overlays remain an observed separate scope; a narrow correction
must not be reported as complete shell keyboard acceptance.
