using System.Collections.Generic;
using UnityEngine;

namespace PourDecisions.Services
{
    // ALL drunk-text CONTENT and the pure rules for picking a pool. No game state, no Unity singletons, no
    // networking - just data + selectors. DrunkTexts.cs is the engine that decides WHEN to pull from here.
    //
    // HOW TO EXTEND
    //   - Add lines to a state:     drop strings into the matching array. That's it.
    //   - Add a drunk tier/state:   add an array, then one branch in Pool()/ReactPool().
    //   - Add a biome:              add an array + a case in ThemePool() (and a probe in DrunkTexts.CurrentTheme).
    //   - Add an event reaction:    add a ReactKind, an array (+ optional *Wild), a case in ReactPool().
    //   - Add a mod-aware pool:     add an array + one line in ApplicableModPools().
    //
    // House rules: ASCII only (the game's pixel font has no unicode glyphs); "{0}" templates get filled by
    // the engine (a live display name, or the texter's name for Replies). Never name a specific monster or
    // valuable in a non-templated pool - those route through EnemyLines/ValuableLines so they only ever
    // appear when actually present.
    internal static class DrunkLines
    {
        internal enum Where { Level, Shop, Truck, Arena }
        internal enum Theme { None, Manor, Wizard, Arctic }
        internal enum ReactKind
        {
            Death, Down, HeadmanGrab, HiddenGrab, Smash, Scared, Cheer,
            SpewerFace,   // a spewer latched onto your face
            SpewerPuke,   // the spewer on your face is hosing the whole crew
            Shadow,       // the shadow (EnemyShadow) reaches out and slaps you - "claps you, stretches out"
            ArenaDeath,   // eliminated in the king-of-the-losers arena
            LastStanding, // you're the last one breathing in the arena
            CrownWin,     // you grabbed the crown - king of the losers
            RaceWin,      // won the king-of-the-losers RACE (ArenaRace vehicle dash)
            RaceOut,      // knocked out of the race
        }

        // tab (drinks owed over the run) -> drunkenness stage 0..4
        internal static int Stage(int tab) => tab >= 21 ? 4 : tab >= 13 ? 3 : tab >= 7 ? 2 : tab >= 3 ? 1 : 0;

        // seconds between a player's ambient lines, tighter the drunker they are
        internal static Vector2 Interval(int stage) => stage switch
        {
            4 => new Vector2(30f, 55f),
            3 => new Vector2(50f, 90f),
            2 => new Vector2(75f, 130f),
            _ => new Vector2(120f, 200f),
        };

        // ---- pool selectors (the engine asks here; content lives in the arrays below) ----

        internal static string[] Pool(int stage, bool dead, Where where) => where switch
        {
            Where.Shop => dead ? ShopDead : (stage >= 3 ? ShopSmashed : ShopBuzzed),
            Where.Truck => dead ? TruckDead : (stage >= 3 ? TruckSmashed : TruckBuzzed),
            Where.Arena => dead ? ArenaDead : (stage >= 3 ? ArenaWild : ArenaLines),
            _ => dead
                ? (stage >= 4 ? DeadBlackout : stage == 3 ? DeadWasted : stage == 2 ? DeadDrunk : DeadTipsy)
                : (stage >= 4 ? Blackout : stage == 3 ? Wasted : stage == 2 ? Drunk : Tipsy),
        };

        internal static string[]? ThemePool(Theme theme) => theme switch
        {
            Theme.Manor => ManorLines,
            Theme.Wizard => WizardLines,
            Theme.Arctic => ArcticLines,
            _ => null,
        };

        internal static string[] EnemyPool(bool wild) => wild ? EnemyLinesWild : EnemyLines;
        internal static string[] ValuablePool(bool wild) => wild ? ValuableLinesWild : ValuableLines;

        internal static string[] ReactPool(ReactKind kind, bool wild) => kind switch
        {
            ReactKind.Death => wild ? DeathReactWild : DeathReact,
            ReactKind.Down => wild ? DownReactWild : DownReact,
            ReactKind.HeadmanGrab => wild ? HeadmanGrabReactWild : HeadmanGrabReact,
            ReactKind.HiddenGrab => wild ? HiddenGrabReactWild : HiddenGrabReact,
            ReactKind.Smash => wild ? SmashReactWild : SmashReact,
            ReactKind.Scared => wild ? ScaredReactWild : ScaredReact,
            ReactKind.SpewerFace => wild ? SpewerFaceReactWild : SpewerFaceReact,
            ReactKind.SpewerPuke => wild ? SpewerPukeReactWild : SpewerPukeReact,
            ReactKind.Shadow => wild ? ShadowReactWild : ShadowReact,
            ReactKind.ArenaDeath => wild ? ArenaDeathReactWild : ArenaDeathReact,
            ReactKind.LastStanding => wild ? LastStandingReactWild : LastStandingReact,
            ReactKind.CrownWin => wild ? CrownWinReactWild : CrownWinReact,
            ReactKind.RaceWin => wild ? RaceWinReactWild : RaceWinReact,
            ReactKind.RaceOut => wild ? RaceOutReactWild : RaceOutReact,
            _ => wild ? CheerReactWild : CheerReact,
        };

        // King-of-the-losers winner, name-dropped in regular runs while they wear the crown. "{0}" = the
        // crowned Semibot's display name. Offered by the engine only while SessionManager has a crowned player.
        internal static string[] KingPool(bool aboutSelf) => aboutSelf ? CrownSelfLines : CrownCrewLines;

        // Cart callouts that imply a cart is actually there - gated by the engine on a real PhysGrabCart in
        // the level, so "get off the cart" never fires in a cart-less room. "{0}" = the drunk's name (replies).
        internal static string[] CartReplies => CartReplyLines;

        // Cart/extraction-mod revive variant (the engine swaps it in for Death when such a mod is present).
        internal static string[] DeathExtraction => DeathReactExtraction;

        // Mod-aware pools that apply right now: a mod's pool when it's installed, the ShrinkerGun SELL pool
        // only when it isn't. Each is a stable array reference, so the engine's no-repeat dedup still works.
        // The hand-written mod table: (GUID fragments) -> (joke pool). Add a mod = add one row here plus
        // its array down in the content section. Built lazily so it runs after every pool array is
        // initialized (static-field order). ApplicableModPools and AccountedFragments both derive
        // from this, so the procedural fallback automatically knows which mods are already covered.
        private static (string[] Frags, string[] Pool)[]? _rows;
        private static (string[] Frags, string[] Pool)[] Rows() => _rows ??= new (string[] Frags, string[] Pool)[]
        {
            // Vippy's own
            (new[]{"sharedupgrades"}, ModSharedUpgrades),
            (new[]{"repofidelity","fidelity"}, ModFidelity),
            (new[]{"battlescars"}, ModBattleScars),
            (new[]{"robecalling"}, ModRobeCalling),
            (new[]{"cartfix"}, ModCartFix),
            (new[]{"aimdeltafix","aimdelta"}, ModAimFix),
            // popular community
            (new[]{"moreshopitems"}, ModMoreShopItems),
            (new[]{"moreupgrades"}, ModMoreUpgrades),
            (new[]{"morehead"}, ModMoreHead),
            (new[]{"deathheadhopper","headhopper"}, ModHeadHopper),
            (new[]{"lethalcompanyvaluables"}, ModLCValuables),
            (new[]{"mimic"}, ModMimic),
            (new[]{"moreplayers"}, ModMorePlayers),
            (new[]{"extractionpointconfirm","confirmbutton"}, ModExtractConfirm),
            (new[]{"noitemspawnlimit","spawnlimit"}, ModNoSpawnLimit),
            (new[]{"adminmenu"}, ModAdminMenu),
            // favorited-dev mods
            (new[]{"lobbyimprovements"}, ModLobbyImprovements),
            (new[]{"levelscaling"}, ModLevelScaling),
            (new[]{"timeattack"}, ModTimeAttack),
            (new[]{"repodrp","richpresence","discordrp"}, ModDiscordRP),
            (new[]{"resellyourupgrades","resellupgrades"}, ModResell),
            (new[]{"shopdiscount"}, ModShopDiscounts),
            (new[]{"returntolobby"}, ModReturnToLobby),
            (new[]{"legalizenuclearbombs","nuclearbomb","nuke"}, ModNuke),
            (new[]{"enemyvaluabletweaks"}, ModOrbTweaks),
            (new[]{"unknownupgrades"}, ModUnknownUpgrades),
            (new[]{"playerspeed"}, ModPlayerSpeed),
            (new[]{"latejoin"}, ModLateJoin),   // ribbed as janky-by-design, nods at ReturnToLobby
        };

        internal static List<string[]> ApplicableModPools()
        {
            var list = new List<string[]>
            {
                ModAware.Has("shrinkergun", "scalercore") ? ModShrinker : ModShrinkerSell,  // SELL when absent
            };
            foreach (var row in Rows())
                if (ModAware.Has(row.Frags)) list.Add(row.Pool);
            if (ModAware.EasyMode) list.Add(ModEasyMode);   // catch-all for cheaty/easy-mode mods
            return list;
        }

        // Every GUID fragment we've hand-written a pool for - the procedural fallback skips these so it only
        // ever shouts out mods we DIDN'T account for.
        internal static IEnumerable<string> AccountedFragments()
        {
            yield return "shrinkergun";
            yield return "scalercore";
            foreach (var row in Rows())
                foreach (var f in row.Frags)
                    yield return f;
        }

        // =====================================================================================
        //  CONTENT
        // =====================================================================================

        // ---- in-level, alive (universal - no specific monster/valuable names) ----
        internal static readonly string[] Tipsy =
        {
            "ok ok i can totally still carry this", "im fine im fine i'm totally fine",
            "watch this. watch. ok nvm", "i actually kinda love this job",
            "that valuable was looking at me weird", "the quota is more of a suggestion right",
            "do NOT tell the taxman how much we dropped", "i found a coin. we're rich. we're saved.",
            "we're so good at this why do we keep dying", "which way's the cart. don't answer. i got it. i don't got it.",
            "i'm pacing myself. this is me pacing. *trips*",
        };
        internal static readonly string[] Drunk =
        {
            "i luv u guys so much *hic*", "wait whose head is that", "the taxman GETS me yknow",
            "who keeps moving the extraction point", "i'm not lost the MAP is wrong",
            "this monster is actually kinda chill", "extraction? in THIS economy?",
            "the company prints a new me if i die so. whatever", "i kissed the valuable for luck. judge me.",
            "i put a coin in the wishing... it was just a hole. it ate my coin.",
            "who scheduled overtime in the AFTERLIFE wing", "i high-fived a valuable and it FELL and i'm in TROUBLE",
            "i'm sweating money. is that normal. is the quota OSMOSIS.", "i love this crew even when you LEAVE me to die",
        };
        internal static readonly string[] Wasted =
        {
            "guysh the floor is MOVIN", "shhh the monster can hear ur FACE", "im not drunk the gravity is drunk",
            "i AM the valuable now actually. sell me.", "we're just printed meat for the quota and i'm at PEACE",
            "the walls are breathin and i respect it", "i'm gonna marry the cart. you're all invited. bring the haul.",
            "physics is a LAW and i'm a CRIMINAL", "i tried to extract MYSELF and the machine said no. RUDE.",
            "we left a guy. did we leave a guy? i'm the guy. i left me.",
            "the quota whispered my NAME. it knows my PRINT NUMBER.", "i licked the floor to taste the QUOTA. it tastes like CARPET.",
            "the extraction machine is BEAUTIFUL and it will NEVER love me back",
            "we're doing GREAT. are we doing great? define great. define WE.",
        };
        internal static readonly string[] Blackout =
        {
            "WHO TURNED OFF GRAVITY", "i am become haul, destroyer of carts",
            "the monster and i are dating now don't extract without us",
            "the company printed 47 of me and ALL were cowards", "i have achieved liftoff (i fell in the lava)",
            "I ATE THE COIN. THE COIN IS ME NOW. I AM CURRENCY.", "GRAVITY FILED A RESTRAINING ORDER AGAINST ME",
            "we are inside the truck inside the level inside the QUOTA it's QUOTAS ALL THE WAY DOWN",
            "the cart has a NAME now and it's greg. everything is greg.", "the void called. it's understaffed. i SAID I'D HELP.",
            "tell the taxman i'm not AFRAID of him (i am, send the surplus)", "i'm not blacked out i'm DOWNLOADING the quota directly",
            "i'd sell my SOUL but the taxman already OWNS it (standard contract)",
        };

        // ---- in-level, dead ----
        internal static readonly string[] DeadTipsy =
        {
            "ok being a head is fine actually", "can someone PICK ME UP", "death is just a coffee break",
            "carry me to the healer u cowards", "i can see your ankles from down here", "well THAT happened",
            "i regret nothing. except the cart.", "ten outta ten would die again honestly",
            "rolling toward extraction. very slowly. very dignified.", "i'm not a corpse i'm a CARRY-ON",
            "guys i found a coin down here come get it (i can't)", "the floor is so much closer now and i have NOTES",
            "anyone gonna grab me or is this a BIT", "i died doing what i love: dropping the valuable",
        };
        internal static readonly string[] DeadDrunk =
        {
            "i'm a head now and honestly? freeing.", "the company will print another me. say hi.",
            "respawn me i have OPINIONS", "from down here u all look like valuables",
            "being dead is great no quota down here", "i think i'm haunting the cart now",
            "the taxman can't tax a HEAD checkmate", "i rolled under the couch and found the OTHER dead guy. we talk now.",
            "i'm a valuable now technically. extract ME. i'm worth at least a coin.", "kick my head toward the exit i wanna CONTRIBUTE",
            "i can hear the cart and i MISS her", "death is just unpaid overtime and i'm STILL clocked in",
            "tell my cart i love her", "i'm not dead i'm load-bearing now",
        };
        internal static readonly string[] DeadWasted =
        {
            "i am one with the void and the void is UNDERSTAFFED", "the taxman is in the walls. in ME.",
            "death tastes like overtime and i WANT MORE", "i can hear the quota breathing it's so CLOSE",
            "pick me up pick ME UP i have SECRETS", "i'm not dead i'm just between print runs",
            "the cart will avenge me. the cart KNOWS.", "i'm rolling toward the light it's the extraction machine it's GORGEOUS",
            "from the floor i can confirm: this place has TERRIBLE foundations", "tell the cart i'll be back for the recipe",
            "i'm a head AND a hazard AND a HERO, scoop me UP", "i've been every corpse in this room and we are LEGION",
            "the void has a SUGGESTION BOX and i FILLED it", "my head is a VALUABLE now please add me to the haul",
        };
        internal static readonly string[] DeadBlackout =
        {
            "I AM THE HAUL NOW. EXTRACT ME. FREE ME.", "i've died 47 times and learned NOTHING and i'm THRILLED",
            "the void is the truck but FOREVER and i LIVE here", "tell the MANAGER i quit. i'm a head. i QUIT.",
            "i am every dead Semibot and we are LEGION", "the surplus took me. drink for the surplus.",
            "reprint me with LEGS this time you cowards", "i see the quota's true form and it is ANGRY",
            "death is a LATERAL career move and i'm THRIVING down here", "i have become friends with the floor. the floor is HONEST. the floor never LIES.",
            "i can see EVERY extraction point at ONCE and NONE of them are real", "i AM the level now. you're all walking around INSIDE me. mind the cart.",
            "scoop my head into the cart i wanna be SOLD i wanna MATTER", "the company can't FIRE a head that's already DEAD checkmate corporate",
            "i'm filing for HEAD rights with the taxman's UNION (it's me, i'm the union)", "MY HEAD ROLLS WHERE IT WANTS NOW i answer to NO quota",
        };

        // ---- shop (safe - shopkeeper, cart, drone, the gun, all standard fixtures) ----
        internal static readonly string[] ShopBuzzed =
        {
            "ooh do we have enough for the big one", "the shopkeeper is staring. say something cool.",
            "everything in here is so OVERPRICED honestly", "i want the gun. i NEED the gun.",
            "tax included?? on top of the QUOTA??", "the cart's lookin empty let's FIX that",
            "can we afford it. CAN we. i can't do math rn", "i'm just gonna hold this item real quick",
            "do we NEED healing or do we need the cool grenade. think carefully.", "i'm buying a drone and naming it after the cart",
            "split the bill? we don't have a BILL we have a QUOTA", "the shopkeeper has seen things. you can tell.",
            "put it back. put it BACK. ok fine ONE.", "i could buy stamina or i could buy VIBES",
            "everything's on sale if you just take it real fast", "we came in for ONE item. we are leaving with SEVEN.",
            "is the shopkeeper judging my cart. he's judging my cart.", "the prices went UP since last shop i'm WATCHING you",
            "we should pool our money. emotionally. i'm broke.", "i'm gonna read every label out loud. EVERY one.",
            "this drone better be worth it or i'm RETURNING it (i can't)", "guys the healing pack is RIGHT THERE we're not INVINCIBLE",
            "ooh ooh the upgrade station. i love a little UPGRADE.", "do we trust this shopkeeper. we don't KNOW him. we love him.",
            "i'm window shopping. there's no windows. i'm just shopping.",
        };
        internal static readonly string[] ShopSmashed =
        {
            "i'm gonna lick every item to see which is real", "SHOPKEEPER i love u marry me",
            "we should just TAKE the items he can't stop us", "i put it in the cart with my MIND",
            "the prices are TALKING to me and they're RUDE", "this whole shop is a tax write off baby",
            "i ate a healing item is that. is that bad", "buy the gun buy the GUN buy the gun",
            "i'm gonna haggle. with my FISTS. (i mean talking. probably.)", "every item in here is my CHILD now and we are LEAVING together",
            "the drone is judging my purchases and it's RIGHT to", "i offered the shopkeeper a coin and my UNDYING loyalty",
            "the cart is full. the cart is NEVER full. MORE.", "i'm not stealing i'm RELOCATING inventory",
            "i can hear the grenades and they want to come HOME with me", "this is the BEST store i have EVER been thrown out of",
            "i'm gonna buy the WHOLE shelf. with WHAT. with DREAMS.", "the shopkeeper blinked first i WIN i get a discount (i don't)",
            "i licked the cart. for SECURITY. it's MINE now. branded.", "the upgrade machine PROMISED me things. i BELIEVE the machine.",
            "i'm gonna ride the drone like a little HORSE", "money is FAKE but this grenade is REAL and it's coming with ME",
            "we don't need a PLAN we need EVERYTHING on this SHELF", "i told the shopkeeper my whole life story. all 47 print runs.",
            "the healing pack and i are SOULMATES don't let me leave without it", "i'm gonna negotiate. by crying. at the shopkeeper. softly.",
            "put the gun in the cart. the GUN. in the CART. trust the process.", "i smelled the item. it smells like PROFIT. or feet. profit-feet.",
        };
        internal static readonly string[] ShopDead =
        {
            "carry my head to the shop i wanna buy things", "even dead i have better credit than the taxman",
            "put my head in the cart with the loot please", "i can't shop i'm a HEAD but i have OPINIONS on the gun",
            "buy something nice for my funeral. and the cart.", "from down here the shop prices look even WORSE",
            "the shopkeeper won't sell to a head. discrimination.", "spend my share. i'm dead. treat yourselves. get the gun.",
            "roll me past the shelf i wanna WINDOW SHOP with my one good eye", "tell the shopkeeper a head's money is STILL money",
            "buy the healing pack. for ME. it's too LATE for me but STILL.", "i'd point at what i want but i have NO ARMS. it's the drone. buy the drone.",
        };

        // ---- truck (the road - truck, cart, healer pod, road, all standard) ----
        internal static readonly string[] TruckBuzzed =
        {
            "are we there yet. are we. are we there yet.", "good run guys. good RUN. i'm a lil emotional.",
            "let's count the haul again i love counting the haul", "the road goes on forever and so does the quota",
            "anyone else just vibing to the engine hum", "i call shotgun for the next level",
            "we did NOT lose that much value. did we.", "i love this truck. genuinely. don't make it weird.",
            "who's driving. nobody? incredible. love that for us.", "the healer pod is basically a hot tub if you squint",
            "let's NOT tell the taxman about the vase. the vase stays between us.", "i'm gonna nap. wake me when we're rich.",
            "next level's the one. i can FEEL the quota shrinking.", "group photo by the cart? for morale?",
            "the engine sounds happy today. or that's just me.", "i miss the last level already. weird. i almost died there.",
            "we're a TEAM. a sweaty, indebted, beautiful team.", "i wanna decorate the truck. little curtains. quota permitting.",
            "how much further. don't tell me. tell me. don't.", "the road is bumpy or i'm just FULL of feelings",
        };
        internal static readonly string[] TruckSmashed =
        {
            "the truck is a SUBMARINE now buckle up", "i'm gonna drive. give me the wheel. GIVE it.",
            "i love this truck more than my last 12 print bodies", "where are we GOING. metaphysically. where.",
            "the haul is talking again it wants to go HOME", "i'm gonna nap in the cart wake me at the quota",
            "next level's gonna be EASY i can FEEL it", "the truck healer is my therapist now",
            "i renamed the truck. her name is Brenda. respect Brenda.", "let's do donuts. the truck does donuts right. RIGHT?",
            "i counted the haul nine times and got nine answers", "the road is a SUGGESTION and i REJECT it",
            "i'm gonna lie down in the road. the truck'll catch up. trust.", "Brenda and i have plans the taxman wouldn't UNDERSTAND",
            "if i press every button at once we go to SPACE probably", "the engine and i are harmonizing don't RUIN this",
            "i wanna marry the truck AND the cart. it's legal in the void.", "the road told me a SECRET. it's that there's MORE road.",
            "let me hold the haul. just HOLD it. i'll give it back. probably.", "the healer pod and i had a MOMENT and now i'm CRYING happy tears",
        };
        internal static readonly string[] TruckDead =
        {
            "just toss my head in the back with the haul", "dead in the truck. peak. ten outta ten.",
            "revive me before the next level i wanna HELP", "i can hear the truck healer calling me home",
            "leave me here i AM cargo now", "prop my head on the dash i wanna see the road",
            "buckle my head in. safety first. i'm already dead but STILL.", "i'm the truck's emotional support corpse now",
            "drive smooth my head's gonna ROLL off the bench", "wake my head when we hit the next level i'll do my BEST",
        };

        // ---- biome-specific (alive, in-level) - ENVIRONMENTAL flavor only; no item/monster name-drops ----
        internal static readonly string[] ManorLines =
        {
            "fancy house. who hurt the people who lived here.", "this manor has WAY too many hallways and i've gotten lost in ALL of them",
            "the chandelier is worth more than my entire print run", "i found the good silverware. it's in the cart. it's MINE now.",
            "every painting in here is staring and i HATE it", "this carpet is older than the company and TWICE as cursed",
            "the manor creaks like it owes someone money", "rich people had so much SPACE and used it for HAUNTING",
            "the grand staircase tried to KILL me. respect.", "i'm gonna live here after we hit quota. just me and the ghosts.",
            "the manor has a ballroom and i WILL be dancing before i die", "who dusts this place. nobody. that's the SCARY part.",
            "the wallpaper is judging my whole career", "this house has a LIBRARY and none of the books explain the QUOTA",
        };
        internal static readonly string[] WizardLines =
        {
            "this whole place smells like ozone and bad DECISIONS", "everything here GLOWS and i think i'm absorbing it through my FACE",
            "the candles won't blow out and they're JUDGING me", "magic is real and it's COMING OUT OF MY QUOTA",
            "i waved my arms and yelled a spell. nothing. i yelled LOUDER.", "the wizard left in a hurry. the kettle's still ON. eternally.",
            "there's runes on the floor and i think i STEPPED in a curse", "everything in here hums at a frequency that says PAY RENT",
            "i'm pretty sure that potion is sentient and DISAPPOINTED in me", "the air SPARKLES and so do i (i'm sweating glitter, send help)",
            "i feel POWERFUL in here. and also like i might EXPLODE.", "this place has VIBES and the vibes are LICENSED NECROMANCY",
            "the floor tried to teleport me. i think. i'm somewhere ELSE now.", "wizards had a SAW for DECOR and a CAULDRON for SOUP. priorities.",
        };
        internal static readonly string[] ArcticLines =
        {
            "it's so COLD my print body is BUFFERING", "the ice is cracking and so is my COMMITMENT to this job",
            "i can see my breath and ALSO the quota slipping away", "everything's frozen even my MISTAKES are preserved out here",
            "i licked a frozen railing for SCIENCE. update: stuck.", "the cold is honest. the cold doesn't TAX you.",
            "i slipped on the ice and slid into extraction. BEST run ever.", "my hands went numb so now i grab valuables with VIBES",
            "is that a monster or just a really committed icicle", "frostbite is just the company's way of saying slow down",
            "i built a snow Semibot. his name is greg. he's the smart one now.", "the wind out here is SAYING things and none of it is nice",
            "i can't feel my face but i CAN feel the quota and that's WORSE", "we drove something off a ledge out here. it's fine. it's GONE. we're fine.",
        };

        // ---- TEMPLATED, presence-gated. "{0}" = a live in-game DISPLAY name, filled by the engine ----
        internal static readonly string[] EnemyLines =
        {
            "is that a {0}? it's a {0}. act natural.", "the {0} hasn't seen us yet. nobody breathe. nobody. {0}.",
            "i'm gonna wave at the {0}. for diplomacy.", "ok which one of you scheduled the {0}",
            "the {0} is doing its little walk again", "i named the {0}. its name is greg. don't get attached.",
            "everyone be cool the {0} can smell fear and COINS", "the {0} is between me and the cart and i WANT the cart",
            "do NOT make eye contact with the {0}", "the {0} looked at me. we have a CONNECTION now.",
            "tell the {0} it's doing a great job and then RUN", "i think the {0} is just lonely honestly",
            "who let the {0} in here. this is a WORKPLACE.", "the {0} is faster than it looks. i'm slower than i look. bad combo.",
            "hiding from the {0} in this locker. living my best life.", "the {0} walked past and i HID behind a vase. iconic.",
            "psst. the {0}. it's RIGHT there. i'm not panicking YOU'RE panicking.", "the {0} sighed. even the {0} is tired of the quota.",
            "i'd fight the {0} but my arms are for CARRYING not WINNING",
        };
        internal static readonly string[] EnemyLinesWild =
        {
            "i'm gonna ASK the {0} out. worst it can do is kill me. again.", "the {0} and i are PARTNERS now we split the haul 50/50",
            "i CHALLENGE the {0} to a duel of WITS (i have none)", "the {0} is my PRINTED SOULMATE and the company KNEW",
            "let me PET the {0} just ONCE before it ends me", "the {0} whispered the quota number to me and it's WRONG and i BELIEVE it",
            "i'm not running from the {0} we're doing PARKOUR together", "the {0} ate my arm and said thank you and i said no problem KING",
            "i taught the {0} my name and now it WON'T stop saying it", "the {0} is the only one who GETS me down here",
            "i'd die for the {0}. i WILL. probably in like ten seconds.", "the {0} and the taxman are in CAHOOTS i've cracked the CASE",
            "i licked the {0}. don't ask. it tasted like OVERTIME.", "the {0} is wearing my LAST print body as a HAT and honestly? slay.",
            "i'm gonna RIDE the {0} into extraction it's a SHORTCUT probably", "the {0} blinked and i fell in LOVE and also down the STAIRS",
            "the {0} is just MISUNDERSTOOD it only wants the QUOTA like the rest of us", "i invited the {0} to the truck. don't be WEIRD about it.",
        };
        internal static readonly string[] ValuableLines =
        {
            "the {0} is worth more than my whole CAREER", "i'm gonna marry the {0}. you're all invited.",
            "DO NOT drop the {0}. look at me. the {0}.", "the {0} blinked. i swear the {0} blinked.",
            "i kissed the {0} for luck. it's tradition now.", "the {0} is so HEAVY who designed this who HURT them",
            "load the {0} FIRST it's basically the whole quota", "i found a {0}!! we're rich!! we're saved!! ...it's one {0}.",
            "the {0} is mine. i called it. i licked it. it's mine.", "careful with the {0} that thing's our RENT",
            "the {0} is staring at me from the cart and i feel WATCHED", "i whispered the quota to the {0} and it UNDERSTOOD",
            "we are NOT leaving without the {0}. i'll carry it with my TEETH.", "the {0} fits in the cart if we BELIEVE",
            "guys the {0}. the {0}. are you SEEING the {0}.", "i tripped and almost dropped the {0} and aged ten PRINT YEARS",
            "the {0} and i have BEEN through it this level", "hands off the {0} that's MY emotional support valuable",
        };
        internal static readonly string[] ValuableLinesWild =
        {
            "i AM the {0} now. extract ME. i'm worth at LEAST a {0}.", "the {0} spoke to me. it wants to go HOME. i AM its home.",
            "i ate part of the {0} to see if it was real. inconclusive. ate more.", "the {0} and i are ELOPING the cart can't hold us",
            "i traded my last print body for the {0} and i'd do it AGAIN", "the {0} knows the taxman's WEAKNESS and it told ME",
            "i'm gonna THROW the {0} just to see. for science. for the QUOTA.", "the {0} is GLOWING or i'm dying. either way. gorgeous.",
            "stack the carts. STACK them. the {0} demands a THRONE.", "i named the {0} after my first print body. rest easy little {0}.",
            "the {0} is the only thing in this level that has ever LOVED me", "i'm gonna whisper the quota to the {0} until it CONFESSES",
            "the {0} winked. the {0} WINKED. we have an UNDERSTANDING now.", "let me hold the {0} one more time before the taxman TAKES her",
        };

        // A soberer crewmate's clapback. "{0}" = the drunk Semibot's name.
        internal static readonly string[] Replies =
        {
            "wtf {0}", "{0} please", "{0} are you ok", "someone cut {0} off", "ignore {0} they're gone",
            "we are NOT carrying {0} again", "{0} put it DOWN",
            "{0}... buddy...", "take {0}'s comms away", "{0} that's the third time tonight",
            "respectfully {0} what", "{0} we are AT WORK", "not {0} again oh my god",
            "{0} that is the EXTRACTION machine do not lick it", "nobody give {0} the gun",
            "{0} the monster can HEAR you", "i'm telling the taxman on {0}",
            "who let {0} drink before the quota", "{0} that's a real monster not a costume",
            "{0} we have been over this", "someone revive {0}'s brain cell", "{0} drink some WATER for the love of",
            "{0} is having a moment. give them space.",
        };

        // Cart-directive replies - only offered when a cart is actually in the level (engine-gated). "{0}" =
        // the drunk's name. These name the cart as a real object you're on/near, so they'd read as nonsense
        // in a cart-less room.
        internal static readonly string[] CartReplyLines =
        {
            "{0} get OFF the cart", "this is why {0} doesn't get the cart",
            "{0} the cart is for LOOT not for SITTING", "someone get {0} off the cart before it ROLLS",
            "{0} you are not cargo get OFF", "{0} the cart cannot hold your DRAMA and the haul",
            "we are not pushing {0} to extraction in the cart again",
            "{0} the cart has a weight limit and it's your EGO", "off the cart {0}, it's not a THRONE",
            "{0} climbed in the cart with the loot AGAIN", "{0} the cart is not a BED we are WORKING",
            "get {0} out of the cart before we sell {0} by accident (tempting)", "{0} stop STEERING with your BODY",
        };

        // Cart ambient (alive, in-level) - drunk rambling that only makes sense with a real cart around, so
        // the engine offers it only when one's actually in the level. Generic cart references that are pure
        // fantasy ("marry the cart") still live in the universal pools; this is the "there's a cart RIGHT
        // here" set.
        internal static readonly string[] CartLines =
        {
            "the cart's right here and i LOVE her", "i'm putting EVERYTHING in this cart watch me",
            "who's pushing the cart. not me. i'm SUPERVISING.", "the cart squeaks and i think it's TALKING to me",
            "i named the cart. again. she's Brenda again.", "load the cart load the CART we're so close to quota",
            "i could fit in this cart. should i. i'm gonna.", "the cart is FULL and my heart is FULLER",
            "do NOT let the cart roll off the ledge again", "i kissed the cart for luck, standard procedure",
            "the cart and i are going STEADY, mind your business", "i'll guard the cart with my LIFE (i won't)",
            "the cart's wobbling. no wait that's me. the cart's fine.", "we lose the cart we lose EVERYTHING, protect Brenda",
            "one more valuable and the cart ASCENDS", "i'm gonna ride the cart down the ramp. for SCIENCE. for QUOTA.",
        };

        // ---- mod-aware (only offered when the relevant mod is loaded on the HOST). Jokes are grounded in
        //   each mod's own description; the ShrinkerGun SELL pool fires only when ShrinkerGun is ABSENT. ----

        // -- Vippy's own --
        internal static readonly string[] ModShrinker =   // ShrinkerGun: shrink enemies/valuables/items/players/vehicles
        {
            "someone SHRINK this thing it's never gonna fit in the cart", "i shrunk my own hand by accident now i grab SMALL",
            "the shrinker gun is the only reason this run isn't OVER", "shrink the monster. SHRINK it. make it a little GUY.",
            "i shrunk a valuable so small i LOST it. it's in my SHOE now probably.", "make me TINY i wanna nap in the cart",
            "i shrunk the WHOLE haul into my pocket we're geniuses", "shrunk a teammate and now he's my little GUY. say hi.",
        };
        internal static readonly string[] ModShrinkerSell =   // only when ShrinkerGun is NOT installed - gentle
        {
            "we should grab the shrinker gun mod for runs like this", "imagine if we could just SHRINK this to fit. someday.",
            "you ever think we need a shrink ray? ...just me? ok.", "this'd be way easier if we could resize stuff. anyway.",
        };
        internal static readonly string[] ModSharedUpgrades =   // SharedUpgradesPlus: host-only chance-based shared upgrades
        {
            "good thing upgrades are SHARED or i'd have NOTHING", "love that we all get the upgrades. we RISE together (into debt).",
            "i bought an upgrade and you ALL got a little something. that's LOVE.", "shared upgrades baby what's MINE is OURS and what's OURS is the TAXMAN'S",
        };
        internal static readonly string[] ModFidelity =   // REPOFidelity: HD graphics, DLSS/FSR, ultrawide, FOV slider
        {
            "the game looks SO good right now or maybe that's the drinks", "everything's so CRISP i could cry. or that's the quota.",
            "real ultrawide and i STILL can't find the extraction", "i cranked the FOV so high i can see my OWN bad decisions",
        };
        internal static readonly string[] ModBattleScars =   // BattleScars: damage shows as cracks/bandages/lost limbs = readable HP
        {
            "i can read your HP by how BUSTED you look and you look BUSTED", "i'm covered in cracks and bandages, that's just my HEALTH BAR now",
            "lost a limb at low HP very FASHION very ON THE NOSE", "you're one dent away from a HEAD, buddy, heal UP",
        };
        internal static readonly string[] ModRobeCalling =   // RobeCalling: the Arctic Phone got a new ringtone/screen
        {
            "the arctic phone is RINGING again who keeps CALLING", "the phone's got a new RINGTONE and honestly? it SLAPS",
            "answer the phone i think it's for the ROBE", "new phone, who dis. it's the robe. it's always the robe.",
        };
        internal static readonly string[] ModCartFix =   // CartFix: loaded carts no longer heavy/slow; mass scales with payload
        {
            "thank GOD the cart turns now i was losing my MIND", "the cart used to drive like a FRIDGE now she GLIDES",
            "loaded cart still handles? we are SPOILED", "i can actually STEER the full cart now we're UNSTOPPABLE",
        };
        internal static readonly string[] ModAimFix =   // AimDeltaFix: fixes mouse sens feeling slow at high framerate
        {
            "my aim finally feels RIGHT at high fps", "the camera stopped feeling like SOUP and i can AIM now",
            "i can actually flick now and i STILL miss the orb",
        };

        // -- popular community --
        internal static readonly string[] ModMoreShopItems =   // MoreShopItems: store carries more items
        {
            "the shop's got so much STUFF now i can't even CHOOSE", "so many shop items i blacked out from sheer CHOICE",
            "more shop items just means more ways to go BROKE i LOVE it", "the shelves are FULL and so is my anxiety",
        };
        internal static readonly string[] ModMoreUpgrades =   // MoreUpgrades: more unique upgrade items, configurable
        {
            "so many upgrades i'm basically a SUPERHERO now (still broke)", "i bought EVERY upgrade and i STILL fell in a hole",
            "more upgrades means i can carry the WHOLE level on my BACK", "i'm so upgraded the taxman sends a PERSONAL invoice",
        };
        internal static readonly string[] ModMoreHead =   // MoreHead: customizable cosmetics
        {
            "check out my HEAD cosmetic i look INCREDIBLE", "i spent the whole budget on a HAT. worth it.",
            "my head's drippin and the quota's CRYING", "i can't do the quota but i CAN accessorize",
        };
        internal static readonly string[] ModHeadHopper =   // DeathHeadHopper: hop around after death (enhanced spectate)
        {
            "i'm DEAD but i can HOP so really who's winning here", "watch me hop my severed head into EXTRACTION",
            "death? i just BOUNCE now. catch me.", "i'm hopping my head toward the cart gimme a SEC",
            "being a head is FREEING when you can do little JUMPS",
        };
        internal static readonly string[] ModLCValuables =   // LethalCompanyValuables: scrap items from Lethal Company as valuables
        {
            "is that a Lethal Company scrap in MY repo level", "we're CROSSING OVER with the OTHER company job",
            "this valuable's from Lethal Company it KNOWS too much", "two companies, one quota, infinite debt. beautiful.",
        };
        internal static readonly string[] ModMimic =   // Mimic: creatures sound like you (Skinwalker-inspired)
        {
            "the monster just talked in MY voice i'm UNWELL", "something out there sounds EXACTLY like greg. it is NOT greg.",
            "if you hear me but my mouth isn't MOVING. run.", "the monster MIMICKED me and honestly did a better job",
        };
        internal static readonly string[] ModMorePlayers =   // MorePlayers: raise/lower the max player cap
        {
            "there's like NINE of us in here it's CHAOS i love it", "more Semibots more PROBLEMS more DRINKS",
            "we raised the player cap and LOWERED our standards", "a whole CROWD and we STILL can't make quota",
        };
        internal static readonly string[] ModExtractConfirm =   // ExtractionPointConfirmButton: confirm button on extraction
        {
            "press the extraction confirm button BEFORE i change my mind", "i'll hit the confirm myself don't you TOUCH it",
            "confirm the extraction i'm gonna CRY happy tears", "one button between us and the truck. press it. PRESS it.",
        };
        internal static readonly string[] ModNoSpawnLimit =   // NoItemSpawnLimit: removes the item spawn cap
        {
            "why are there so MANY items this is HEAVEN", "no spawn limit means INFINITE little treasures i'm UNWELL",
            "the floor is COVERED in stuff and i want ALL of it", "so much loot the cart filed a COMPLAINT",
        };
        internal static readonly string[] ModAdminMenu =   // RepoAdminMenu: admin/host tools menu
        {
            "do NOT tell the admin i'm drunk on the job", "if that admin menu opens we RIOT",
            "i saw the admin menu ONCE and i wasn't the same after", "the admin can just SPAWN money and YET we suffer",
        };

        // -- favorited-dev mods --
        internal static readonly string[] ModLobbyImprovements =   // Dev1A3 LobbyImprovements: various lobby QoL
        {
            "the lobby's so SMOOTH now bless whoever did that", "lobby improvements? in THIS economy? we're SPOILED",
            "joining up is actually PLEASANT now. suspicious.",
        };
        internal static readonly string[] ModLevelScaling =   // Swaggies LevelScaling: configurable difficulty/challenge/run modifiers
        {
            "who CRANKED the difficulty i'm gonna have WORDS", "level scaling's got the monsters BUFF this run, good LUCK",
            "challenge mode? i can barely do REGULAR mode", "we asked for harder and the game said 'bet'",
        };
        internal static readonly string[] ModTimeAttack =   // Swaggies TimeAttack: per-level timer, bonus money for speed
        {
            "the CLOCK is ticking and so is my ANXIETY", "we're on a TIMER move move MOVE (i'm gonna nap)",
            "time attack means i panic-grab EVERYTHING", "go fast for bonus money? i go fast for FEAR.",
        };
        internal static readonly string[] ModDiscordRP =   // Swaggies RepoDRP: Discord Rich Presence with live stats
        {
            "my discord is telling EVERYONE how bad this run is going", "the rich presence says i'm 'extracting'. it's LYING.",
            "everyone on discord can see i've died FOUR times. cool. cool cool cool.", "live stats means there are WITNESSES to my crimes",
        };
        internal static readonly string[] ModResell =   // Jettcodey ResellYourUpgrades: sell/refund applied upgrades
        {
            "wait we can RESELL upgrades? i'm returning my LEGS for cash", "refunding my upgrades to afford ONE more drink",
            "sold my upgrade back, the taxman LOVES a returner", "i can un-buy my mistakes now. emotionally, i cannot.",
        };
        internal static readonly string[] ModShopDiscounts =   // Jettcodey ShopDiscounts: random/set shop discounts
        {
            "DISCOUNT day baby everything must GO into my cart", "rolled a discount i'm basically a FINANCIAL genius",
            "the shop's on SALE and the taxman is FURIOUS", "i'd buy it full price but a DEAL is a deal is a DUTY",
        };
        internal static readonly string[] ModReturnToLobby =   // OrigamiCoder ReturnToLobby: host button to return to lobby
        {
            "host can just YEET us back to lobby don't TEMPT them", "one button and we're HOME please don't press it yet i'm RICH",
            "return to lobby? i'm not DONE losing money here",
        };
        internal static readonly string[] ModNuke =   // BLOKBUSTR LegalizeNuclearBombs: a configurable nuke valuable
        {
            "who brought a NUKE to a salvage job. respect.", "we have a NUKE. a NUKE. this can only end WELL.",
            "i'm not SAYING nuke the building but the nuke is RIGHT there", "the most valuable thing here is ALSO a war crime. love it.",
        };
        internal static readonly string[] ModOrbTweaks =   // BLOKBUSTR EnemyValuableTweaks: makes the orb fairer/more interesting
        {
            "the orb's actually FAIR now i don't TRUST it", "someone made the orb REASONABLE, what timeline is this",
            "the orb and i have a NEW understanding and i'm still scared",
        };
        internal static readonly string[] ModUnknownUpgrades =   // OrigamiCoder UnknownUpgrades: hides upgrade names/textures
        {
            "i bought a MYSTERY upgrade could be ANYTHING could be NOTHING", "every upgrade's a surprise now, i love GAMBLING",
            "i don't know what this upgrade DOES and neither do YOU", "blind-bought an upgrade for the THRILL. it did nothing. THRILLING.",
        };
        internal static readonly string[] ModPlayerSpeed =   // Jettcodey PlayerSpeedConfig: custom walk/sprint/crouch speed
        {
            "i set my speed to RIDICULOUS catch me NEVER", "i'm so FAST now the monsters can't even keep UP",
            "custom speed baby i walk like i'm being CHASED (i'm not)", "i tuned my sprint so high i ran PAST the extraction. twice.",
        };

        // LateJoin - ribbed for being janky-by-design; the joke leans toward ReturnToLobby, comedically.
        internal static readonly string[] ModLateJoin =
        {
            "late join worked! ...i think. why is my head on the CEILING.", "someone joined mid-run and now physics is just a SUGGESTION",
            "late join's held together with TAPE and PRAYER but welcome aboard", "honestly we should just RETURN TO LOBBY but where's the FUN in stable",
            "fresh joiner spawned INSIDE the cart. classic late join. love this buggy king.", "late join is chaos. return-to-lobby is for COWARDS (and people who like working games)",
        };

        // Catch-all for any cheaty / easy-mode mod present - the crew gets a little self-aware.
        internal static readonly string[] ModEasyMode =
        {
            "are we even playing the REAL game anymore with all these mods", "we modded the game EASIER and i'm STILL stressed",
            "this many helper mods and we might STILL lose lmao", "the taxman filed a COMPLAINT about how easy we've got it",
            "with all these mods the only challenge LEFT is ME", "we've got mods on mods on MODS and zero quota to show for it",
        };

        // PROCEDURAL fallback - "{0}" = a loaded mod's display name we DIDN'T hand-write a pool for. Never
        // claims what the mod does (we don't know), so it's safe for literally any mod. The engine fills it
        // from BepInEx's live plugin list, so mods you add later get referenced with zero code changes.
        internal static readonly string[] ModProcedural =
        {
            "honestly? {0} is carrying this whole run", "whoever made {0} deserves a RAISE",
            "i don't even know what {0} DOES but i TRUST it", "we'd be lost without {0}. probably. what's it do again?",
            "shoutout to {0}, the real MVP of this debt", "{0} is installed and the vibes are accordingly IMMACULATE",
            "i'm pretty sure {0} is the only reason we're still alive", "modded to HELL and back and {0} is part of why",
            "{0}? in MY repo? it's more likely than you think", "god bless {0} and god bless whoever made it",
            "i added {0} last night and remember NOTHING but we're THRIVING", "if {0} ever breaks i am simply LOGGING OFF",
            "{0} doing the lord's work back there. whatever that work IS.", "i'd read the {0} description but i can't READ right now",
        };

        // ---- event reactions (fired the instant something happens to a drunk Semibot) ----
        internal static readonly string[] DeathReact =
        {
            "well i'm DEAD. anyway. revive me?", "i regret EVERYTHING (mostly the cart)",
            "down i go. tell my print body i loved it.", "REVIVE me i had so much more to DROP",
            "i died doing what i love: this, poorly", "scoop my head up i am NOT done",
            "this is NOT how i wanted to go (i wanted to go RICHER)", "revive me i have UNFINISHED debt",
            "guys. GUYS. i'm a head again. classic me.", "put me back together i have NOTES for the taxman",
        };
        internal static readonly string[] DeathReactExtraction =
        {
            "revive me by the extraction i wanna be CLOSE to the money", "drag my head to the extraction and hit confirm for me",
            "revive me at the cart i'll feel SAFER (i won't)", "bring me to extraction i wanna SEE the haul one last time",
        };
        internal static readonly string[] DownReact =
        {
            "i'm DOWN someone GRAB me", "help i'm doing the floor thing again", "pick me UP this is UNDIGNIFIED",
            "i'm not napping i'm TUMBLED, grab me", "down bad. literally. on the floor. send help.",
            "i can't get up the gravity is TOO MUCH right now", "grab my hand. grab my ankle. grab SOMETHING.",
            "i'm a turtle now. flip me over. i believe in you.", "i've fallen and i CAN get up i just don't WANT to",
            "this is fine. i live here now. on the floor. anyway grab me.", "i'm having a little GROUND moment everybody relax (and help)",
        };
        internal static readonly string[] HeadmanGrabReact =
        {
            "the HEADMAN'S got me tell my cart i love her", "i'm in the BAG send the cavalry",
            "the headman picked ME of all people i feel SPECIAL and DOOMED", "shoot the head off me i TRUST you (please be sober)",
            "the headman's got my WHOLE skull, help", "kidnapped by a floating HEAD. so on brand for me.",
        };
        // The Hidden - invisible, wheezing. Everyone calls him the pervert / the asthmatic.
        internal static readonly string[] HiddenGrabReact =
        {
            "the PERVERT'S got me again i can HEAR him breathing", "something invisible GRABBED me and it's WHEEZING",
            "the asthmatic one found me HELP he needs an INHALER and a restraining order", "can't SEE it but i can HEAR it breathing on my NECK",
            "the heavy breather strikes AGAIN i did NOT consent to this", "invisible hands. wheezing. you KNOW who. HELP.",
            "the pervert dragged me off again, find me by the BREATHING", "being carried by something i can't SEE and it will NOT stop PANTING",
            "follow the wheeze. FOLLOW the wheeze. save me from the asthmatic.", "invisible creep alert he's GOT me and he is OUT of breath",
            "i'm getting kidnapped to the sound of LABORED BREATHING send backup",
        };
        internal static readonly string[] SmashReact =
        {
            "i dropped it. i DROPPED it. don't look at me.", "that was worth HOW much. don't tell the taxman.",
            "butterfingers strikes again sorry sorry SORRY", "it slipped! it SLIPPED. physics did that, not me.",
            "RIP that valuable it died doing what it loved: getting dropped by me", "i can fix it. i can't fix it. i'm so sorry.",
            "in my defense it was already kinda broken (it was not)", "we do NOT talk about the number that just left the haul",
        };
        internal static readonly string[] ScaredReact =
        {
            "something WOKE UP and it wasn't my better judgment", "i heard a noise i'm CHOOSING to ignore it",
            "monster's awake. act natural. act NATURAL.", "nope nope NOPE i'm going the other way",
            "did anyone else hear DEATH approaching or just me", "i'm gonna hide. forever. with the cart.",
        };
        internal static readonly string[] CheerReact =
        {
            "WE DID IT we're slightly less BROKE", "haul secured KISS the cart", "SURPLUS baby drinks are on the COMPANY",
            "we EXTRACTED i could KISS all of you (i won't)", "money money MONEY ok i'm crying", "take THAT taxman we LIVE another quota",
            "we made quota i'm gonna go lie down for a WEEK", "did we WIN? we didn't LOSE. in this economy that's a WIN.",
        };

        // ---- wild reaction tiers (stage >= 3): the same moments, but unhinged ----
        internal static readonly string[] DeathReactWild =
        {
            "i'm DEAD and the void has a SUGGESTION BOX and i'm FILLING it", "reprint me with BETTER knees this time you COWARDS",
            "i ascended. i'm a head. i can see the QUOTA'S soul.", "death is just LOGGING OFF i'll be RIGHT back babe",
            "tell the taxman i'll pay him in the NEXT life (i won't)", "i'm not dead i'm BUFFERING revive me to FINISH downloading",
        };
        internal static readonly string[] DownReactWild =
        {
            "GRAVITY and i are BEEFING and gravity is WINNING", "i'm one with the FLOOR now we share SECRETS pick me UP",
            "i'm not down i'm doing RESEARCH on the carpet GET me", "the floor PROPOSED and i said yes now flip me over",
            "i CHOSE this. i did NOT choose this. pick me UP.",
        };
        internal static readonly string[] HeadmanGrabReactWild =
        {
            "the headman and i are ENGAGED now plan the WEDDING or shoot him, dealer's choice", "i'm in the bag and honestly? cozy. still SAVE me.",
            "the floating SKULL whispered the quota to me and it LIED", "abducted by a HEAD with COMMITMENT issues, HELP",
            "tell the head i need at least DINNER first",
        };
        internal static readonly string[] HiddenGrabReactWild =
        {
            "the pervert and i are DATING now he NEVER stops breathing it's a RED FLAG", "carried off by an INVISIBLE asthmatic and we're MOVING IN TOGETHER send help",
            "can't SEE him but i can FEEL the wheeze in my SOUL", "the heavy breather took me to his LAIR (a corner) (still panting)",
            "follow the sound of LABORED LONGING it's GOT me", "invisible. wheezing. CLINGY. you KNOW who. extract me from this RELATIONSHIP.",
        };
        internal static readonly string[] SmashReactWild =
        {
            "i didn't drop it the FLOOR ASSAULTED it", "that valuable CHOSE to die it was BRAVE i merely ENABLED it",
            "the taxman FELT that one i felt the taxman FEEL it", "i'm gonna drop MORE just to feel SOMETHING",
            "shattered dreams AND shattered valuables, my SPECIALTY",
        };
        internal static readonly string[] ScaredReactWild =
        {
            "DEATH is awake and i am NOT emotionally prepared", "something is COMING and it is NOT my paycheck",
            "the monster's up i'm gonna go become ONE with this locker", "nope. NOPE. i'm cashing out my LIFE early.",
            "i heard the SCREAMING and i choose VIOLENCE (the violence is fleeing)",
        };
        internal static readonly string[] CheerReactWild =
        {
            "WE'RE RICH (debt-adjacent) KISS the CART KISS the taxman NO not him", "haul SECURED i'm gonna WEEP into the surplus",
            "we LIVED take THAT to the company's FACE", "i'm gonna FRAME this haul and HANG it in the void",
            "EXTRACTED and EMOTIONAL drinks for the WHOLE printed family",
        };

        // ---- spewer: the slow-mouth latched onto a face ----
        // It attaches to your face and hoses everyone in front of you. SpewerFace = it's on YOU; SpewerPuke =
        // the thing on your face just unloaded on the crew. (The pervert is the Hidden; this one's the mouth.)
        internal static readonly string[] SpewerFaceReact =
        {
            "there is a MOUTH on my FACE and it is NOT mine", "the spewer picked me as a HAT get it OFF",
            "i can't SEE there's a whole face-mouth on my face", "something latched on and it's DROOLING down my visor",
            "the mouth-thing's got me i'm a PEZ dispenser now", "get this off my FACE i can taste its OPINIONS",
            "i've got a passenger and it is GURGLING", "the spewer chose ME. of all the faces. RUDE and FLATTERING.",
            "i have a SECOND mouth now and it's the LOUD one", "it's on my face and it will NOT stop introducing itself",
            "blind. drooled on. on the clock. living the DREAM.", "the spewer and my face are in a SITUATION right now",
            "why is it WARM. why is the face-mouth WARM. PEEL it off.", "i look like a HORROR movie and i can't even SEE it",
        };
        internal static readonly string[] SpewerFaceReactWild =
        {
            "the mouth on my face and i are FUSING we share THOUGHTS now and they're WET", "i'm a two-mouth Semibot now BOTH of us are screaming",
            "the spewer is my new FACE and honestly it emotes BETTER", "i kissed back. i don't know why. we're MARRIED now. it's on my FACE.",
            "i can't see but i can FEEL the gurgle in my SOUL, get it off or DON'T, i'm conflicted",
            "my face has a TENANT now and it does NOT pay the quota", "the spewer whispered the haul total into my SKULL and it was WRONG",
            "i'm not blind i just see through the FACE-MOUTH now, it's fine, it's FINE",
        };
        internal static readonly string[] SpewerPukeReact =
        {
            "{0}'s face-mouth just HOSED all of us", "EVERYONE back up {0} is spraying", "{0} barfed on the whole crew and it wasn't even {0}",
            "the thing on {0}'s face just UNLOADED, i'm DRENCHED", "{0} please AIM the face-mouth AWAY from the haul",
            "we all got {0}'d. it's in my JOINTS now.", "{0} is a sprinkler of regret right now everybody SCATTER",
            "do NOT stand in front of {0} that's a LESSON now", "{0}'s got a firehose for a face and zero AIM",
            "the haul is COVERED in whatever came out of {0}", "{0} turned into a SPRINKLER and ruined the moment",
            "i made eye contact with {0} at the WRONG time", "{0} hosed the cart, the loot, AND my will to live",
        };
        internal static readonly string[] SpewerPukeReactWild =
        {
            "{0} HOSED us like a FIRE TRUCK of pure REGRET and i RESPECT the range", "i've been BAPTIZED by whatever's on {0}'s face and i feel REBORN and DISGUSTING",
            "{0}'s face-mouth painted the WHOLE room we're all part of the ART now", "we are MARINATING in {0}'s situation and the taxman is gonna SMELL it",
            "{0} sprayed so hard the QUOTA flinched everybody get UNDER something",
            "{0} achieved a SPLASH ZONE and we are all SEASON TICKET HOLDERS", "i'll be finding bits of {0}'s situation in my JOINTS for THREE print runs",
        };

        // ---- the shadow (EnemyShadow: reaches/stretches out and slaps you flat) ----
        internal static readonly string[] ShadowReact =
        {
            "the shadow just SLAPPED me like a MOSQUITO", "it STRETCHED its arms out and got me, i'm so FLAT now",
            "got clapped between two big creepy shadow hands, send help", "the shadow went CLAP and i went FLAT, anyway",
            "it reached ALL the way across the room for me. dedication.", "i've been APPLAUDED to death by the shadow, ten outta ten",
            "the shadow stretched and SLAPPED and now i'm a PANCAKE", "clapped. flattened. humbled. by the SHADOW.",
            "the shadow's arms are NOT that long. they were that long. i was WRONG.", "got backhanded by a SILHOUETTE and i have QUESTIONS",
            "it stretched across the WHOLE room to slap ONE guy. that guy was me.", "the shadow clapped once. i felt it in my PRINT NUMBER.",
            "note to self: the dark corner SLAPS. literally. it slaps.", "the shadow gave me a high-five at MACH SPEED and i did NOT consent",
            "it just kept STRETCHING. when does an arm STOP. it didn't.", "i turned around and the shadow was already MID-CLAP. rude.",
        };
        internal static readonly string[] ShadowReactWild =
        {
            "the shadow gave me a STANDING OVATION (the standing part was IT, the ovation FLATTENED me)", "i got SLAPPED across the WHOLE map, the shadow does NOT respect personal space OR physics",
            "the shadow stretched out like taffy and folded me like LAUNDRY", "i'm 2D now thanks to the shadow, tell the taxman i'm easier to FILE",
            "CLAP. that's it. that's the whole relationship. me and the shadow.",
            "the shadow and i have a SLAP-BASED relationship now and i STAY", "it stretched its arm from the SHADOW REALM to flatten me, work-life balance WHO",
            "CLAP heard round the level, my soul left, my body's a DOORMAT now", "the shadow reached through THREE walls and a CONTRACT clause to slap me",
            "i've been folded so many times i'm basically ORIGAMI for the haul", "the shadow slapped me into the NEXT difficulty tier",
        };

        // ---- arena: king of the losers (post-wipe) ----
        // Ambient (alive, in the arena) and the reaction tiers for eliminations / last-standing / the crown.
        internal static readonly string[] ArenaLines =
        {
            "oh we're doing the LOSER game again. i'm built for this.", "king of the LOSERS. finally a title i can WIN.",
            "last one standing gets the crown. it's me. it's gonna be me. (it's not)", "the floor's gonna fall again isn't it. it's gonna FALL.",
            "i grabbed a gun off the floor i have NO idea what it does", "we all DIED and now we FIGHT about it, love that for us",
            "the crown's right THERE. i just gotta outlive you idiots.", "platform's shrinking AGAIN who keeps doing that",
            "i will betray every one of you for that crown. respectfully.", "this is just the bar fight after the funeral and i'm THRIVING",
            "don't fall don't fall don't f-- ok i'm falling", "whoever made the crown a PRIZE knew their audience (us. losers.)",
            "i found a melee weapon. i am now a THREAT. a small one.", "the loser arena is the only place i've ever felt SEEN",
        };
        internal static readonly string[] ArenaDead =
        {
            "out of the loser game. a loser among losers. peak me.", "knocked off the platform, see you in the truck",
            "i lost the LOSER game. that's a new low even for me.", "ghosting the arena now, cheer for whoever's left i GUESS",
            "fell off. tell the crown i loved it from afar.", "eliminated. anyway who's winning. is it not me. shocking.",
            "i'm just gonna watch from the void and JUDGE", "dead in the arena, again, you'd think i'd be GOOD at dying by now",
            "out here losing the game built for LOSERS. impressive, honestly.", "i'll be in the void if anyone needs a HYPE corpse",
            "voted myself off the platform apparently", "the crown will NEVER know my name and that's the realest part",
        };
        // Arena ambient, higher stages (wasted+): the same loser-game energy, fully unhinged.
        internal static readonly string[] ArenaWild =
        {
            "i'm gonna WIN the loser game and ASCEND into a HIGHER loserdom", "the crown is calling my PRINT NUMBER specifically, i can HEAR it",
            "i'll throw EVERY one of you off this rock and call it TEAMWORK", "the platform's shrinking because it FEARS me (or it's physics)",
            "i am BUILT from a THOUSAND lost runs and i WANT that crown", "betrayal? in the LOSER arena? more likely than making QUOTA",
            "i licked the crown cage. it's MINE now. legally. (it is not.)", "the void is just the truck with EXTRA STEPS and i RULE here",
            "i'm not falling off, i'm DESCENDING with INTENT", "give me the crown or the OTHER crown, there's one crown, GIVE IT",
            "everyone down here is a LOSER except me, also me, mostly me", "the floor is lava the walls are lava the QUOTA is lava i'm THRIVING",
        };

        // Generic (NOT templated) - the elimination RPC only carries the survivor count, not who fell, so
        // these never name a victim.
        internal static readonly string[] ArenaDeathReact =
        {
            "someone just got YEETED off the platform", "down goes another loser, gravity stays undefeated",
            "another one off the edge, anyway", "and another loser bites the void",
            "somebody fell. pour one out. (we're already pouring.)", "one less Semibot between me and that crown",
            "off the platform you go, buddy, see you in the truck", "the void's filling up with us losers",
            "somebody just ate it HARD out there", "another contestant eliminated, the crown waits",
        };
        internal static readonly string[] ArenaDeathReactWild =
        {
            "someone got LAUNCHED into the void like a CROUTON off a salad", "another loser FELL and i swear the crown LAUGHED",
            "the void just got a NEW tenant and zero rent control", "down goes somebody in a BLAZE of absolutely no glory",
            "the platform SPAT another one out, the loser game feeds",
            "someone exited STAGE DOWN, very dramatic, zero points awarded", "the platform held a VOTE and somebody LOST, violently",
            "another Semibot speedran the EDGE, no survivors, no dignity",
        };
        internal static readonly string[] LastStandingReact =
        {
            "it's just {0} left. {0}?? against ALL odds??", "LAST ONE STANDING is {0}, the crown's right there {0} GRAB it",
            "everyone's down but {0}. {0} this is your MOMENT.", "{0} is the last loser breathing, touch the crown {0} TOUCH it",
            "down to {0}. nobody bet on {0}. nobody.", "{0} outlived us ALL, go get the crown you beautiful disaster",
            "{0} is the FINAL loser standing, the bar was on the FLOOR and {0} CLEARED it", "it's {0}. somehow {0}. the prophecy nobody bothered to write.",
            "don't choke now {0}, the crown is RIGHT THERE", "watch {0} fumble this in REAL time (do NOT fumble it {0})",
        };
        internal static readonly string[] LastStandingReactWild =
        {
            "ONE Semibot left and it's {0}?? the SIMULATION is broken {0} go CLAIM it", "{0} is the sole SURVIVOR of the loser games, the prophecy (that nobody made) is FULFILLED",
            "everyone's a corpse except {0}, the crown TREMBLES, {0} GO", "it all comes down to {0}, somehow, against god and the platform, GRAB the crown {0}",
            "last one breathing: {0}. the void is SO disappointed. i'm so PROUD. GO.",
            "{0} STANDS ALONE atop a mountain of LOSERS (it's us, we're the mountain)", "the universe CHOSE {0} and the universe has clearly been DRINKING",
            "{0} survived on SPITE and bad decisions ALONE, crown that disaster IMMEDIATELY",
        };
        internal static readonly string[] CrownWinReact =
        {
            "ALL HAIL {0}, KING OF THE LOSERS", "{0} grabbed the crown!! {0} is our IDIOT KING now",
            "bow to {0}, ruler of nothing, winner of the LOSER game", "{0} touched the crown FIRST, drinks for the KING",
            "we have a KING and it's {0}, god help us", "{0} won the loser games, put it on the resume",
            "the crown chose {0}. the crown has TERRIBLE taste. long live {0}.", "{0} is crowned, the rest of us are just SUBJECTS now (and broke)",
        };
        internal static readonly string[] CrownWinReactWild =
        {
            "{0} SEIZED the crown and i for one welcome our new LOSER OVERLORD", "KNEEL. {0} is KING. of LOSERS. of the VOID. of my HEART. of NOTHING.",
            "{0} grabbed the crown and ASCENDED, the taxman now reports to {0} (he doesn't, but let {0} dream)", "LONG LIVE KING {0}, may your reign be brief and your bar tab ETERNAL",
            "{0} is crowned and POWER-DRUNK and regular-drunk, a DOUBLE threat, ALL HAIL",
            "{0} won a CROWN for losing the LEAST, the highest honor we HAVE", "bow to {0}, monarch of the MEDIOCRE, sovereign of the SAD",
            "{0} took the crown and IMMEDIATELY owes more taxes, congrats your majesty",
        };

        // ---- the crowned king, name-dropped in regular runs (they wear the vanilla crown) ----
        // CrownCrew = said by anyone about the king; CrownSelf = the king bragging. "{0}" = king's name.
        internal static readonly string[] CrownCrewLines =
        {
            "all hail {0} i GUESS, king of the losers", "{0}'s still wearing the crown like it MEANS something",
            "yes your majesty {0}, shall i carry your debt too", "{0} won ONE loser game and won't shut up about the crown",
            "bow to {0} or whatever, mind the quota your highness", "the king ({0}) cannot even make quota but sure, the CROWN",
            "{0} rules over us, a kingdom of NOTHING and TAX", "long live {0}, may the reign end before the next wipe",
            "{0}'s got a crown and a bar tab, balanced rulership honestly",
        };
        internal static readonly string[] CrownSelfLines =
        {
            "i'm the KING remember. of losers. but KING.", "as your crowned monarch i decree: GET the haul",
            "bow before me i won the LOSER game once", "the crown's still mine. nobody's wiped since. checkmate.",
            "royalty does NOT do quota. ...fine. royalty does quota TOO.", "i wear the crown AND the bar tab, a balanced king",
            "kneel. or don't. i'm a chill king. but mostly kneel.", "still your king. still broke. still GLORIOUS.",
        };

        // ---- arena RACE variant (ArenaRace: a drunk vehicle dash to the crown) ----
        // Ambient while racing (engine offers these when ArenaRace.instance is live), plus the finish-line
        // coronation and the knocked-out jeers.
        internal static readonly string[] RaceLines =
        {
            "it's a RACE? oh no, i drive like i DRINK", "give me a vehicle and a DREAM and watch me crash BOTH",
            "first to the crown wins, last one's ME, traditionally", "i'm not LAST i'm in REVERSE, strategically",
            "the track is a SUGGESTION and my brakes are a MYTH", "vroom. vroom. that's the whole strategy.",
            "who gave the drunk a VEHICLE (it was me, i gave me the vehicle)", "i'll win this race or DIE trying (probably the second one)",
            "racing for a CROWN at a salvage job, normal tuesday", "drift? i only know how to SPIN OUT majestically",
            "the crown's up front and so is everyone who ISN'T me", "i kissed the wheel for luck, we're MARRIED now, FLOOR it",
            "i'm taking the shortcut (there is no shortcut) (i'm in a wall)", "last-lap energy and i haven't done the FIRST lap",
        };
        internal static readonly string[] RaceWild =
        {
            "i AM the vehicle now, there is no me, only SPEED and REGRET", "i'm gonna ram the crown-holder into the SHADOW REALM, sorry not sorry",
            "physics RESIGNED and left ME in charge of this vehicle, godspeed", "i'm driving with my EYES CLOSED for the VIBES and the QUOTA",
            "the finish line is a STATE OF MIND and my mind is GONE", "i'll lap you ALL or i'll lap MYSELF, either way LAPS",
            "i traded my brakes to the taxman for one more drink, no regrets (some regrets)", "the crown is mine the vehicle is mine the road is mine the WALL is also mine",
            "i'm not crashing i'm doing AGGRESSIVE PARKING at 90", "VROOM is a LIFESTYLE and i am its MESSIAH",
            "i'm gonna win this drunk, sober me could NEVER", "the steering wheel and i had a FIGHT and the WALL won",
        };
        internal static readonly string[] RaceWinReact =
        {
            "{0} took the checkered flag AND the crown, ALL HAIL", "{0} WON the race?? {0} drives like a HAZARD though",
            "first across the line: {0}, king of the loser GRAND PRIX", "{0} lapped us all into next quota, bow to the racing king",
            "{0} crossed the finish and got CROWNED, drinks for the speed demon", "the crown goes to {0}, who somehow DIDN'T crash, incredible",
            "{0} WINS, the rest of us are still finding the FIRST turn", "podium of ONE and it's {0}, the fastest loser alive",
        };
        internal static readonly string[] RaceWinReactWild =
        {
            "{0} won the grand prix of LOSERS and i cannot FEEL my face", "{0} crossed the line at MACH JESUS and seized the crown, KNEEL",
            "{0} is the racing KING now, the taxman's gonna invoice the WHOLE podium", "{0} drove so unhinged the crown filed a COMPLAINT and then SUBMITTED, all hail {0}",
            "ALL HAIL SPEED KING {0}, may the pit stops be merciful and the bar tab ETERNAL",
        };
        internal static readonly string[] RaceOutReact =
        {
            "someone just got LAPPED out of existence", "another racer ATE the wall, gg",
            "down a racer, the crown does not WAIT", "someone's vehicle AND dreams both exploded",
            "one less bumper between me and that crown", "another loser DNF'd into the void",
            "somebody spun out SPECTACULARLY out there", "a racer just got DELETED off the track",
            "and they're OUT, the track claims another", "someone found the wall at FULL speed, rip",
        };
        internal static readonly string[] RaceOutReactWild =
        {
            "a racer got LAUNCHED off the track like a champagne cork of FAILURE", "someone CARTWHEELED off the course in a blaze of zero glory",
            "another vehicle became MODERN ART against the wall", "the track ATE one and BURPED, the loser games feed",
            "someone exited the race stage-left at TERMINAL velocity",
        };
    }
}
