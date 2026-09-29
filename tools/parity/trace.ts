/**
 * The original simulation's side of the parity check.
 *
 *   node --experimental-strip-types tools/parity/trace.ts > /tmp/ts-trace.json
 *
 * Runs the TypeScript sim from the three.js build (the sibling folder
 * "Lanes of vietnam") and records, for every tick of every match, one FNV-1a
 * hash over the entire simulation state — every man, squad, piece of cover,
 * area and event, with doubles hashed by their exact bits. The C# runner
 * (tools/simcs) computes the same hash the same way; the first tick where the
 * two sequences differ is where the port diverges.
 *
 * A hash per tick rather than one per match, because "the matches differ" is
 * not a diagnosis and "they differ from tick 1,407, in man 31's pin" is.
 *
 * The scripted scenario buys every card once and gives player orders, so the
 * called-in effects — barrages, smoke, traps, the tunnel — are covered by the
 * check and not only the plan-driven fight.
 */
import { createMatch, step, buyCard, orderSquad, DECK, FLOOR, CEILING, DEFEND, NO_REINFORCE,
  type Plan } from '../../../Lanes of vietnam/src/sim/match.ts';
import { roster } from '../../../Lanes of vietnam/src/sim/squad.ts';
import { Rng } from '../../../Lanes of vietnam/src/sim/rng.ts';
import { MAX_TICKS } from '../../../Lanes of vietnam/src/sim/tune.ts';
import type { SimState, Side } from '../../../Lanes of vietnam/src/sim/types.ts';

const KINDS = ['fire', 'kill', 'pinned', 'unpinned', 'first-contact', 'cover-taken', 'cover-left',
  'ranged-in', 'squad-spawned', 'squad-broke', 'bound-start', 'morale-lost', 'match-over',
  'shell', 'area-start', 'area-end', 'trap-sprung'];
const POSTURE: Record<string, number> = { stand: 0, crouch: 1, prone: 2 };
const ORDER: Record<string, number> = { advance: 0, hold: 1, bound: 2, fallback: 3 };
const PHASE: Record<string, number> = { opening: 0, fight: 1, over: 2 };
const SIDE: Record<string, number> = { us: 0, vc: 1 };
const COVER: Record<string, number> = { trench: 0, sandbag: 1, bunker: 2, crater: 3, berm: 4 };
const AREA: Record<string, number> = { barrage: 0, smoke: 1, trap: 2 };

const f64 = new Float64Array(1);
const u32 = new Uint32Array(f64.buffer);

function mix(h: number, v: number): number {
  v = v >>> 0;
  for (let i = 0; i < 4; i++) {
    h = (h ^ ((v >>> (i * 8)) & 0xff)) >>> 0;
    h = Math.imul(h, 0x01000193) >>> 0;
  }
  return h;
}
function mixD(h: number, x: number): number {
  f64[0] = x;
  return mix(mix(h, u32[0]!), u32[1]!);   // low word, then high: little-endian
}
function mixI(h: number, x: number): number { return mix(h, x | 0); }
function mixB(h: number, b: boolean): number { return mix(h, b ? 1 : 0); }

export function hashState(st: SimState, fromEvent: number): number {
  let h = 0x811c9dc5;
  h = mixI(h, st.tick); h = mixI(h, PHASE[st.phase]!); h = mixI(h, st.contactTick);
  for (const m of st.men) {
    h = mixI(h, m.id); h = mixI(h, m.squad); h = mixI(h, SIDE[m.side]!);
    h = mixD(h, m.x); h = mixD(h, m.z); h = mixB(h, m.alive); h = mixD(h, m.pin);
    h = mixI(h, POSTURE[m.posture]!); h = mixI(h, m.cooldown); h = mixI(h, m.cover);
    h = mixI(h, m.dwell); h = mixB(h, m.seen); h = mixD(h, m.veterancy); h = mixI(h, m.diedAt);
    h = mixI(h, m.trail.length);
    for (const t of m.trail) h = mixD(h, t);
  }
  for (const s of st.squads) {
    h = mixI(h, s.id); h = mixI(h, SIDE[s.side]!); h = mixI(h, ORDER[s.order]!);
    h = mixD(h, s.anchorX); h = mixD(h, s.anchorZ); h = mixI(h, s.lane); h = mixI(h, s.target);
    h = mixB(h, s.bounding); h = mixI(h, s.playerOrder === null ? -1 : ORDER[s.playerOrder]!);
  }
  for (const c of st.cover) {
    h = mixI(h, c.id); h = mixI(h, COVER[c.kind]!); h = mixD(h, c.x); h = mixD(h, c.z);
    h = mixD(h, c.length); h = mixI(h, c.capacity); h = mixD(h, c.quality);
    h = mixD(h, c.rangedIn); h = mixI(h, c.heldBy === null ? -1 : SIDE[c.heldBy]!);
  }
  for (const a of st.areas) {
    h = mixI(h, a.id); h = mixI(h, AREA[a.kind]!); h = mixI(h, SIDE[a.side]!);
    h = mixD(h, a.x); h = mixD(h, a.z); h = mixD(h, a.radius); h = mixI(h, a.ticks);
    h = mixI(h, a.next); h = mixD(h, a.power);
  }
  for (const s of ['us', 'vc'] as Side[]) {
    h = mixD(h, st.morale[s]); h = mixD(h, st.cp[s]); h = mixD(h, st.front[s]);
    h = mixD(h, st.moraleLost[s].casualties); h = mixD(h, st.moraleLost[s].ground);
  }
  h = mixI(h, st.events.length);
  for (let i = fromEvent; i < st.events.length; i++) {
    const e = st.events[i]!;
    h = mixI(h, KINDS.indexOf(e.kind)); h = mixI(h, e.tick); h = mixI(h, SIDE[e.side]!);
    h = mixI(h, e.id); h = mixI(h, e.target ?? -1);
    h = e.x === undefined ? mixI(h, -7) : mixD(h, e.x);
    h = e.z === undefined ? mixI(h, -7) : mixD(h, e.z);
    h = e.amount === undefined ? mixI(h, -7) : mixD(h, e.amount);
  }
  h = mixB(h, st.over); h = mixI(h, st.winner === null ? -1 : SIDE[st.winner]!);
  return h >>> 0;
}

/** The scripted scenario: [tick, action]. Applied before the step into that tick. */
type Act = { side: Side; card?: string; lane?: number; aimX?: number; squad?: number;
             order?: 'advance' | 'hold' | 'bound' | 'fallback' | null };
// Scheduled against the CP curve (0.9 CP/s = 0.045 per tick, from zero) so
// every purchase can actually go through: under reinforcing plans the CP is
// spent on squads as fast as it accrues and the expensive calls — the
// barrages, which are the code that uses sin and cos — were never bought.
const SCRIPT: [number, Act][] = [
  [300, { side: 'us', card: 'us-smoke', lane: 0, aimX: 0 }],
  [300, { side: 'vc', card: 'vc-punji', lane: 0, aimX: -5 }],
  [350, { side: 'us', squad: 0, order: 'hold' }],
  [500, { side: 'vc', card: 'vc-spider', lane: 1, aimX: 8 }],
  [700, { side: 'us', squad: 0, order: null }],
  [900, { side: 'us', card: 'us-arty', lane: 1, aimX: 10 }],
  [900, { side: 'vc', card: 'vc-tunnel', lane: 1, aimX: -20 }],
  [1300, { side: 'us', card: 'us-medevac', lane: 0, aimX: 0 }],
  [1300, { side: 'vc', card: 'vc-tripwire', lane: 0, aimX: 0 }],
  [2200, { side: 'us', card: 'us-airstrike', lane: 1, aimX: 5 }],
];

function trace(seed: number, us: Plan, vc: Plan, scripted: boolean) {
  const st = createMatch({ seed, us, vc });
  const rng = new Rng(seed).fork('sim');
  const original = new Map<number, number>();
  for (const sq of st.squads) original.set(sq.id, roster(st, sq.id).length);
  const cds: Record<Side, Map<string, number>> = { us: new Map(), vc: new Map() };
  const hashes: number[] = [hashState(st, 0)];
  const accepted: boolean[] = [];
  let k = 0;
  while (!st.over && st.tick < MAX_TICKS) {
    if (scripted) {
      while (k < SCRIPT.length && SCRIPT[k]![0] === st.tick + 1) {
        const [, a] = SCRIPT[k++]!;
        if (a.card) {
          const card = DECK[a.side].find((c) => c.id === a.card)!;
          accepted.push(buyCard(st, a.side, card, a.lane!, cds[a.side], rng, a.aimX!));
        } else {
          accepted.push(orderSquad(st, a.squad!, a.order!));
        }
      }
    }
    const before = st.events.length;
    step(st, us, vc, rng, original);
    hashes.push(hashState(st, before));
  }
  return { seed, us: us.name, vc: vc.name, scripted, ticks: st.tick, winner: st.winner,
           reason: st.reason, accepted, hashes };
}

const plans: [Plan, Plan][] = [[CEILING, CEILING], [FLOOR, CEILING], [DEFEND, DEFEND],
                               [NO_REINFORCE, CEILING]];
const out = [];
for (const [u, v] of plans) for (const seed of [1, 2, 7, 99]) out.push(trace(seed, u, v, false));
for (const seed of [3, 11, 42, 5, 8, 13]) out.push(trace(seed, NO_REINFORCE, NO_REINFORCE, true));

// Primitive vectors, for the maths the port reimplements.
const vec = new Rng(777);
const hypot: string[] = [], trig: string[] = [];
const hex = (x: number) => { f64[0] = x; return u32[1]!.toString(16).padStart(8, '0') + u32[0]!.toString(16).padStart(8, '0'); };
for (let i = 0; i < 20000; i++) {
  const a = (vec.next() - 0.5) * Math.pow(10, vec.int(-3, 3));
  const b = (vec.next() - 0.5) * Math.pow(10, vec.int(-3, 3));
  hypot.push(`${hex(a)} ${hex(b)} ${hex(Math.hypot(a, b))}`);
  const t = vec.next() * Math.PI * 2;
  trig.push(`${hex(t)} ${hex(Math.cos(t))} ${hex(Math.sin(t))}`);
}
process.stdout.write(JSON.stringify({ matches: out, hypot, trig }));
