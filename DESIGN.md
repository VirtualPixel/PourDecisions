# Pour Decisions - design doc

> A host-run drinking game stitched into R.E.P.O.
> Bad calls, worse hangovers.

**Mode:** everyone in the lobby installs it. Host owns the rules; clients render the prompts.
**Deps:** BepInEx pack + REPOConfig (live in-game rule tuning). No REPOLib - we register nothing.
**Status:** scaffolding in progress (renamed from "Liquid Assets"). Namespace/GUID `Vippy.PourDecisions`.

---

## 1. The name

**Pour Decisions.** It's a finance pun (liquid assets = cash on hand) folded over a drinking pun (liquid), and it lands exactly on REPO's whole premise - Semibots converting recovered valuables into money. Your haul is the asset; your tab is the liability. Sell-y, short, double meaning, on-theme. Tagline: *"Turn your haul into a hangover."*

Runners-up if you hate it: **Last Call**, **Surplus** (REPO already uses "surplus" for over-goal extraction - see §3.5), **Happy Hour**, **On The Rocks**.

---

## 2. Why it feels native

Three decisions make it feel built-in:

1. **It rides the game's own network events.** Almost every drink trigger is a vanilla `[PunRPC]` that REPO already fans out to *all* clients (`RpcTarget.All`). So when someone dies, every client's copy of the mod sees `PlayerDeathRPC` fire at the same instant and renders the same prompt - perfectly synced, with **zero custom networking** for the core loop. The game *is* our message bus.
2. **It borrows the game's UI furniture.** We reuse `BigMessageUI`, the `SemiUI` spring-jitter animation system, `MenuManager`'s sound bank, `AssetManager`'s animation curves, `HUDCanvas` as the parent, and - critically - we **clone a live game text object to inherit its exact TMP font + material** instead of guessing the font asset. Our toast looks like REPO's HUD because it's built from REPO's HUD.
3. **Config lives in the mod menu, host-authoritative.** REPOConfig renders our `ConfigEntry`s as native sliders/dropdowns. The host's ruleset is the only one that matters; it's pushed to the room so clients obey it. Players never see a config file.

---

## 3. Verified game hooks (the trigger surface)

Every signature below was read out of `/home/justin/repo-wiki/work/decompiled-public/`. These are the events the rule engine listens to.

| # | Trigger | Patch target (verified) | Fan-out | Carries |
|---|---------|-------------------------|---------|---------|
| 1 | A Semibot dies | `PlayerAvatar.PlayerDeathRPC(int enemyIndex)` `:1356` | `RpcTarget.All` | who (the `__instance`'s `photonView.Owner`), killer enemy index |
| 2 | A Semibot revived | `PlayerAvatar.ReviveRPC(bool _revivedByTruck)` `:1526` | `All` | who, truck-vs-other |
| 3 | Health changed | `PlayerHealth.UpdateHealthRPC(int healthNew,int healthMax,bool effect,bool hurtByHeal)` `:504` | `All` | new/old health → damage vs heal |
| 4 | Valuable smashed | `PhysGrabObject.DestroyPhysGrabObjectRPC()` `:1768` | `All` | the object (check for `ValuableObject` + its dollar value) |
| 5 | Extraction point state | `ExtractionPoint.StateSetRPC(State state)` `:2162`; enum `Idle/Active/Success/.../Complete/Surplus` `:10` | `All` | reaching goal vs over-goal |
| 6 | Whole run wiped | `RunManagerPUN.AllPlayersDeadRPC(bool _set)` `:75` (buffered) / setter `RunManager.AllPlayersDeadSet` `:621` | `OthersBuffered` | team is down |
| 7 | Enemy spawned/woke | `EnemyParent.SpawnRPC()` `:328` | `All` | an enemy went live |
| 8 | Knocked down / ragdolled | `PlayerTumble.TumbleSetRPC(bool _isTumbling,bool _playerInput)` `:663` | `All` | who; `_playerInput=false` = something hit them |
| 9 | Head-grabber attack | `EnemyHeadGrabber.AttackSetRPC(int _index)` `:1312` | `All` | grabbed by the headman |
| 10 | Run phase / results screen | `GameDirector.gameState` enum `:7` (`gameStateResult` `:262`, `gameStateOutro` `:220`, `gameStateDeath` `:287`) | host state, all converge | level→truck, results, fail |

**Haul accounting** (for value-scaled smashes and the extraction toast) lives on `RoundDirector.instance`: `currentHaul`, `haulGoal`, `extractionPoints`, `extractionPointsCompleted`, `extractionPointSurplus` (all `internal int`, publicized). Player identity comes from `PlayerAvatar.playerName` / `.steamID` / `.isLocal` / `.photonView` (`:57-72`), and `SemiFunc.PlayerGetAll()` / `PlayerAvatarLocal()` / `PlayerGetSteamID()` for iteration.

> Determinism note: triggers 1-9 fire identically on every client, so per-victim drink orders ("you died → you drink") and the running tally are computed **locally on each client** and stay in sync for free. Only *host-decided* outcomes (random victim, waterfall order) need a broadcast - see §6.

---

## 4. The economy: sips, drinks, shots

One unit currency so amounts read at a glance. Default sizes (all host-tunable):

| Unit | Pips | Default weight | Feel |
|------|------|----------------|------|
| Sip | 🍺 | 1 | minor - took a hit, broke something cheap |
| Drink | 🍺🍺 | 2 | medium - got grabbed, smashed a fat valuable |
| Shot | 🍺🍺🍺 | 3 | major - you died |
| Finish your drink | 🥃 | - | team event - wipe |
| Toast | 🍻 | 1 each | everyone - a good extraction |

The **unit word is a config string** (default `"drink"`). Set it to `"sip"`, `"shot"`, `"dare"`, `"push-up"` - this is also the **soft-mode escape hatch** (§7) for non-alcohol play and for Thunderstore comfort.

---

## 5. Rule catalog

Each rule is an independent toggle + amount in config. Defaults below are the **Standard** preset (§5.1).

| Rule | Trigger | Who drinks | Default | Prompt copy |
|------|---------|-----------|---------|-------------|
| **Flatlined** | death (#1) | the dead Semibot | Shot (3) | "{name} flatlined. Take {n}." |
| **First Blood** | first death of the run | +bonus on top of Flatlined | +1 | "First to go. One extra." |
| **Down Bad** | tumble, `_playerInput=false` (#8) | the tumbled Semibot | Sip (1) | "{name} ate dirt. Sip." |
| **In The Bag** | head-grabber attack (#9) | the grabbed Semibot | Drink (2) | "The headman's got {name}. Drink." |
| **Butterfingers** | valuable smashed (#4) | last holder, **scaled to value lost** | Sip→Drink by tier | "You smashed ${value}. Drink {n}." |
| **Taxman Cometh** | enemy spawn (#7) | everyone | mode: `Off` / `First per level` / `Every spawn` (default First) | "Something woke up. Everyone sips." |
| **Cash Out** | extraction `Complete` (#5) | everyone (toast) | Toast (1); **Surplus → 2** | "Haul secured. Cheers - {n}." |
| **Quota Missed** | team wipe (#6) | everyone | Finish your drink | "Run's dead. Finish 'em." |
| **Welcome Back** | revive (#2) | the revived Semibot | Sip (1), off by default | "Back from the dead. Sip." |
| **Waterfall** | new gameplay level starts (#10 → `RunIsLevel` edge) | ceremony: P1 drinks till they stop, then P2... | off by default | full-screen 3-2-1 then chain |

### 5.1 Presets (a `[General]` dropdown)
- **Casual** - Flatlined + Cash Out only. Light touch for mixed groups.
- **Standard** *(default)* - the table above at listed defaults.
- **Last Resort** - everything on, amounts doubled, Taxman = Every spawn, Waterfall on.
- **Custom** - exposes every per-rule toggle/amount below; preset dropdown reads "Custom" once you touch one.

Picking a non-Custom preset stamps the individual rule values so REPOConfig still shows them populated and live-editable.

---

## 6. Architecture

```
PourDecisions/
  PourDecisions.cs                      # BaseUnityPlugin, ≤50 lines: Instance, PluginConfig.Init, Harmony.PatchAll
  Configuration/PluginConfig.cs        # all ConfigEntry<T>, REPOConfig-discoverable
  Models/
    DrinkUnit.cs                       # enum-ish weights + pip rendering
    Ruleset.cs                         # serializable rule config snapshot (host → room)
    DrinkOrder.cs                      # {targetSteamIDs[], reason, amount, isTeam}
  Services/
    RuleEngine.cs                      # vanilla-event in → DrinkOrder out; the brain
    RulesetSync.cs                     # host writes ruleset to room props; clients read on join/change
    TabService.cs                      # per-Semibot running tally, derived from events
    CeremonyService.cs                 # host-decided sequences (waterfall, random victim) via RaiseEvent
    NetworkCallbackService.cs          # MonoBehaviourPunCallbacks on DontDestroyOnLoad GO; late-join + RaiseEvent rx
  UI/
    DrinkHud.cs                        # the overlay: toast cards + the Tab widget, parented under HUDCanvas
    NativeText.cs                      # clones a live game TMP to inherit font/material
    BarTabScreen.cs                    # results-screen leaderboard
  Patches/
    PlayerAvatarDeathPatch.cs          # #1
    PlayerAvatarRevivePatch.cs         # #2
    PhysGrabObjectDestroyPatch.cs      # #4
    ExtractionPointStatePatch.cs       # #5
    RunManagerPUNWipePatch.cs          # #6
    EnemyParentSpawnPatch.cs           # #7
    PlayerTumbleSetPatch.cs            # #8
    EnemyHeadGrabberAttackPatch.cs     # #9
    GameDirectorResultsPatch.cs        # #10 → BarTabScreen + Waterfall trigger
    StatsManagerStartPatch.cs          # spawns NetworkCallbackService + DrinkHud, catches up existing room
```

### 6.1 Networking model (three channels, minimal custom plumbing)

1. **Core loop - no custom networking.** Every patch in `Patches/` runs on every client because the patched method is a vanilla `RpcTarget.All` RPC. Each client feeds the event to `RuleEngine`, which consults the synced ruleset and renders the prompt locally. `TabService` tallies the same deterministic events identically everywhere. This is the bulk of the mod and it needs *no* RPC of our own.

2. **Ruleset sync - Photon room properties.** On `OnJoinedRoom` (and on any live config edit), if `PhotonNetwork.IsMasterClient`, host serializes `Ruleset` to `PhotonNetwork.CurrentRoom.SetCustomProperties({ "pourdecisions_rules": json, "pourdecisions_v": Version })`. Clients read it on join and via `OnRoomPropertiesUpdate`. Late joiners get it automatically (room props are sticky). Clients show a small "Playing by host's rules" HUD note; their own config is ignored in-room. This is the skill's watermark pattern doing real work.

3. **Taxman terminal text - the *vanilla* RPC.** When the host posts an emoji telegram / recap to the truck screen, it calls `TruckScreenText.MessageSendCustom(name, msg, 0)`, whose body (read at `:1252`) does `photonView.RPC("MessageSendCustomRPC", RpcTarget.All, ...)` in multiplayer and a direct local call in singleplayer. So we reuse the game's own RPC; we host-gate the call (`IsMasterClientOrSingleplayer()`) so it's sent exactly once and fans out to every terminal. No custom RPC of ours.

> **No hand-rolled RPCs anywhere.** The core never *sends* - it postfixes vanilla `RpcTarget.All` RPCs, so every client already receives the event natively. Shared host state (ruleset + live tab) rides room custom properties, which replay to late joiners for free. "Random" ceremonies (waterfall order, random victim) are **deterministically seeded** from shared run/room state so each client computes the identical outcome with zero broadcast. We deliberately do **not** use `PhotonNetwork.RaiseEvent` - the modding rules forbid inventing RaiseEvent plumbing when a vanilla mechanism fits, and one does. If a genuine host-push ever became necessary, the correct tool is a real `[PunRPC]` on our own `PhotonView`, not RaiseEvent.

### 6.2 Host vs client responsibilities
- **Host:** owns `Ruleset` and the authoritative tab, publishes both to room properties, posts Taxman terminal text via the vanilla `MessageSendCustom` RPC.
- **Every client (incl. host):** runs all `Patches/`, the deterministic `RuleEngine`, `BarTabStats`, and the `DrinkHud`. Renders prompts locally from the vanilla events. Obeys the host's ruleset, never its own, while in a room.

### 6.3 Late join
Ruleset *and* the running tab both ride room custom properties, so a joiner reads current state on `OnJoinedRoom` with no message to send - sticky props replay automatically. The joiner's `BarTabStats` seeds from that property; from then on its local tally tracks the same deterministic vanilla events everyone else sees. (If a per-avatar action is ever needed at join, the addressable signal is `PlayerTumble.SetupDone`, not `OnPlayerEnteredRoom`.)

### 6.4 Scene gating
`NetworkCallbackService` and `DrinkHud` live on a `DontDestroyOnLoad` GO, so they exist in the menu too. Gate all prompt rendering and tally on `SemiFunc.RunIsLevel()` (plus `RunIsShop()`/`RunIsLobby()` where a rule should also fire in the truck/shop). On the level→menu edge, clear active toasts and hide the Tab widget. No drink prompts ever fire on the main menu.

---

## 7. UI / UX

### 7.1 Personal drink toast
A card that slides into a screen corner when **you personally** owe a drink - non-blocking so it doesn't cover gameplay.
- **Build:** create under `HUDCanvas.instance.transform`. Text via `NativeText` (§7.4) so font + material match the HUD exactly.
- **In:** `AssetManager.instance.animationCurveWooshIn` for the slide; a `SemiUISpringScale(0.4f, 5f, 0.2f)` pop on arrival.
- **Emphasis:** `SemiUISpringShakeY(20f,10f,0.3f)` jitter on the amount, scaled by drink weight (a Shot shakes harder than a Sip).
- **Content:** `{reason}` line + amount as pip emoji (🍺×n) rendered in the line, money values via the `<color=#558B2F>$</color>` style cribbed from `HaulUI` (`HaulUI.cs:34`).
- **Sound:** `MenuManager.instance.soundDeny.Play(cam)` for "you owe," `soundConfirm` for toasts. (`MenuManager.cs:77-91` confirmed the bank: `soundAction/Confirm/Deny/Tick/Hover/...`.) **Signature drink-call SFX:** the museum martini valuable carries a spill sound - `ValuableCocktail.soundSpill` (`ValuableCocktail.cs:14`, played via `soundSpill.Play(centerPoint)` at `:50`). Grab that `Sound` reference off a spawned `ValuableCocktail` (or cache the `AudioClip` the first time one loads) and play it on every drink prompt - a thematically perfect, fully-native "pour/clink" with no AssetBundle. Same trick applies to any `ValuableObject`'s break/impact `Sound` for the Butterfingers prompt, so the smash and the drink share the game's own audio vocabulary.
- **Out:** `animationCurveWooshAway` after a configurable dwell.

### 7.2 Team moments (the splash)
For everyone-drinks events (Cash Out toast, Quota Missed wipe) reuse the game's own center splash: `BigMessageUI.instance.BigMessage(message, emoji, size, colorMain, colorFlash)` (`BigMessageUI.cs:35`). Green flash for a good extraction, red for a wipe. This is literally the API REPO uses for "EXTRACTION COMPLETE," so team drink calls feel first-party.

### 7.3 The Tab (persistent widget)
A small receipt-style readout in a corner: your name + running total for this run, styled like the haul money readout. Toggleable in `[Display]`. Updates from `TabService`. Hidden outside levels.

### 7.4 Native font without guessing (`NativeText`)
The decompiled source doesn't expose the font *asset name* (the research flagged this at ~60% confidence). So we don't guess - we **clone**: grab a live game `TextMeshProUGUI` (e.g. off `BigMessageUI.instance` or `HaulUI.instance`), read its `.font` (`TMP_FontAsset`) and `.fontSharedMaterial`, and apply them to the text objects we create. Guaranteed pixel-match because it's the same asset the HUD is using. Capture once into a static cache; never instantiate a fresh TMP with a made-up font.

### 7.5 Countdown ceremonies
Waterfall / group-drink use a 3-2-1 built on the `ArenaRaceTimerUI` pattern (`ArenaRaceTimerUI.cs:452`): big red numbers ≤3, spring bounce per tick, then the chain order plays out. Reuse `MenuManager.instance.soundTick` per number.

### 7.6 Bar Tab leaderboard (results screen)
Patch `GameDirector.gameStateResult` (`GameDirector.cs:262`) to overlay a "Bar Tab" board: every Semibot, total drinks this run, 👑 on the heaviest tab, sorted. Native results-screen styling via `NativeText`. This is the payoff screen and the shareable/streamable moment.

### 7.7 Color palette (grounded constants)
No unified palette is exposed, so hardcode REPO's observed values as `static readonly Color`:
- Haul/money green `#558B2F` (`HaulUI.cs:34`)
- Warning orange `new Color(1f,0.6f,0f)` (`OverchargeRoundBarUI.cs:131`)
- Crit red / safe yellow as the countdown UI uses them.

---

## 8. Config surface (REPOConfig, host-authoritative)

`[General]`
- `Enabled` (bool, master)
- `Ruleset` (`string` + `AcceptableValueList`: `Casual / Standard / Last Resort / Custom`) - spaced human labels, not an enum, per the skill's REPOConfig note.
- `UnitWord` (string, default `"drink"`) - also the soft-mode lever.
- `ToggleHudKey` (`ConfigEntry<KeyCode>`, default `KeyCode.F7`) - show/hide overlay. **`KeyCode`, never the `Input.GetKeyDown(string)` trap**.

`[Rules]` (each pair shown only meaningfully in Custom; always bound so REPOConfig renders them)
- per rule: `Rule_Flatlined` (bool) + `Amount_Flatlined` (int, `AcceptableValueRange 0..5`), same shape for Down Bad, In The Bag, Welcome Back, Cash Out, First Blood bonus.
- `Butterfingers_Enabled` (bool) + `Butterfingers_ValueTiers` (the $ thresholds → pips).
- `Taxman_Mode` (`string` list: `Off / First per level / Every spawn`).
- `Quota_FinishDrink` (bool).
- `Waterfall_Enabled` (bool).

`[Display]`
- `ShowTab` (bool), `UsePipEmoji` (bool), `PromptSeconds` (float), `TeamSplash` (bool), `Sound` (bool).

All read into locals at the top of hot methods (every `.Value` explicit; never per-frame `.Value` reads in render loops).

---

## 9. Safety & Thunderstore

- README + manifest lead with **"Everyone in the lobby needs this mod"** and **"Host sets the rules."**
- A one-line **drink-responsibly** notice in the README and a first-launch `LogAlways` line. Not preachy, just present.
- The `UnitWord` soft-mode means the mod ships as a generic "consequence on game events" engine that *defaults* to drinking but reskins to dares/exercise in one setting - broadens the audience and sidesteps any storefront squeamishness.
- Manifest description ≤250 chars, no hype, names the R.E.P.O. version targeted.
- Deps: `BepInEx-BepInExPack-5.4.2305`, `nickklmao-REPOConfig-1.2.6`.

---

## 10. Build order (suggested milestones)

1. **Skeleton** - plugin entry, csproj from the SharedUpgradesPlus template, `PluginConfig` with the `[General]` block, `StatsManagerStartPatch` spawning an empty `DrinkHud`. Confirms it loads + REPOConfig sees the settings.
2. **One real rule end-to-end** - `PlayerAvatarDeathPatch` → `RuleEngine` → a personal toast via `NativeText`. Proves the font-clone and the local-event model. Test singleplayer.
2b. **Test harness (§17)** - as soon as `RuleEngine` exists, register the `pd ...` dev-console commands (`self`/`other`/`all`). From here on *every* milestone is verifiable solo: you fire each trigger by hand instead of needing the in-game situation or a second client. Build this early - it pays for itself immediately.
3. **The HUD polish** - Tab widget, team splash via `BigMessageUI`, sounds, woosh animations.
4. **Full rule catalog** - remaining patches, presets, value-scaled Butterfingers.
5. **Networking** - `RulesetSync` room props + the "host's rules" note; two-client test.
6. **Ceremonies + leaderboard** - `CeremonyService` RaiseEvent (waterfall/random), `BarTabScreen`, late-join snapshot.
7. **Ship gate** - compat check against the current game, README/CHANGELOG pass, 256² icon, zip.

---

## 11. Open calls for you

- **Name** - locking `Pour Decisions` unless you say otherwise.
- **Custom audio** - v1 reuses native sounds, no AssetBundle. The drink-call SFX pulls from `ValuableCocktail.soundSpill` (museum martini) for a native pour/clink; smashes reuse the valuable's own break `Sound`. Custom recorded SFX deferred to v1.1.
- **Taxman default** - defaulting to `First per level` so enemy spawns don't spam every client into oblivion. Say the word for `Every spawn` in Standard.

---

## 12. The Taxman tends bar

**The Taxman - the corporate operator who already speaks to you only in emoji through the truck terminal - is secretly running the bar tab.** Every deep feature below hangs off that framing, and all of it is built on verified vanilla machinery.

### 12.1 The truck terminal is our second screen
REPO's truck screen has a real, patchable message pipeline (`TruckScreenText.cs`):
- Singleton `TruckScreenText.instance` (`:117`).
- Custom messages: `MessageSendCustom(string playerName, string message, int emojis)` (`:1214`) → broadcasts `MessageSendCustomRPC(string playerName, string message, ...)` to `RpcTarget.All` (`:1252`). So **a single host-side call shows identical text on every client's terminal** - the game does the networking for us.
- The `emojis` arg is REPO's own convention: `0` none, `1` happy, `2` sad, and it appends a random emoji from curated lists already in the method (`:1218` happy `{:)} {:D} {:P} {eyes} {:o} {heart}`, sad `{:(} {:'(} {heartbreak} {fedup}`).
- Emoji are TMP sprite tags. `SemiFunc.EmojiText(string)` (`SemiFunc.cs:2072`) literally does `{name}` → `<sprite name=name>`. So any message we send can carry emoji by wrapping a sprite name in braces.

**In-level**, drink prompts use the HUD toast/`BigMessageUI` (existing §7). **In the truck** (`SemiFunc.RunIsLobby()` - the truck between levels), the *terminal itself* delivers the action: the Taxman posts the bar-tab recap and reacts to the run. That split is what makes it feel native - the loud stuff is on the HUD mid-job, the bookkeeping is on the terminal where REPO already does bookkeeping.

### 12.2 The Taxman speaks only in emoji (per your note)
Anything the Taxman "says" about the drinking game is **emoji-only**, matching his established voice. Pour Decisions posts these via `MessageSendCustom(taxmanName, "{emoji}{emoji}...", 0)` from the host (gated `IsMasterClientOrSingleplayer()`; the vanilla RPC fans it out). A confirmed emoji vocabulary, all read out of shipped sprite usage (`TruckScreenText`, `ShopKeeper.cs:372-408`, `ExtractionPoint`):

| Mood | Sprite tokens (confirmed in source) |
|------|-------------------------------------|
| Cheers / good haul | `:)` `:D` `:P` `eyes` `:o` `heart` `mellow` `cryinglaughing` `mouthhappy` `sparkle` `$$` |
| Drink up / death | `skull` `deathhead` `:(` `:'(` `fedup` `thumbsdown` `heartbreak` `mouthsad` |
| Chaos / very drunk | `weird_eye` `void_eye` `bang` `fire` `rocketfire` `dry` `smh` `disappointed` `X` `!` `?` |
| Pointing at the truck | `pointright` `truck` `pointleft` `point` `finger_wag` |

> **Ship-time guard:** a sprite name not present in the terminal's TMP sprite asset renders as a missing-glyph box. At init, read `TruckScreenText.instance` text mesh's `spriteAsset.spriteInfoList`, intersect our vocabulary against the names actually present, and only ever emit confirmed-present tokens. Self-validating, survives a game sprite-sheet change.

So: someone flatlines → Taxman posts `{skull}{:(}`. Surplus extraction → `{$$}{:D}{sparkle}`. The drink *order* is delivered as an emoji telegram, not English - and at high Proof (§14) the telegrams degrade into garble (`{weird_eye}{:o}{fedup}`).

### 12.3 More screen messages (the catalog)
New terminal messages Pour Decisions injects, all through the `MessageSendCustom` pipeline so they typewriter-reveal in the native style:
- **Tab recap on truck arrival** - per-Semibot line: name + emoji mood + drinks owed this level. The Taxman reading out the round's tab.
- **Round on the house** - extraction success toast announced on the terminal (`{$$}{heart}`), in addition to the in-level splash.
- **Last call** - before the final level / when heading to the shop: a wind-down line.
- **Career milestone** - when a player crosses a lifetime threshold from the stats manager (§13): `{name} {fire} 100 lifetime drinks`.
- **The reckoning** - end-of-run, the Taxman tallies the night before the results screen.

All are host-sent, host-gated, scene-gated to the truck, and Proof-slurred on the way out.

---

## 13. Bar Tab Stats (the statistics manager)

A first-class stats service, `BarTabStats`, modeled on the game's own `StatsManager` shape so it feels like part of the same system - but persisted to **our own file**, never the game's save (no corruption risk).

### 13.1 Shape (mirrors StatsManager)
`StatsManager` uses `SortedDictionary<string, Dictionary<string,int>> dictionaryOfDictionaries` (`:94`), is `DontDestroyOnLoad` (`:176`), singleton `instance` (`:44`). `BarTabStats` mirrors that: a steamID-keyed structure of per-category counters.
- **Run tab** (resets each run): `Dictionary<string /*steamID*/, RunTab>`, `RunTab` = counts per category (flatlined, butterfingers, downbad, inthebag, toasts, waterfall, welcomeback) + total units. This is the same on every client because it's derived from the deterministic vanilla events (§3 note), so the leaderboard and Proof agree everywhere with no extra networking.
- **Career file** (persists across sessions): lifetime totals per steamID, personal records (biggest single-run tab, longest death streak), run count, and **the play-date ledger** (§15). Stored as plain JSON under a `PourDecisions` data dir in `Paths.ConfigPath` - no ES3 coupling. (Option B: use the bundled ES3 to a *separate* `.es3` we own, mirroring `StatsManager`'s `persistentDataPath/saves` approach. JSON is the safer default; ES3 noted for parity.)

### 13.2 What it feeds
`BarTabStats` is the single source of truth for: the §7.6 Bar Tab leaderboard, the §14 Proof level (drunkenness), the Taxman's mood emoji selection, the §12.3 career-milestone messages, and the §15 usage warning. One service, many consumers.

### 13.3 Piggyback on real numbers where they exist
The game does **not** keep a rich per-player run ledger, so we count our own from the §3 events. But where REPO already has a real number, use it: haul via `RoundDirector.currentHaul`/`totalHaul`, value-lost for Butterfingers from the smashed `ValuableObject`'s dollar value. Our counters are drink-economy facts; the dollar figures are the game's.

### 13.4 Late join
On `OnPlayerEnteredRoom`, host sends the current run-tab snapshot via the single §6.1 RaiseEvent channel so the joiner's leaderboard and Proof match. Career stats are local-only (each player owns their own file).

---

## 14. The Drunkify engine (Proof-driven text corruption)

**As the team drinks, the truck terminal gets visibly drunker** - vanilla Taxman messages included.

### 14.1 Proof (the drunkenness scalar)
`Proof ∈ [0,1]`, derived from `BarTabStats` team run total through a config curve: `Proof = clamp(teamRunUnits / ProofThreshold)`, minus a per-level **sober-up** decay (each new level subtracts a configurable amount - the team dries out a little between jobs). **Default tuning: builds gradually** - `ProofThreshold` set so the terminal stays clean early and only slurs noticeably after a solid drinking session, rewarding a long run rather than firing the gag immediately. Fully tunable; deterministic from synced stats, so Proof is identical on every client.

### 14.2 Drunkify(text, proof) - tag-aware, deterministic
A pure string transform applied to terminal text. **Correctness is everything here:** the truck pipeline is full of `[haul]`/`[goal]` substitution tokens, `<color=...>`/`<sprite name=...>` rich-text tags, and `{emoji}` tokens. Drunkify **only mutates plain alphabetic runs outside `[]`, `<>`, and `{}`** - it tokenizes, skips every tag/variable/emoji span untouched, and slurs only real words. Get this wrong and you corrupt `<sprite name=skull>` into a missing glyph or break `[haul]` substitution.

Effects scale with Proof:
- **Low** - occasional letter doubling (`extraction`→`extracttion`), a stray vowel stretch (`go`→`gooo`).
- **Mid** - `*hic*` between words, `s`→`sh` slurs (`yes`→`yesh`), trailing `...`, a random emoji from the §12.2 "drink up" set punctuating a line.
- **High** - word transpositions, wrong-but-funny swaps, doubled/garbled emoji from the chaos set, sentences trailing off mid-`th-`.

**Determinism:** seed the transform from a hash of `(message, runSeed, floor(Proof*10))` - never `Random`/`DateTime`. Because the input message, the run seed, and Proof are all identical across clients, every terminal slurs the *same way*. The shared screen stays in sync. (This also sidesteps non-deterministic RNG entirely.)

### 14.3 Where it hooks
- **Vanilla Taxman pages** - `TextLine` carries `text` and `textOriginal` (`TruckScreenText.cs:95/98`). Postfix `InitializeTextTypingLogic()` (`:447`) and rewrite each line's `text = Drunkify(text, Proof)`, leaving `textOriginal` intact. The typewriter then reveals the already-slurred line, and `[haul]`-style substitution (which runs later in `TypingUpdate`) still works because Drunkify preserved the `[...]` tokens.
- **Our injected messages** - Drunkify the plain words before handing the string to `MessageSendCustom` (emoji tokens preserved for `EmojiText`).
- **Erratic reveal (polish tier)** - scale `TextLine.letterRevealDelay` (`:87`) by a Proof-seeded per-line jitter at init so drunk text reveals unevenly. Simple and sturdy; avoids fragile per-character patching of `TypingUpdate`.

### 14.4 Respecting the player
Drunkify is a `[Display]` toggle (`DrunkTerminal`, default on) with a `ProofThreshold` slider. Off = clean terminal, drink prompts only. It never touches gameplay-critical numbers - `[haul]`/`[goal]` values are inside skipped tokens, so the haul figure is always legible.

---

## 15. Vanilla-style usage warning splash

Per your ask: a warning that looks like it shipped with the game, shown alongside the real splash screens, escalating on heavy repeat use.

### 15.1 The vanilla furniture (confirmed)
REPO's boot splash is a real state machine: `SplashScreen` (`SplashScreen.cs:13`) runs `enum State { Wait, Semiwork, Warning, Done }`, with `StateWarning()` (`:92`) showing `SplashScreenUI.instance.warningTransform` (`SplashScreenUI.cs:9`) - that's the photosensitivity warning. `SplashScreen` carries `warningCurveIntro` (`:27`) and `warningSound` (`:32`); `SplashScreenUI` holds `semiworkTransform`/`warningTransform` RectTransforms and a singleton (`:5`). This is exactly the look to match.

### 15.2 Our splash
At splash time we **clone `SplashScreenUI.instance.warningTransform`** into our own page, swap its text to the Pour Decisions warning, and drive it with the same `warningCurveIntro` + `warningSound` so the animation and audio are pixel-and-pitch identical. Insert it into the flow by patching the `Warning → Done` transition (postfix on `StateWarning`/`StateSet`) to show our page for one beat after the vanilla warning, then proceed. Result: an extra warning card that reads as first-party, appearing right after REPO's own.

### 15.3 Content escalates on usage frequency
`BarTabStats` keeps a **play-date ledger** (§13.1) - one entry per distinct calendar day the mod ran (`System.DateTime.Today`, dedup by `yyyy-MM-dd`). On boot we compute the consecutive-day streak (gaps reset it). The splash text is chosen by streak:
- **Normal** - a brief, tasteful "Pour Decisions is active - please drink responsibly." (Shown sparingly, e.g. first launch of a day; config `ResponsibleSplash`.)
- **Streak ≥ threshold** (config `ExcessiveUsageDays`, **default 3** distinct days running) - the escalated card: "Excessive usage - {n} days running. Maybe take a night off." Same vanilla styling, gentle copy, easy to dismiss.

This is the responsible-drinking nudge from §9, made native and self-aware instead of a README footnote. It's a genuine, low-key duty-of-care touch - the kind of polish that makes the mod feel like it was made by someone who thought about the people using it.

**Toggle:** a single `[Safety].UsageWarning` bool, **default on**, is the master switch for the escalating splash - cleanly disableable in the in-game settings but enabled out of the box (per your call). `ExcessiveUsageDays` (default 3) is the threshold underneath it. With `UsageWarning` off, neither the responsible splash nor the excessive-usage card ever shows.

> Date handling lives in mod C# (`DateTime.Today`), which is fine - the no-`DateTime` rule applies only to workflow scripts, not runtime mod code. Store dates as `yyyy-MM-dd` strings; streak = longest run of consecutive days ending today.

---

## 16. Updated file structure (additions)

```
Services/
  BarTabStats.cs            # §13 - per-run + career stats, play-date ledger, JSON persistence
  ProofService.cs          # §14.1 - team Proof scalar from stats, sober-up decay
  Drunkify.cs              # §14.2 - tag-aware deterministic slur transform (pure, unit-testable)
  TaxmanBartender.cs       # §12 - composes + sends emoji telegrams and recaps to the truck terminal
UI/
  UsageSplash.cs           # §15 - clones warningTransform, escalating copy
Patches/
  TruckScreenInitPatch.cs  # §14.3 - drunkify vanilla TextLines in InitializeTextTypingLogic
  TruckScreenArrivalPatch.cs # §12 - fire tab recap / Taxman reactions on entering the truck
  SplashScreenWarningPatch.cs # §15 - inject the usage splash after the vanilla warning
```

Config additions: `[Display].DrunkTerminal` (bool, on), `[Display].ProofThreshold` (float, tuned to build gradually), `[Display].TaxmanReactions` (bool, on), `[Safety].UsageWarning` (bool, **on** - master toggle for the splash, disableable in-settings), `[Safety].ExcessiveUsageDays` (int, **3**), `[Debug].EnableTestCommands` (bool, off - §17). All host-irrelevant display/safety/debug toggles stay client-local (each player's own terminal/splash/console); only *rules* are host-authoritative.

Build-order additions: after milestone 4 (full rules), insert **4b - Bar Tab Stats + Proof + Drunkify on the terminal** (the deep layer), and fold **the Taxman bartender messages** into milestone 5 (networking, since they ride `MessageSendCustom`). **Splash warning** slots into milestone 6 with the leaderboard.

---

## 17. Test harness - native debug console commands

So every trigger can be exercised in singleplayer before a second client exists. REPO ships an **extensible dev console**, so we don't build UI - we register into the game's own.

### 17.1 The vanilla command system (confirmed)
`DebugCommandHandler` (`DebugCommandHandler.cs:41` singleton) exposes a public registry:
- `public void Register(ChatCommand cmd)` (`:69`).
- `ChatCommand(string name, string description, Action<bool,string[]> execute, Func<bool,string,string[],List<string>> suggest = null, Func<bool> isEnabled = null, bool debugOnly = true)` (`:25`) - name, help text, the executor `(isDebugConsole, args)`, an optional tab-completion suggester, an enable-gate, and a debug-only flag.
- Dispatch via `Execute(input, isDebugConsole)` (`:81`); `debugOnly` commands only run from the dev console, not normal chat (`:93`). The console (`DebugConsoleUI`) opens on a toggle key, gated to `gameState.Main` (`DebugConsoleUI.cs:52`), with autocomplete (`GetSuggestions`) and history already wired.

We register our commands in a postfix on `DebugCommandHandler` `Start`/`Awake` (once `instance` exists). They appear in the native console with native autocomplete, native confirm/deny click sounds (`CommandSuccessEffect`/`CommandFailedEffect`), and read as first-party dev tooling.

### 17.2 The command set - `pd <event> <target> [n]`
Every drink trigger gets a command, and every command takes a **target** so we can fire it as *self* or as *another (synthetic) Semibot* - the whole point, so multiplayer rendering, the leaderboard's second row, and "other player drinks" prompts can all be verified solo.

Targets: `self` (local avatar) · `other` (a synthetic test Semibot - steamID `PD_TEST`, name "Test Subject", created on demand so a second tab row exists in SP) · `all` (everyone, incl. the synthetic one).

| Command | Simulates | Default n |
|---------|-----------|-----------|
| `pd death <target>` | Flatlined (#1) | shot |
| `pd revive <target>` | Welcome Back (#2) | sip |
| `pd damage <target>` | health tick (#3) | - |
| `pd smash <target> [value]` | Butterfingers (#4), value-scaled | $-based |
| `pd extract [surplus]` | Cash Out toast (#5), `surplus` = over-goal | toast |
| `pd wipe` | Quota Missed team wipe (#6) | finish |
| `pd enemyspawn` | Taxman Cometh (#7) | per mode |
| `pd tumble <target>` | Down Bad (#8) | sip |
| `pd grab <target>` | In The Bag (#9) | drink |
| `pd firstblood <target>` | First Blood bonus | +1 |
| `pd waterfall` | Waterfall ceremony | - |

The executor builds a synthetic `DrinkOrder` for the named target and feeds it to the **same `RuleEngine`** path a real event uses - so we're testing the real pipeline, not a parallel mock. (In a real room, `other`/`all` route through the §6.1 RaiseEvent channel exactly as a host-decided order would; in SP it runs locally.)

### 17.3 State & inspection commands (test the deep layer)
| Command | Effect |
|---------|--------|
| `pd proof <0..1>` / `pd proof reset` | Force the Proof scalar to any value - eyeball Drunkify at any intensity without drinking the threshold. |
| `pd slur "<text>"` | Run `Drunkify` on arbitrary text at current Proof, print result to console - verify the tag-aware transform never eats `<sprite>`/`[haul]`/`{emoji}`. |
| `pd taxman <cheers\|drinkup\|chaos>` | Force a Taxman emoji telegram to the terminal - preview each mood's emoji vocabulary. |
| `pd recap` | Fire the truck tab-recap now (works in the truck). |
| `pd splash <normal\|excessive>` | Force-show the §15 usage splash - preview both copies without waiting days. |
| `pd streak <n>` | Set the play-date streak to test splash escalation at the `ExcessiveUsageDays` boundary. |
| `pd tab` / `pd tab clear` | Print / reset the current run tab. |
| `pd career` / `pd career reset` | Print / reset the persisted career stats + play-date ledger. |
| `pd preset <name>` | Switch ruleset live (Casual/Standard/Last Resort/Custom). |

Each provides a `Suggest` callback returning the valid sub-tokens (event names, then target names) so native tab-completion guides usage.

### 17.4 Gating
Register with `debugOnly: true` (dev console only, never normal chat) **and** `isEnabled: () => PluginConfig.EnableTestCommands.Value`. New `[Debug].EnableTestCommands` bool, **default off** for ship, flip on for development. This keeps the harness invisible to players while making solo testing one keypress away for us. The commands are a dev tool, not a feature - they don't appear in the README.

### 17.5 File
```
Services/TestCommands.cs   # §17 - registers all ChatCommands into DebugCommandHandler; synthetic-other target
Patches/DebugCommandHandlerStartPatch.cs  # postfix → TestCommands.RegisterAll()
```

---

## Locked decisions

- **Name:** Pour Decisions (final).
- **Default preset:** Standard, exactly as speced in §5.
- **Audio v1:** native only - `ValuableCocktail.soundSpill` for drink prompts, per-valuable break sounds for Butterfingers, `MenuManager` bank for UI feedback. No AssetBundle in v1.
- **Drunk terminal:** builds gradually - clean early, slurs after a real session. `[Display].DrunkTerminal` default on.
- **Usage warning:** on by default, disableable in settings (`[Safety].UsageWarning`). Escalates at a 3-distinct-day streak, gentle tone.
- **Test harness (§17):** native dev-console commands via `DebugCommandHandler.Register`, `self`/`other`/`all` targets, gated behind `[Debug].EnableTestCommands` (default off). Built first so every trigger is verifiable in singleplayer.
