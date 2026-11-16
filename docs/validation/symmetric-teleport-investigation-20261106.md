# Symmetric teleport report review

Parent compared st_01a1af70 against the current resolver. The reported snippet
uses target.Cell modulo14 and a different Move signature; actual code at
SpellResolver.cs453-459 uses MapPointOf, distinct moving/pivot selection for
1104/1105/1106, and Move(c,moving,action,destination,true).

Passing true does enable occupied-destination exchange. That fact alone does
not prove a bug. The report asserts all symmetric teleports must block on
occupancy, without runtime or primary-source evidence. This also affects the
very exchange behavior needed for evaluating Xelor Telefrags. No independent
server contract, recorded cast or trusted implementation was supplied to
justify replacing exchange with blocking for all three actions.

No teleport change or assertion encoding that unsupported assumption is made.
The class mechanics task remains open. Further work must establish semantics
per action and spell, including blocked terrain, occupied landing and state
transitions, before modifying sequential placement behavior.
