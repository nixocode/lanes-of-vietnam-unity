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

# Single sounds from the Sonniss #GameAudioGDC 2017 bundle (royalty-free,
# commercial use, no attribution; not to be sold as-is). The owner's rule for a
# public repo: licensed files stay out of git, so these land in the ignored
# Audio/licensed/ and are rebuilt from SourceArt/audio/sonniss2017 (re-fetch
# with tools/audio/fetch_sonniss.sh). name: [(file, trim to seconds)]
SONNISS = os.path.join(ROOT, "SourceArt", "audio", "sonniss2017")
LICENSED = {
    "shell": [("explosion_large_08.wav", 4.0), ("explosion_med_long_tail_01.wav", 4.0)],
    "grenade": [("explosion_large_no_tail_03.wav", 2.5)],
    "crack": [("bullet_flyby_fast_05.wav", 1.0)],
    "thump": [("bullet_impact_body_thump_02.wav", 1.0)],
}
# The bed: Faunethic's Thailand library, a quiet jungle of insects and birds —
# the forest of this war, recorded in Southeast Asia. A 60 s loop.
AMBIENCE = ("Jungle quiet insects and birds wide _120407_11.wav", 30.0, 60.0)

# name: (folder, files, how many shots to keep). The M16 is the AR-15 — the
# same rifle, semi-automatic — and the VC's AK-47 is the AK; the SKS joins
# the AK so a VC volley is not one rifle.
SETS = {
    "m16": ("AR-15", None, 8),
    "ak": ("AK-47", None, 8),
    "sks": ("SKS", None, 4),
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
        print(f"[audio] {name}: {len(shots)} shots found in {len(takes)} takes, kept {len(pick)}: "
              f"{', '.join(str(e['seconds']) for e in listing[name])} s", flush=True)
    # --- the licensed sounds (skipped, with a note, when the sources are absent) ---
    lic_dir = os.path.join(OUT, "licensed")
    if os.path.isdir(SONNISS):
        os.makedirs(lic_dir, exist_ok=True)
        for name, items in LICENSED.items():
            listing[name] = []
            for i, (f, keep) in enumerate(items):
                x, sr = read_stereo(os.path.join(SONNISS, f))
                m = x.mean(1)
                # From just before the onset, trimmed, faded, peak at -1 dBFS.
                start = max(0, int(np.argmax(np.abs(m) > np.abs(m).max() * 0.02)) - int(sr * 0.005))
                seg = m[start:start + int(sr * keep)].copy()
                fade = int(sr * 0.08)
                seg[-fade:] *= np.linspace(1, 0, fade)
                seg *= 10 ** (-1 / 20) / max(np.abs(seg).max(), 1e-6)
                wav = os.path.join(tmp, f"{name}_{i}.wav")
                write_wav(wav, seg, sr)
                encode(wav, os.path.join(lic_dir, f"{name}_{i}.m4a"), 1, 128)
                listing[name].append(dict(file=f"licensed/{name}_{i}.m4a", source=f"sonniss2017/{f}", seconds=round(len(seg) / sr, 3)))
            print(f"[audio] {name}: {len(items)} from Sonniss", flush=True)
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

    # A list of named sets, which Unity's JsonUtility can read (it cannot read a dict).
    with open(os.path.join(OUT, "audio.json"), "w") as f:
        json.dump(dict(sets=[dict(name=k, files=v) for k, v in listing.items()]), f, indent=1)
    print(f"[audio] wrote {OUT}")


main()
