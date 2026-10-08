# Cloud brief: M2, the rules of Part 2

> **Read first (2026-10-08).** This brief was written against `PLAN.md` §12.25. The order has changed (§12.27): Part 2's rules now arrive with the map that needs them. Napalm and Air Cav (Task 2, items 1 and 2) come first, with Ia Drang; weather, Arc Light and the RPD team with Khe Sanh; the rest later. The game now runs with `tactics fortune` on as well: measure with `... ammo tactics fortune tempo` and `player 48 29 rate=0.9 tactics fortune`, and write up as the next free section after §12.27.

Simulation work for milestone M2 of `PLAN.md` §12.25 (rows 2a, 2c and 2d), written on 2026-10-05 against `main` at `9941454`. You are a cloud session: no Unity, no GPU, no licensed assets. Read CLAUDE.md first (it says what you can run and the rules the project keeps), then README.md, then PLAN.md §12.8 (what the 2D game has and what Part 2 owes it, and the policy for new mechanics), and §12.15 to §12.25 (the rules that exist and how each was built and measured). Work on a new branch `cloud/m2-sim` from `main`; push it after every task; never merge into `main` and never push to `main`.

Start by installing the .NET 8 SDK, building `tools/simcs` and checking that a clean `main` gives the numbers CLAUDE.md lists. If it does not, stop and report that instead of going on.

Only the rules are yours. Each task below is a rule in the simulation behind its own new `MatchOptions` flag, off by default. With the flag off every existing pin is unchanged to the bit, on every layer (`simcs hash`). The game does not turn any of them on yet: do not edit `View/GameRoot.cs` and do not write view code; list what the view will need to show each rule (new events, new state it must read, new cards and what they say).

The 2D game's numbers do not transfer (§12.8, point 5): its units had hit points and this simulation has none. Translate the intent, then set costs and cooldowns by measuring.

## Task 1: weather (row 2a)

A schedule made from the match's seed: stretches of clear, rain and fog with their start and end ticks, drawn from the simulation's `Rng` in a way that does not disturb any draw made when the flag is off. `SimState.Sight` is already the hook (how far anyone sees against a clear day's 1); the schedule drives it, with a ramp in and out rather than a step. Rain and fog shorten sight by different amounts; rain also slows men a little and makes a shot heard from less far. An event when the weather turns, carrying what it turns to and how long the ramp is, so the view can bring in the sky, the rain and the sound.

Done means: the same seed gives the same weather; a test that sight never jumps by more than the ramp allows from one tick to the next; `aware` taken in clear and in fog shows men spotting each other later and closer in fog (say by how much); and the balance block below shows neither side's wins falling outside the band.

## Task 2: three call-ins (row 2d)

New cards for the Americans, in the deck only when the flag is on, each with its own flag or one flag for the three if they share their machinery (say which and why):

1. **Napalm.** A strip that burns for a time. Men in it die or run; nobody enters it while it burns; and where it burned, concealment is gone for the rest of the match: a man there is seen as if he stood in the open, and natural cover in the strip no longer hides (built cover still stops bullets). Events for the drop, the burning strip and its end.
2. **Air Cav.** Helicopters put a squad down at a place the player chooses, within a limit from his own front line, after a delay during which the place is marked and can be shot at. Events for inbound, landing and away, so the view can fly the Hueys.
3. **Arc Light.** A line of bombs across the lanes at a chosen depth, with a long warning (both sides get the event) and the longest cooldown and cost in the deck. It kills in the open and breaks cover it lands on.

The computer's plan must be able to buy and place each one with a reason (napalm on concealed men it cannot see into, Air Cav where a lane is empty, Arc Light on a mass).

## Task 3: new units (row 2c)

1. **The ARVN squad** (American side): a cheaper line squad, less steady under fire than a US rifle squad.
2. **The LRRP team** (American side): two or three men who see further than anyone and are hard to see, and whose sightings count for every American squad in their lane (through `Senses`: what one squad knows, the others in the lane may fire on). They avoid a fight.
3. **The RPD team** (VC side): the M60 team's opposite number; the RPD already has its kit and its ammunition in `Arms.cs` and `Ammo.cs`.
4. **The M113**: one vehicle, slow, that small arms cannot kill, that a rocket, a satchel charge or a mortar round can, and that carries a squad's worth of protection for the men walking behind it. It needs an armour rule; write it so a second vehicle later costs a table row, not a rewrite. If satchel charges do not exist on `main` when you get here, give the M113 its weakness to them behind the same flag and say so.

## For every task

- A new pinned case with the flag on, with the `simcs hash` line that gave it; tests beside the others in `Assets/_Project/Tests/EditMode/SimTests.cs` for the rule's own promises.
- `simcs audit` shows every new event fires; `simcs` accepts the new flag by name wherever it accepts the others.
- A balance block of 48 seeds: `player 48 29 rate=0.8` with the flag off, then on. With the flag on, a player who buys line squads should still win between about 14 and 20 of 24 a side at this cost (28 to 40 of 48); if he does not, change the new card's cost or cooldown, not `GameRoot.CostFor` or `RateFor`, and say what you changed and why.
- One commit a task, pushed, before the next one starts.

Another cloud session may be working on `cloud/m1-sim` at the same time. It changes how squads behave under the existing rules, which moves the Senses, Gunnery and Ammo pins. Your pins are recorded on `main` as you found it; say so in your hand-over, so the Mac records them again after the two branches meet.

## What to hand back

As CLAUDE.md's "A cloud session's hand-over" says: the branch pushed; new pins with the `simcs hash` line that gave each; a new section **§12.27** in `PLAN.md`, after §12.25 and in the style of §12.24 (leave §12.26 to the other session), with what was asked, what was built, every measurement and the command that gave it, and what is left for the Mac. Numbers come from the tool; do not round them into a better story. If something could not be done or was not checked, say so plainly in that section.

Your last message gives: the branch and its last commit; for each task, whether it is done, partly done or not done; the balance blocks; the new flags, events and cards by name; and anything that stopped you (being unable to push, for one).

Things you must not do: change the parity baseline or any existing pin; add a download of any kind other than the .NET SDK; commit anything under `Assets/_Licensed/` or `StreamingAssets/Audio/licensed/`; touch `main`.
