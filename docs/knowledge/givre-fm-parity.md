# Givre FM and reference parity

Session context recorded 2026-10-30; this note is context, not authorization for future consequential actions.

- User corrected the initial manual-workshop interpretation: launching FM should automatically travel to a compatible workshop, like HDV. The user chooses the objective, not the workshop.
- Confirmed on 2026-10-30: FM objectives are profession leveling, target rolls, or exo. Leveling means the user picks the item and a target profession level; the bot travels, passes runes, and stops when the in-game profession reaches that level or when runes or budget are exhausted. Rolls and exos stay. Runes are not passed unless the character is at the workshop and in the FM interface. Each profession takes its own items: jewelmagus for amulets and rings, cordomagus for belts.
- Mizanbot 3.6 is installed by the user. They asked for its source or 1:1 features by any means, including installing and downloading tools. Do not copy decompiled source into this repo or bypass its anti-tamper; reimplement observed behavior in GivreClone.
- User requested full reference-product parity: UI, features and accessible source/decompiled logic, and invited useful suggestions to the reverse-engineering worker. Keep extracted source provenance distinct from inferred reconstruction.
- Current project worktree: %USERPROFILE%/.t3/worktrees/GivreWork/fix-f01-f16-final-20261024. Live verification journal: docs/codex/LIVE_TEST_20261030.md.


## Cursor runtime verified on 2026-10-30

Cursor Agent is installed at `%USERPROFILE%/AppData/Local/cursor-agent/cursor-agent.cmd`, version `2026.10.22-27dcf5f`; `status` returned logged in. It was absent from this session's PATH. The CLI supports `--print`, `--mode ask` (read-only), `--workspace` and `--output-format`. Use the explicit path rather than assume it is unavailable. This is environment evidence, not authorization to start future consequential work.

## Grill answers locked 2026-10-30

User words: Q14–Q22 and Q24–Q32 yes. Q23 modified: rune buying stays available while leveling a profession, and bank runes can be used from the forgemagie workshop without carrying them, like selling bank stock from HDV. Quality bar from the same message: improve the accepted work and make it better than the current bot.

- An item above the profession level is skipped, logged, and the next item is tried. If none remain forgeable, FM stops with no rune passed.
- Crushing breaks only the chosen list, never the whole bag. Search and multi-select fill both the FM queue and the crush list.
- Search filters name, level, and type immediately, case-insensitive. Shift-click selects a range. Select-all takes only visible allowed rows. Add keeps display order. Enter adds the single remaining allowed row.
- A forbidden row stays visible, greyed, with the reason. FM: wrong profession, or item level above profession. Crush: equipped item, rune, or resource. One row is one object.
- Crush has its own button, does not start after a sale, travels to the crusher, and breaks nothing until the crush interface is open. Resulting runes stay in the bag.
- A missing or failed crush item is logged and the next item is tried. Removing a row or clearing the list breaks nothing. Changing character clears the crush list.
- FM has two exclusive modes, Montée de métier and Objectifs, over the same queue. Switching mode does not clear the queue. In leveling, existing target lines stay visible but do not choose the rune.
- The leveling profession is chosen among professions represented in the queue, or taken automatically when there is only one. Other items stay queued, are logged, and are skipped for that session.
- If the server has not announced the profession level, or it is already at or above the target, no rune is passed and no travel starts.
- Leveling stays on the first forgeable item of that profession. If it leaves the bag, the next item of the same profession takes over. A missing workshop or failed travel stops the session and keeps the queue.
- A crush row is one bag UID. A stack is crushed in full. Only unequipped equipment is allowed. The selector reads the displayed character's bag.
- The nearest known crusher is used, in list order, while the interface stays open. Travel failure or a closed interface stops the run and leaves the remaining list.
- An item already queued for FM or crush stays visible and greyed. After Add, the window stays open, checks clear, and the search remains.
- Before leveling, the sale pass is the existing display fix plus one live cycle: buy one item at 20 000 kamas or less, list it, reprice once, then remove it.
- Leveling and crush follow these decisions. A Mizan behavior that is not recovered yet stays marked unrecovered and is not part of this pass.
- Q23 mechanism is not settled yet: which bank, direct use, and when buying turns on.

## Socket prices and live forge validation (2026-10-30)

- The user clarified that Socket mode should receive server average prices. The live Socket connection did receive the approximately 81 KB `ObjectAveragePricesEvent`; the generic decoder rejected its unusually large frame because of a 16,384-field limit. A fallback decoder was added and tested against a 7,000-price table.
- During live FM testing, automatic travel reached the forge workshop interior and approaching the machine opened the exchange craft interface. Auto-buying runes then failed because Socket mode had no average-price data. The fallback purchase plan is to travel to the rune HDV, read the local price, and buy within the configured item cap and total budget.
- The user requested buying missing runes automatically and clarified that Socket should receive average prices; treat server average-price acquisition as desired behavior, not as a reason to bypass the configured purchase safeguards.

## Implemente 2026-10-30

Q33 a Q35 ne sont plus ouvertes. La rune du sac est prise en premier, puis une rune de la banque deja lue, dans le meme passage d'atelier, sans detour. L'achat reste une case, et ne se lance que si sac et banque sont a zero. Tests Release Smith : 32 reussites, 0 echec. La fenetre Debug deja lancee ne contient pas ce build.

## Réponses utilisateur sur objets liés — 2026-10-30

- Les objets liés au compte/personnage, comme les Intrépides cités par l'utilisateur, ne sont ni FMables ni vendables : les exclure des candidats FM et des ventes.
- Brisage d'objets liés : jamais.
- Banque liée : seuls les objets liés au compte peuvent être stockés/utilisés depuis la banque ; ne pas étendre cela aux objets liés au personnage.
- La date FM n'est pas certaine ; l'utilisateur pense que la FM est autorisée malgré la date. Traiter comme une hypothèse à confirmer, pas comme une règle établie.
- L'utilisateur invite à poser d'autres questions de clarification sur le jeu pour améliorer le bot, au-delà de la FM.
