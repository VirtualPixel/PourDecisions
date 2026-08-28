# Changelog

## 1.0.1

- The wipe drink never actually fired. It was hanging off a message the host never
  receives, so "finish 'em" has been dead since launch.
- Solo runs never cleared the tab. A singleplayer wipe drops you into a fresh level
  without passing through the lobby, so the old run's drinks, first blood and results
  splash all carried over until you quit the game.
- The Taxman's emoji reactions were gated to the truck, where drinks never happen, so
  they could not fire at all. He reacts on the terminal in the level now.
- A late joiner's tab is now replaced by the host's, not merged into it. Anyone left over
  from an earlier room used to sit on the leaderboard and count toward how drunk the
  terminal got.
- Crown lines fire for every arena now, not just the first one of the session.
- Prompt time in the settings does something. It was bound and never read.
- Names with a space in them come back out of the save ledger readable.
- Fewer allocations on the nametags, and no more dead references to the truck screen or
  the glitch effect across a scene change.
- Internal messages are renamed so they cannot collide with another mod's. Everyone in
  the lobby needs to be on 1.0.1.

## 1.0.0

First release.

- Drinks for the usual disasters: dying, getting knocked down, getting head-grabbed, smashing valuables, enemies waking up, extractions, and full-crew wipes.
- Shoot or kill a teammate and you pay for it: hitting one of your own is a round, actually killing one makes you finish your drink.
- Drink counts sit right on everyone's name, like "Bilbo (12 sips)", with what they still owe in red when they're falling behind. The whole lobby sees the same numbers.
- The Taxman keeps the tab back at the truck: emoji reactions, a recap of who owes what, and once a run he pulls the crew's all-time death count off everyone's machine for a body-count standings.
- Drunk Semibots start running their mouths on screen, getting worse the more they drink, right down to ranting about whatever monster keeps killing them this run.
- The truck screen slurs and glitches along with the crew as the tab climbs. Never touches the haul or quota numbers.
- Four presets (Casual, Standard, Last Resort, Custom), every rule tunable on its own.
- Host decides every drink: only the host's config evaluates the rules, then it broadcasts the call. No ruleset is synced.
- Configurable unit word so you can play dry (dare, push-up, whatever you want).
- Drink-responsibly notice on startup, picked at random and leaning harder after a few days running. On by default, toggleable.
- Dev-console test commands (pd ...) behind a config flag.
