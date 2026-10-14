"""
Cut single shots out of the Free Firearm Sound Library's takes (PLAN §12.6:
sourced audio; CC0, logged in ASSETS.md), for the Web Audio engine to place
in the world.

    tools/.venv/bin/python tools/audio/slice_shots.py

Each take is several shots in a row, 96 kHz, 24-bit stereo, close-miked:
"dry recordings, and let the engine add the distance" (§12.6). A shot is
found by its onset — the envelope jumping out of the quiet before it — and
cut from 4 ms before it to where it has decayed 60 dB or the next shot
begins, faded over its last 60 ms, folded to mono (the engine pans), and
peak-normalised to -1 dBFS so every weapon enters the engine at the same
level and the engine's gains mean what they say.

Outputs, in Assets/StreamingAssets/Audio/ (the browser fetches them there):
<name>_<i>.m4a, AAC 44.1 kHz mono, via macOS afconvert, and audio.json
listing them with their durations.
"""
import json
import os
import subprocess
import tempfile
import wave

import numpy as np

ROOT = os.path.dirname(os.path.dirname(os.path.dirname(os.path.abspath(__file__))))
SRC = os.path.join(ROOT, "SourceArt", "audio", "firearms", "Prepared SFX Library")
OUT = os.path.join(ROOT, "Assets", "StreamingAssets", "Audio")

# Single sounds from the Sonniss #GameAudioGDC bundles (royalty-free,
# commercial use, no attribution; not to be sold as-is). The owner's rule for a
# public repo: licensed files stay out of git, so these land in the ignored
# Audio/licensed/ and are rebuilt from SourceArt/audio/sonniss<year> (re-fetch
# with tools/audio/fetch_sonniss.sh). name: [(year/file, trim to seconds)]
ART = os.path.join(ROOT, "SourceArt", "audio")
SONNISS = os.path.join(ART, "sonniss2017")
M101 = "2019/Howitzer,M101,C{},105 mm,{},Right Side,Shot,{}.wav"
LICENSED = {
    "shell": [("2017/explosion_large_08.wav", 4.0), ("2017/explosion_med_long_tail_01.wav", 4.0)],
    "grenade": [("2017/explosion_large_no_tail_03.wav", 2.5)],
    "crack": [("2017/bullet_flyby_fast_05.wav", 1.0)],
    "thump": [("2017/bullet_impact_body_thump_02.wav", 1.0), ("2020/PM_BBI_Bullet_Impact_Hit_Body_Flesh_25.wav", 0.9)],
    # A man hitting the ground, a beat after the round that dropped him.
    "bodyfall": [("2019/RL_bodyfall_Dirt_M4_Close_Stereo_Hard_Impact_10.wav", 1.2)],
    # Rounds striking the earth near the listener: the misses.
    "dirt": [("2020/PM_BBI_Bullet_Impact_Dirt_3.wav", 1.2)],
    # The battery firing: 105 mm M101s, the US howitzer of 1965 (Plei Me, the Ia Drang).
    "howitzer": [(M101.format(1, "Distant", "Pound,Thick"), 4.0), (M101.format(3, "Distant", "Explode,Crack,Sweetener"), 2.8),
                 (M101.format(3, "Medium Distant", "Firm,Heavy,EQ Version,Soft Attack"), 3.2)],
    # The Browning .30 cal: one three-round burst per take, near and far.
    "mg": [("2016/M1919A4_Browning_Machine_Gun_.30cal_5m_behind_ORTF_blanks_Triple_shots_x_1.wav", 2.5),
           ("2016/M1919A4_Browning_Machine_Gun_.30cal_200m_left_behind_blanks_Triple_shots_x_1.wav", 2.5)],
}
# Takes of many events, cut apart where the sound falls quiet for `gap`
# seconds: bursts, rounds going by, the phrases of a radio call.
# name: [(year/file, gap s, how many to keep, longest s)]
EVENTS = {
    "mg": [("2017/warfare_t2_mg_firing_close_projectile_tail_large_field_Telinga_w_MKH8020_or_MKH8060.wav", 0.35, 4, 3.0)],
    # The PPSh-41: the VC's and NVA's submachine gun (and the Chinese Type 50).
    "smg": [("2020/PPSh41, Firing, t1, Burst, Long, MKH416.wav", 0.3, 4, 2.5)],
    "crack": [("2017/warfare_t3_mg_whizzes_ricochets_bullet_cracks_M10.wav", 0.15, 10, 1.2),
              ("2020/PM_BBI_Bullet_Passby_Whizzby_Airy_5.wav", 0.3, 6, 1.2)],
}
# One round each of the weapons that fire bursts, cut out of takes of bursts (PLAN §12.42): the game plays
# a report for every round of a burst at the weapon's own rate, where it used to play one recording of a
# three-round burst whatever the burst. Inside a burst a round's tail is under the next round, so the round
# that is kept is the last of each burst, with the tail it leaves: found by its onset, cut from just before
# it to where it has died away. The Browning .30 cal and the take of a machine gun firing in a field stand
# for the belt-fed guns; the PPSh for the submachine guns.
# name: [(year/file, how many to keep, longest s)]
SINGLES = {
    "mg1": [("2016/M1919A4_Browning_Machine_Gun_.30cal_5m_behind_ORTF_blanks_Triple_shots_x_1.wav", 1, 0.75),
            ("2017/warfare_t2_mg_firing_close_projectile_tail_large_field_Telinga_w_MKH8020_or_MKH8060.wav", 5, 0.75)],
    "smg1": [("2020/PPSh41, Firing, t1, Burst, Long, MKH416.wav", 2, 0.6)],
}
# A piece cut out of a long take by its seconds (PLAN §12.45): the medevac's helicopter. A Bell 222 winding
# up, lifting and going away: a two-bladed Bell, as the Huey is, and the nearest the licensed bundles have to
# one. From eight seconds before it is loudest (the game starts it as the call goes out, and the helicopter is
# in eight seconds later) to twelve after, as it goes.
# name: [(year/file, from s, to s, fade in s, fade out s)]
PIECES = {
    "rotor": [("2019/Helicopter,Bell,222,Idle,Grow,Take Off,Away - faint background birds.wav", 74.0, 94.0, 2.0, 4.0)],
    # A radio man's request for fire, whole: heard as a US player's call goes out. (It asks for forty
    # millimetre; it is the voice and the net that are wanted, under the fight.)
    "call": [("2019/Military Radio Voice D (HI-3) Forty Mike Mike Request.wav", 0.0, 4.48, 0.02, 0.12)],
}
# A recording played slow, made into a sound of its own: the incoming shell. Nothing free or licensed here
# has a shell coming in; a round going by has the same rush, a tenth as long. Played at half its speed it is
# an octave down, and it is cut off just after it is loudest: a sound that grows and stops, which the game
# times to stop as the round bursts. The guns' rounds land 3.5 s after the call and the ranging round a
# second before them, so neither may be longer than 2.4 s. A stand-in, and said to be one.
# name: [(year/file, which event of the take, speed)]
SLOWED = {
    "incoming": [("2020/PM_BBI_Bullet_Passby_Whizzby_Airy_5.wav", 0, 0.50), ("2020/PM_BBI_Bullet_Passby_Whizzby_Airy_5.wav", 2, 0.45)],
}
# How loud each of those is made: the RMS of its first 120 ms, dB below full scale.
SINGLE_DB = -16.0
# Played whole (trimmed around its loudest moment): a fast jet over the air
# strike, and the RTO's call at first contact.
WHOLE = {
    "jet": [("2019/Jet,Fighter,CF-18,Hornet,By,Slow,Rip to Whistle.wav", 3.0, 5.0),
            ("2019/Jet,Fighter,F-16,Fighting Falcon,Medium Distant,By,Whip,Buffet.wav", 3.0, 5.0)],
    "radio": [("2019/Military Radio Voice A (HHG) Troops In Contact Message.wav", 20.0, 20.0)],   # all of it
}
# The bed: Faunethic's Thailand library, a quiet jungle of insects and birds —
# the forest of this war, recorded in Southeast Asia. A 60 s loop.
AMBIENCE = ("Jungle quiet insects and birds wide _120407_11.wav", 30.0, 60.0)

# The launchers' and the mortar's own reports (the owner, 2026-10-09; until then the battery's howitzer,
# pitched up, stood in for all three). Cut from the soundtracks of three videos of US forces firing, which
# are works of the US government and so in the public domain (Wikimedia Commons; ASSETS.md has each
# file's page). Chosen by looking at their spectrograms for reports with no voice or music over them.
# These are free to pass on, so they are committed, beside the CC0 gunfire.
# name: [(file under SourceArt/audio/commons, from s, to s)]
PUBLIC = {
    # An M203 firing: the M79's own 40 mm round, and its hollow thump.
    "m79": [("m203_ifs.wav", 29.575, 30.15), ("m203_ifs.wav", 32.245, 32.9)],
    # An RPG-7 leaving the tube, and its rocket going away.
    "rpg": [("paratroopers_rpg.wav", 29.315, 31.4), ("paratroopers_rpg.wav", 32.365, 33.15)],
    # An 81 mm mortar: the round going down the tube is not heard, the charge is.
    "mortar": [("kw26_mortar_lfx.wav", 26.9, 27.75), ("kw26_mortar_lfx.wav", 33.7, 34.65)],
}
# The music: two pieces by Kevin MacLeod (incompetech.com), Creative Commons Attribution 4.0, credited in
# the game's settings and in ASSETS.md. Re-encoded whole; committed (CC BY allows it, with the credit).
MUSIC = ["Drums of the Deep.mp3", "Crypto.mp3"]

# name: (folder, files, how many shots to keep). The M16 is the AR-15 — the
# same rifle, semi-automatic — and the VC's AK-47 is the AK; the SKS joins
# the AK so a VC volley is not one rifle.
SETS = {
    "m16": ("AR-15", None, 8),
    "ak": ("AK-47", None, 8),
    "sks": ("SKS", None, 4),
    # The snipers' rifle on both sides: a Mosin-Nagant, the bolt action of this war's marksmen.
    "bolt": ("Mosin Nagant", None, 4),
    # The American sniper's: a Tikka bolt action in .308, as near the Marines' M40 (a Remington 700
    # in 7.62 NATO) as the library has. The owner, playtest 6: "sounds for sniper".
    "sniper": ("Tikka", None, 4),
}


def read(path):
    with wave.open(path, "rb") as w:
        ch, sw, sr, n = w.getnchannels(), w.getsampwidth(), w.getframerate(), w.getnframes()
        raw = w.readframes(n)
    if sw == 3:
        b = np.frombuffer(raw, np.uint8).reshape(-1, 3)
        x = (b[:, 0].astype(np.int32) | (b[:, 1].astype(np.int32) << 8) | (b[:, 2].astype(np.int32) << 16))
        x = np.where(x >= 1 << 23, x - (1 << 24), x).astype(np.float64) / (1 << 23)
    elif sw == 2:
        x = np.frombuffer(raw, np.int16).astype(np.float64) / 32768
    else:
        raise ValueError(f"{path}: {sw * 8}-bit not handled")
    return x.reshape(-1, ch).mean(1), sr


def onsets(x, sr):
    """Sample indices where a shot starts: the 1 ms envelope jumps 30 dB over the 50 ms before it."""
    env = np.sqrt(np.convolve(x * x, np.ones(int(sr * 0.001)) / int(sr * 0.001), "same"))
    floor = np.convolve(env, np.ones(int(sr * 0.05)) / int(sr * 0.05), "full")[:len(env)]
    lead = int(sr * 0.05)
    hits = []
    i = lead
    peak = env.max()
    while i < len(env):
        if env[i] > max(floor[i - lead] * 30, peak * 0.08):
            hits.append(i)
            i += int(sr * 0.35)                           # a shot's own blast is not a second shot
        else:
            i += 1
    return hits


def read_stereo(path):
    with wave.open(path, "rb") as w:
        ch, sw, sr, n = w.getnchannels(), w.getsampwidth(), w.getframerate(), w.getnframes()
        raw = w.readframes(n)
    if sw == 3:
        b = np.frombuffer(raw, np.uint8).reshape(-1, 3)
        x = (b[:, 0].astype(np.int32) | (b[:, 1].astype(np.int32) << 8) | (b[:, 2].astype(np.int32) << 16))
        x = np.where(x >= 1 << 23, x - (1 << 24), x).astype(np.float64) / (1 << 23)
    elif sw == 2:
        x = np.frombuffer(raw, np.int16).astype(np.float64) / 32768
    elif sw == 4:
        x = np.frombuffer(raw, np.int32).astype(np.float64) / 2147483648
    else:
        raise ValueError(f"{path}: {sw * 8}-bit not handled")
    return x.reshape(-1, ch), sr


def write_wav_st(path, x, sr):
    y = np.clip(x, -1, 1)
    with wave.open(path, "wb") as w:
        w.setnchannels(y.shape[1]); w.setsampwidth(2); w.setframerate(sr)
        w.writeframes((y * 32767).astype(np.int16).tobytes())


def events(m, sr, gap, keep, longest):
    """Where the events in a take are: 10 ms envelope over -32 dB of the take's
    peak, runs closer than `gap` merged, each cut from 5 ms before its start to
    where it has decayed 60 dB (or the next begins), at most `longest` seconds.
    An even spread of `keep` of them, loudest-first within each stretch."""
    win = max(1, int(sr * 0.01))
    env = np.sqrt(np.convolve(m * m, np.ones(win) / win, "same"))
    loud = env > env.max() * 10 ** (-32 / 20)
    edges = np.flatnonzero(np.diff(loud.astype(np.int8)))
    runs, start = [], (0 if loud[0] else None)
    for e in edges:
        if start is None:
            start = e + 1
        else:
            runs.append([start, e + 1]); start = None
    if start is not None:
        runs.append([start, len(m)])
    merged = []
    for r in runs:
        if merged and r[0] - merged[-1][1] < gap * sr:
            merged[-1][1] = r[1]
        else:
            merged.append(r)
    merged = [r for r in merged if r[1] - r[0] > sr * 0.02]
    cuts = []
    for k, (a, b) in enumerate(merged):
        nxt = merged[k + 1][0] if k + 1 < len(merged) else len(m)
        pk = env[a:b].max()
        tail = b
        while tail < nxt and tail - a < longest * sr and env[tail] > pk * 1e-3:
            tail += win
        cuts.append((max(0, a - int(sr * 0.005)), min(tail, nxt, a + int(longest * sr)), float(pk)))
    if len(cuts) > keep:
        # An even spread over the take; within each share, the loudest.
        share = len(cuts) / keep
        cuts = [max(cuts[int(i * share):int((i + 1) * share)], key=lambda c: c[2]) for i in range(keep)]
    return [(a, b) for a, b, _ in cuts]


def last_rounds(m, sr, keep, longest):
    """The last round of each burst in a take, with its tail: (start, end) in samples, the longest bursts first.

    A round is where the 1 ms envelope jumps to three times what the 12 ms before it held, at least 35 ms
    after the round before. Rounds closer than a quarter of a second are one burst. A burst's last round is
    the last that comes at the burst's own rate (within a third of its middle gap): what follows it at some
    other interval is its echo off the far side of the field, and belongs to its tail."""
    win = max(1, int(sr * 0.001))
    env = np.sqrt(np.convolve(m * m, np.ones(win) / win, "same"))
    held = np.convolve(env, np.ones(int(sr * 0.012)) / int(sr * 0.012), "full")[:len(env)]
    peak, lead = env.max(), int(sr * 0.003)
    hits, i = [], int(sr * 0.015)
    while i < len(env):
        if env[i] > max(held[i - lead] * 3.0, peak * 0.12):
            hits.append(i)
            i += int(sr * 0.035)
        else:
            i += 1
    bursts, run = [], hits[:1]
    for a, b in zip(hits, hits[1:]):
        if b - a < 0.25 * sr:
            run.append(b)
        else:
            bursts.append(run); run = [b]
    if run:
        bursts.append(run)
    cuts = []
    for k, run in enumerate(bursts):
        if len(run) < 3:
            continue
        gaps = np.diff(run)
        mid = np.median(gaps)
        last = 0
        for j, g in enumerate(gaps):
            if abs(g - mid) < mid / 3:
                last = j + 1
        a = run[last] - int(sr * 0.003)
        nxt = bursts[k + 1][0] - int(sr * 0.02) if k + 1 < len(bursts) else len(m)
        b = min(nxt, a + int(longest * sr))
        cuts.append((len(run), a, b))
    cuts.sort(reverse=True)
    return [(a, b) for _, a, b in cuts[:keep]]


def loudness(seg, sr):
    """How loud a report is, dB below full scale: the RMS of its first 120 ms. (Every clip's peak is put at
    -1 dB, and two reports with the same peak are not equally loud: a rifle's crack is over in a few
    milliseconds, a machine gun's round has a body.)"""
    body = seg[:int(sr * 0.12)]
    return 20 * np.log10(max(np.sqrt((body ** 2).mean()), 1e-9))


def finish(seg, sr, fade_s=0.06):
    seg = seg.copy()
    fade = min(len(seg), int(sr * fade_s))
    seg[-fade:] *= np.linspace(1, 0, fade)
    return seg * 10 ** (-1 / 20) / max(np.abs(seg).max(), 1e-6)


def encode(wav, m4a, channels, kbps):
    subprocess.run(["afconvert", "-f", "m4af", "-d", "aac@44100", "-b", str(kbps * 1000), "-c", str(channels), wav, m4a], check=True)


def write_wav(path, x, sr):
    y = np.clip(x, -1, 1)
    with wave.open(path, "wb") as w:
        w.setnchannels(1); w.setsampwidth(2); w.setframerate(sr)
        w.writeframes((y * 32767).astype(np.int16).tobytes())


def main():
    os.makedirs(OUT, exist_ok=True)
    listing = {}
    loud = {}
    tmp = tempfile.mkdtemp()
    for name, (folder, files, keep) in SETS.items():
        d = os.path.join(SRC, folder)
        takes = sorted(f for f in os.listdir(d) if f.lower().endswith(".wav"))
        shots = []
        for f in takes:
            x, sr = read(os.path.join(d, f))
            hs = onsets(x, sr)
            for k, h in enumerate(hs):
                a = max(0, h - int(sr * 0.004))
                end = hs[k + 1] - int(sr * 0.01) if k + 1 < len(hs) else len(x)
                seg = x[a:end]
                # Cut where it has decayed 60 dB below its peak, at most 1.6 s.
                env = np.abs(seg)
                pk = env.max()
                quiet = np.nonzero(env > pk * 1e-3)[0]
                n = min(len(seg), (quiet[-1] if len(quiet) else len(seg)) + int(sr * 0.02), int(sr * 1.6))
                seg = seg[:n].copy()
                fade = min(len(seg), int(sr * 0.06))
                seg[-fade:] *= np.linspace(1, 0, fade)
                seg *= 10 ** (-1 / 20) / max(pk, 1e-6)
                shots.append((f, k, seg, sr))
        # Keep an even spread of the takes' shots.
        n = min(keep, len(shots))
        pick = [shots[int(i * len(shots) / n)] for i in range(n)]
        listing[name] = []
        for i, (f, k, seg, sr) in enumerate(pick):
            wav = os.path.join(tmp, f"{name}_{i}.wav")
            m4a = os.path.join(OUT, f"{name}_{i}.m4a")
            write_wav(wav, seg, sr)
            subprocess.run(["afconvert", "-f", "m4af", "-d", "aac@44100", "-b", "128000", "-c", "1", wav, m4a], check=True)
            listing[name].append(dict(file=f"{name}_{i}.m4a", source=f"{folder}/{f} shot {k}", seconds=round(len(seg) / sr, 3)))
        loud[name] = [loudness(seg, sr) for _, _, seg, sr in pick]
        print(f"[audio] {name}: {len(shots)} shots found in {len(takes)} takes, kept {len(pick)}: "
              f"{', '.join(str(e['seconds']) for e in listing[name])} s", flush=True)
    # --- the licensed sounds (skipped, with a note, when the sources are absent) ---
    lic_dir = os.path.join(OUT, "licensed")
    if os.path.isdir(SONNISS):
        os.makedirs(lic_dir, exist_ok=True)
        out = {}

        def emit(name, seg, sr, source):
            i = len(out.setdefault(name, []))
            wav = os.path.join(tmp, f"{name}_{i}.wav")
            write_wav(wav, seg, sr)
            encode(wav, os.path.join(lic_dir, f"{name}_{i}.m4a"), 1, 128)
            out[name].append(dict(file=f"licensed/{name}_{i}.m4a", source=f"sonniss{source}", seconds=round(len(seg) / sr, 3)))

        for name, items in LICENSED.items():
            for f, keep in items:
                x, sr = read_stereo(os.path.join(ART, "sonniss" + f))
                m = x.mean(1)
                # From just before the onset, trimmed, faded, peak at -1 dBFS.
                start = max(0, int(np.argmax(np.abs(m) > np.abs(m).max() * 0.02)) - int(sr * 0.005))
                emit(name, finish(m[start:start + int(sr * keep)], sr, 0.08), sr, f)
        for name, items in SINGLES.items():
            for f, keep, longest in items:
                x, sr = read_stereo(os.path.join(ART, "sonniss" + f))
                m = x.mean(1)
                cuts = last_rounds(m, sr, keep, longest)
                for a_, b_ in cuts:
                    # Each to the same loudness, not the same peak: the rounds of one burst come from different
                    # takes, and at one peak they were up to eleven decibels apart.
                    seg = finish(m[a_:b_], sr, min(0.25, 0.5 * (b_ - a_) / sr))
                    seg = seg * 10 ** ((SINGLE_DB - loudness(seg, sr)) / 20)
                    seg = seg * min(1.0, 10 ** (-1 / 20) / max(np.abs(seg).max(), 1e-6))
                    loud.setdefault(name, []).append(loudness(seg, sr))
                    emit(name, seg, sr, f"{f} {a_ / sr:.3f}-{b_ / sr:.3f} s")
                print(f"[audio] {name}: {len(cuts)} single rounds from {f.split('/')[-1][:50]}", flush=True)
        for name, items in PIECES.items():
            for f, a_s, b_s, rise_s, fall_s in items:
                x, sr = read_stereo(os.path.join(ART, "sonniss" + f))
                seg = x.mean(1)[int(a_s * sr):int(b_s * sr)].copy()
                rise = int(rise_s * sr)
                seg[:rise] *= np.linspace(0, 1, rise)
                emit(name, finish(seg, sr, fall_s), sr, f"{f} {a_s:.1f}-{b_s:.1f} s")
        for name, items in SLOWED.items():
            for f, which, speed in items:
                x, sr = read_stereo(os.path.join(ART, "sonniss" + f))
                m = x.mean(1)
                cuts = events(m, sr, 0.3, 6, 1.2)
                a_, b_ = cuts[min(which, len(cuts) - 1)]
                seg = m[a_:b_]
                # Slower: the same samples spread over more of them (and so lower, as a tape played slow is).
                slow = np.interp(np.arange(0, len(seg) - 1, speed), np.arange(len(seg)), seg)
                # To just after it is loudest (its level over 50 ms), and no longer than 2.4 s before that.
                w = int(sr * 0.05)
                level = np.convolve(slow * slow, np.ones(w) / w, mode="same")
                end = min(len(slow), int(np.argmax(level)) + int(sr * 0.12))
                slow = slow[max(0, end - int(sr * 2.4)):end].copy()
                rise = min(len(slow), int(sr * 0.3))
                slow[:rise] *= np.linspace(0, 1, rise)
                emit(name, finish(slow, sr, 0.08), sr, f"{f} event {which} at {speed} of its speed, to its loudest")
        for name, items in EVENTS.items():
            for f, gap, keep, longest in items:
                x, sr = read_stereo(os.path.join(ART, "sonniss" + f))
                m = x.mean(1)
                cuts = events(m, sr, gap, keep, longest)
                for a_, b_ in cuts:
                    emit(name, finish(m[a_:b_], sr), sr, f"{f} {a_ / sr:.2f}-{b_ / sr:.2f} s")
                print(f"[audio] {name}: {len(cuts)} from {f.split('/')[-1][:50]}", flush=True)
        for name, items in WHOLE.items():
            for f, before, after in items:
                x, sr = read_stereo(os.path.join(ART, "sonniss" + f))
                m = x.mean(1)
                env = np.convolve(np.abs(m), np.ones(int(sr * 0.05)) / int(sr * 0.05), "same")
                pk = int(np.argmax(env))
                a_, b_ = max(0, pk - int(before * sr)), min(len(m), pk + int(after * sr))
                seg = m[a_:b_].copy()
                rise = min(len(seg), int(sr * 0.3))
                seg[:rise] *= np.linspace(0, 1, rise)
                emit(name, finish(seg, sr, 0.5), sr, f"{f} {a_ / sr:.2f}-{b_ / sr:.2f} s")
        for name, files in out.items():
            listing[name] = listing.get(name, [])[:0] + files
            print(f"[audio] {name}: {len(files)} licensed: {', '.join(str(e['seconds']) for e in files)} s", flush=True)
        f, at, length = AMBIENCE
        x, sr = read_stereo(os.path.join(SONNISS, f))
        a, n, xf = int(sr * at), int(sr * length), int(sr * 3.0)
        body = x[a:a + n].copy()
        # Seamless: the last 3 s crossfaded into the 3 s that follow the loop's start.
        tail = x[a + n:a + n + xf]
        w = np.linspace(0, 1, xf)[:, None]
        body[:xf] = body[:xf] * w + tail * (1 - w)
        rms = np.sqrt((body ** 2).mean())
        body *= 0.1 / max(rms, 1e-6)                 # -20 dBFS RMS: a bed, not a feature
        body = np.clip(body, -0.99, 0.99)
        wav = os.path.join(tmp, "ambience.wav")
        write_wav_st(wav, body, sr)
        encode(wav, os.path.join(lic_dir, "ambience_0.m4a"), 2, 96)
        listing["ambience"] = [dict(file="licensed/ambience_0.m4a", source=f"sonniss2017/{f} {at:.0f}-{at + length:.0f} s", seconds=length)]
        print(f"[audio] ambience: {length:.0f} s loop from Sonniss", flush=True)
    else:
        print("[audio] no SourceArt/audio/sonniss2017: explosions, cracks and the jungle bed are left out "
              "(tools/audio/fetch_sonniss.sh fetches them)", flush=True)

    # --- the public-domain reports and the CC BY music (tools/audio/fetch_free.sh fetches their sources) ---
    commons = os.path.join(ART, "commons")
    if os.path.isdir(commons):
        for name, cuts in PUBLIC.items():
            listing[name] = []
            for i, (f, a_s, b_s) in enumerate(cuts):
                x, sr = read_stereo(os.path.join(commons, f))
                m = x.mean(1)
                seg = m[int(a_s * sr):int(b_s * sr)].copy()
                rise = min(len(seg), int(sr * 0.003))
                seg[:rise] *= np.linspace(0, 1, rise)             # the cut is just before the report: no click
                wav = os.path.join(tmp, f"{name}_{i}.wav")
                write_wav(wav, finish(seg, sr, 0.12), sr)
                encode(wav, os.path.join(OUT, f"{name}_{i}.m4a"), 1, 128)
                listing[name].append(dict(file=f"{name}_{i}.m4a", source=f"commons/{f} {a_s:.2f}-{b_s:.2f} s", seconds=round(len(seg) / sr, 3)))
            print(f"[audio] {name}: {len(cuts)} public domain: {', '.join(str(e['seconds']) for e in listing[name])} s", flush=True)
    else:
        print("[audio] no SourceArt/audio/commons: the launchers and the mortar keep the howitzer's report (tools/audio/fetch_free.sh)", flush=True)
    music = os.path.join(ART, "music")
    if all(os.path.isfile(os.path.join(music, f)) for f in MUSIC):
        listing["music"] = []
        for i, f in enumerate(MUSIC):
            out_m4a = os.path.join(OUT, f"music_{i}.m4a")
            subprocess.run(["afconvert", "-f", "m4af", "-d", "aac@44100", "-b", "96000", "-c", "2", os.path.join(music, f), out_m4a], check=True)
            secs = float(subprocess.run(["afinfo", "-b", out_m4a], capture_output=True, text=True).stdout.split(" sec")[0].split()[-1])
            listing["music"].append(dict(file=f"music_{i}.m4a", source=f"music/{f} (Kevin MacLeod, incompetech.com, CC BY 4.0)", seconds=round(secs, 1)))
        print(f"[audio] music: {', '.join(str(e['seconds']) for e in listing['music'])} s", flush=True)
    else:
        print("[audio] no SourceArt/audio/music: no music (tools/audio/fetch_free.sh)", flush=True)

    # How loud each weapon's report comes out, for AudioView's gains (every clip peaks at -1 dB; they are not equally loud).
    for name, dbs in loud.items():
        print(f"[audio] loudness {name}: {np.mean(dbs):.1f} dB (first 120 ms, RMS; {', '.join(f'{d:.1f}' for d in dbs)})", flush=True)
    # A list of named sets, which Unity's JsonUtility can read (it cannot read a dict).
    with open(os.path.join(OUT, "audio.json"), "w") as f:
        json.dump(dict(sets=[dict(name=k, files=v) for k, v in listing.items()]), f, indent=1)
    print(f"[audio] wrote {OUT}")


main()
