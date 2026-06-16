# Changelog

## 1.0.0

First release.

- Drink triggers for deaths, revives, tumbles, head-grabs, smashed valuables, enemy spawns, extractions and team wipes.
- Four presets (Casual, Standard, Last Resort, Custom) with every rule individually tunable.
- Host decides every drink: only the host's config evaluates the rules, then it broadcasts the resolved call to the room and clients render it. No ruleset is serialized.
- Drink counts on names: every Semibot's floating nametag shows their running tally, like "Bilbo (12 sips)", visible to the whole lobby.
- The Taxman runs the tab on the truck terminal: emoji reactions, a per-Semibot recap, and a results-screen crown for the heaviest tab.
- Drunk terminal: the whole truck screen slurs as the team drinks, building up over a run and sobering off between levels. Never touches haul or quota numbers.
- Configurable unit word for dry play.
- Vanilla-style startup warning that escalates after several days of use. On by default, toggleable.
- Dev-console test commands (`pd ...`) behind a config flag for solo testing.
