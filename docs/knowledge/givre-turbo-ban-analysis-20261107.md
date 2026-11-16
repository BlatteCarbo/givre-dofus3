# Turbo bans / server cuts - analysis 2026-11-07

Evidence comes from 1042 fights in the transport logs of 2026-11-06 and 2026-11-07.

## The two failure classes

**1. Socket Turbo, long fights: a 5-minute ban.**
- Every fight that reached 30 turns or more was banned, 4 out of 4 (32, 41, 53 and 58 turns).
- The server closes the session at FightEndEvent. Reconnecting then fails with `IDENTIFICATION REFUSÉE : BANNED (ban_end=…)`.
- No fight that escaped a ban went past 25 turns.
- Turn rate is not the cause on its own. Socket ran up to 60 turns per minute without any cut.

**2. MITM Turbo, with the bot injecting its own acks and "tour prêt" relances.**
- The server closed the game connection 50 ms after FightEndEvent, twice: 12:34:43 and 12:46:43 (Shadowbot).
- These were 2 cuts out of the only 12 fights with injection. The 1030 fights without injection had 0 cuts.
- Those 12 fights carried 50 to 293 bot acks each, plus 10 to 64 ready relances (`TurboReadyBurst` resends the ready frame 20 times, every 100 ms). The visible client also sends its own ready and acks, so the server receives duplicates.

## Fix direction (keep the Turbo speed)
- **Socket:** a TurboPacing guard. Human pace kicks in only after 25 of my turns in one fight (configurable; 0 disables it). The 10-minute and 1-minute limits exist but default to 0.
- **MITM:** send each ready and each ack only once upstream; drop the client's duplicate; remove the 20×100 ms relance burst.
- **Status:** not yet validated live. Re-check with a log count after deploy: cuts per fight with injection.
