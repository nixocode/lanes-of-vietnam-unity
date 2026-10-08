# Cloud brief: M1, the simulation's share

> **Read first (2026-10-08).** This brief was written against `PLAN.md` §12.25. Since then playtest 9 (§12.26) has done Task 1's work another way, behind two new flags (`Tactics`, `Fortune`): do not do Task 1. Task 2's three rules are still wanted (§12.27, row P2), measured with those two flags on (`... ammo tactics fortune tempo`, `player 48 29 rate=0.9 tactics fortune`) and written up as the next free section after §12.27. Bring the numbers below up to date before starting.

Simulation work for milestone M1 of `PLAN.md` §12.25 (rows 1a and 1d), written on 2026-10-05 against `main` at `9941454`. You are a cloud session: no Unity, no GPU, no licensed assets. Read CLAUDE.md first (it says what you can run and the rules the project keeps), then README.md, then PLAN.md §12.18 to §12.25. Work on a new branch `cloud/m1-sim` from `main`; push it after every task; never merge into `main` and never push to `main`.

Start by installing the .NET 8 SDK, building `tools/simcs` and checking that a clean `main` gives the numbers CLAUDE.md lists. If it does not, stop and report that instead of going on.

The game runs with every Part 2 flag on. Everything below is measured that way.

## Task 1: the AI's two loose ends (row 1a)

Under the rules that already exist (Senses, Gunnery, Ammo on), two things still look wrong in play:

1. Men end up pinned in the open two or three metres in front of a wall, a bunker or a log they had just walked past, instead of using it.
2. A squad strings out at its own edge of the map when it comes on, and its men stand in the open there.

Find the cause of each in `Assets/_Project/Scripts/Sim/` (`Senses.cs`, `Squads.cs`, `Match.cs`, `Fieldcraft` code) and fix it. `simcs watch <seed> <from> <to> [every] ... senses` prints each squad's task, threat and men tick by tick, and is the way to look at one moment.

Measured on `main` before you start, with `aware 3 2400 6 fieldcraft arms senses gunnery ammo tempo` and `muddle` with the same arguments (a minute of fighting, six seeds):

- idle in the open by cover: 128.4 man-seconds (9% of all), of which 34.1 with a seen enemy in range; waiting behind cover their squad has filled: 18.5
- contacts to the first shot: median 4.0 s, nine in ten within 8.1 s
- muddle: turned about 13.4; a man sets off 5.5 times a minute, 1.7 of them hops under a second; a squad changes order 6.0 times and cover 5.2 times a minute
- shots across the lanes 18%; at a squad the firer's own has not in sight 0%

Done means: "idle in the open by cover" is clearly lower (aim for a third off or better) on those six seeds and on a second block of seeds you did not tune on (`aware 20 2400 6 ...`), and none of the muddle lines, the time to the first shot, or the two shot shares got worse by more than a few percent. If a fix buys less idling with more dithering, it is not a fix; say what you tried.

This task changes behaviour under existing flags, so the pins of the Senses, Gunnery and Ammo layers in `Assets/_Project/Tests/EditMode/SimTests.cs` will move, and the layers below Senses must not: prove that with `simcs hash` on each layer before and after. Record the new pins. One thing that has caught this project before: the pinned cases built by `Armed(seed, onMap)` set Frag and SquadSmoke only when `onMap` is true, so a case that is not on the map is recorded without `frag smoke` on the command line.

Then measure balance again with `player 24 29 rate=0.8` (on `main`: an American player who buys line squads wins 18 of 24, mean 183 s; a VC one 19 of 24, mean 152 s) and with `skirmish rate=1.0` and `siege rate=0.9`. Do not retune `GameRoot.CostFor` or `RateFor`; report the numbers and the Mac will decide.

Commit and push when Task 1 is done, before starting Task 2.

## Task 2: every class its job (row 1d), three rules

Each one behind its own new `MatchOptions` flag, off by default, in this order, one commit each:

1. **The computer's levers.** The player can tell a strongpoint's squad to hold or to go (Fieldcraft; `simcs lever` traces it). The computer never does it for its own side. Give the computer side's plan a way to pull them with reasons a player would recognise: hold a position that is winning its fight or is the last cover before its own edge, go when the lane ahead is empty or the position is being flanked or shelled.
2. **The mortar emplaced.** The mortar team should fire only once it has halted and set the tube up, take time to set up and to pack up, and not fire while moving. Events for set up and packed up, so the view can draw them later.
3. **Satchel charges.** Sappers carry a small number of satchel charges and use them against an occupied position (any cover, built or natural) when they reach it: the charge hurts the men in it and knocks the position's protection down for a time or for good. Counted like the launcher rounds under Ammo.

For each: with its flag off every existing pin is unchanged to the bit (`simcs hash`, every layer); a new pinned case with the flag on; tests beside the others in `SimTests.cs` for the rule's own promises (for example "a mortar never fires within N ticks of moving"); `simcs audit` shows each new event fires; `simcs` accepts the flag by name everywhere the others are accepted; and a balance block, `player 24 29 rate=0.8` with the flag off and on.

Do not turn these flags on in `View/GameRoot.cs`, and do not write view code for them. List what the view will need.

## What to hand back

As CLAUDE.md's "A cloud session's hand-over" says: the branch pushed; new pins with the `simcs hash` line that gave each; a new section **§12.26** in `PLAN.md`, after §12.25 and in the style of §12.24, with what was asked, what was built, every measurement before and after and the command that gave it, and what is left for the Mac. Numbers come from the tool; do not round them into a better story. If something could not be done or was not checked, say so plainly in that section.

Your last message gives: the branch and its last commit; for each of the four pieces of work, whether it is done, partly done or not done; Task 1's numbers before and after; which pins moved; and anything that stopped you (being unable to push, for one).

Things you must not do: change the parity baseline; add a download of any kind other than the .NET SDK; commit anything under `Assets/_Licensed/` or `StreamingAssets/Audio/licensed/`; touch `main`.
