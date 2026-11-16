# Character switching: live findings (2026-11-03)

- A packet-injected `ReloginRequest` is insufficient for a reliable hot switch: although the server returned a `ReloginTokenEvent`, the client did not act on a relogin it had not initiated. Cutting the connection returned the still-open client to the title screen, but injected character selection could leave it on a blank screen.
- The game's native **Changer de personnage** flow does work in the same client when the UI control is hovered before clicking. Unity needs the hover before the click; missing that step had caused a false failure. The anti-inactivity hook was not the cause and remained enabled.
- The bot's character-switch button was live-tested successfully, but it still relaunches the Dofus process. In the reported test the new process entered the selected character in about 40 seconds; the in-place switch requested by the user was therefore not achieved by the bot button.
- A prior automated relaunch attempt had stalled during loading; a fix made native-mod unloading wait until the map arrived. The later successful live switch was evidence the loading path worked in that test, not proof every relaunch case is solved.
- At the end of the captured work, a full test suite was running; its result and the planned follow-up game tests were not yet known.
