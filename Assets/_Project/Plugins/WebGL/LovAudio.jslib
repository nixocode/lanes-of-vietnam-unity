// The game's sound, in the browser's own Web Audio (PLAN §12.6, way 1): a port
// of the three.js build's hand-built, measured graph (../Lanes of vietnam/src/
// audio/engine.ts), with recorded sources in place of synthesised ones.
//
// Unity's audio in a browser has no filters and no mixer effects, so the
// distance model cannot be built from AudioSources. Four things change with
// distance, and only one of them is loudness:
//   travel      343 m/s: a shot at 300 m arrives 0.87 s after its flash
//   absorption  a one-pole low-pass, 20 kHz x exp(-d / 120 m): a near rifle
//               cracks, a far one thumps; cover shortens the scale (darker,
//               not quieter)
//   spreading   1/d from a 10 m reference
//   scattering  a reverb send that rises with distance while the dry path
//               falls, into the valley's impulse response
// Master -> limiter, so a volley ducks under itself instead of clipping.
//
// C# calls in through AudioView; nothing here reads the game's state.
mergeInto(LibraryManager.library, {

  LovAudio_Init: function (seed) {
    if (typeof window === 'undefined' || !(window.AudioContext || window.webkitAudioContext)) return 0;
    var A = window.LovAudio = {
      ctx: new (window.AudioContext || window.webkitAudioContext)(),
      buffers: {}, voices: 0, maxVoices: 24,
      listenerX: 0, listenerZ: 44, ambience: null, ambienceGain: null,
      SPEED: 343, REF: 10, ABSORB: 120, MAX: 900
    };
    var ctx = A.ctx;
    var comp = ctx.createDynamicsCompressor();
    comp.threshold.value = -14; comp.knee.value = 6; comp.ratio.value = 8;
    comp.attack.value = 0.002; comp.release.value = 0.18;
    comp.connect(ctx.destination);
    A.master = ctx.createGain(); A.master.gain.value = 0.9; A.master.connect(comp);
    A.dry = ctx.createGain(); A.dry.connect(A.master);
    A.fx = ctx.createGain(); A.fx.gain.value = 1; A.fx.connect(A.dry);
    A.bed = ctx.createGain(); A.bed.gain.value = 1; A.bed.connect(A.master);

    // The valley's impulse: diffuse decay plus discrete returns off the
    // treeline and the ridge (the three.js valleyImpulse, same numbers).
    var s = seed >>> 0;
    function rnd() {                                   // mulberry32
      s = (s + 0x6D2B79F5) >>> 0; var t = s;
      t = Math.imul(t ^ (t >>> 15), t | 1); t ^= t + Math.imul(t ^ (t >>> 7), t | 61);
      return ((t ^ (t >>> 14)) >>> 0) / 4294967296;
    }
    var sr = ctx.sampleRate, n = Math.round(sr * 2.4);
    var ir = ctx.createBuffer(2, n, sr);
    for (var ch = 0; ch < 2; ch++) {
      var d = ir.getChannelData(ch);
      for (var i = 0; i < n; i++) {
        var t = i / sr;
        d[i] = (rnd() * 2 - 1) * Math.exp(-t * 2.6) * (t < 0.02 ? t / 0.02 : 1);
      }
      var rets = [[1.28, 0.30], [1.62, 0.20], [2.05, 0.12]];
      for (var r = 0; r < rets.length; r++) {
        var i0 = Math.round(sr * (rets[r][0] + (rnd() * 0.06 - 0.03)));
        for (var k = 0; k < sr * 0.09 && i0 + k < n; k++)
          d[i0 + k] += (rnd() * 2 - 1) * rets[r][1] * Math.exp(-k / (sr * 0.03));
      }
      var y = 0, pk = 0;
      for (var j = 0; j < n; j++) { y += 0.10 * (d[j] - y); d[j] = y; pk = Math.max(pk, Math.abs(y)); }
      for (var j2 = 0; j2 < n; j2++) d[j2] *= 0.55 / Math.max(pk, 1e-6);
    }
    A.convolver = ctx.createConvolver();
    A.convolver.normalize = true;
    A.convolver.buffer = ir;
    A.wet = ctx.createGain();
    A.wet.connect(A.convolver);
    A.convolver.connect(A.master);

    // Browsers start audio suspended until the player does something.
    var resume = function () { if (ctx.state !== 'running') ctx.resume(); };
    ['pointerdown', 'keydown', 'touchstart'].forEach(function (ev) {
      window.addEventListener(ev, resume, { capture: true });
    });
    return 1;
  },

  LovAudio_Load: function (namePtr, urlPtr) {
    var A = window.LovAudio; if (!A) return;
    var name = UTF8ToString(namePtr), url = UTF8ToString(urlPtr);
    fetch(url).then(function (r) { return r.arrayBuffer(); })
      .then(function (b) { return A.ctx.decodeAudioData(b); })
      .then(function (buf) { A.buffers[name] = buf; })
      .catch(function (e) { console.warn('[LOV] audio: could not load ' + url + ': ' + e); });
  },

  LovAudio_Loaded: function (namePtr) {
    var A = window.LovAudio; if (!A) return 0;
    return A.buffers[UTF8ToString(namePtr)] ? 1 : 0;
  },

  LovAudio_Listener: function (x, z) {
    var A = window.LovAudio; if (!A) return;
    A.listenerX = x; A.listenerZ = z;
  },

  LovAudio_Volume: function (master, effects, ambience) {
    var A = window.LovAudio; if (!A) return;
    A.master.gain.value = Math.max(0, Math.min(1, master));
    A.fx.gain.value = Math.max(0, Math.min(1, effects));
    A.bed.gain.value = Math.max(0, Math.min(1, ambience));
  },

  // Play a loaded sound at a world position. Returns 1 if it sounded, 0 if it
  // was dropped: not loaded, out of range, or over the voice cap.
  LovAudio_Play: function (namePtr, x, z, gain, rate, occluded, immediate) {
    var A = window.LovAudio; if (!A || A.ctx.state !== 'running') return 0;
    var buf = A.buffers[UTF8ToString(namePtr)]; if (!buf) return 0;
    var dx = x - A.listenerX, dz = z - A.listenerZ;
    var d = Math.max(0.5, Math.sqrt(dx * dx + dz * dz));
    if (d > A.MAX || A.voices >= A.maxVoices) return 0;
    var ctx = A.ctx;
    var scale = A.ABSORB / (1 + 2.6 * Math.max(0, Math.min(1, occluded)));
    var cutoff = Math.max(180, Math.min(20000, 20000 * Math.exp(-d / scale)));
    var g1 = Math.min(1, A.REF / Math.max(A.REF, d));
    var wetAmt = Math.min(0.85, 0.06 + d / 420);
    var at = ctx.currentTime + (immediate ? 0 : d / A.SPEED);

    var src = ctx.createBufferSource(); src.buffer = buf; src.playbackRate.value = rate;
    var f = ctx.createBiquadFilter(); f.type = 'lowpass'; f.frequency.value = cutoff; f.Q.value = 0.4;
    var g = ctx.createGain(); g.gain.value = g1 * gain;
    var pan = ctx.createStereoPanner(); pan.pan.value = Math.max(-1, Math.min(1, dx / Math.max(1, d)));
    src.connect(f); f.connect(g); g.connect(pan); pan.connect(A.fx);
    var send = ctx.createGain(); send.gain.value = wetAmt;    // fed from g: 1/d applied once
    g.connect(send); send.connect(A.wet);
    A.voices++;
    src.onended = function () { A.voices--; };
    src.start(at);
    return 1;
  },

  LovAudio_Ambience: function (namePtr, level) {
    var A = window.LovAudio; if (!A || A.ctx.state !== 'running') return;
    var name = UTF8ToString(namePtr);
    if (!A.ambience && A.buffers[name]) {
      var src = A.ctx.createBufferSource(); src.buffer = A.buffers[name]; src.loop = true;
      A.ambienceGain = A.ctx.createGain(); A.ambienceGain.gain.value = 0;
      src.connect(A.ambienceGain); A.ambienceGain.connect(A.bed); src.start();
      A.ambience = src;
    }
    if (A.ambienceGain) A.ambienceGain.gain.setTargetAtTime(Math.max(0, Math.min(1, level)), A.ctx.currentTime, 0.6);
  },

  LovAudio_Voices: function () {
    var A = window.LovAudio; return A ? A.voices : 0;
  }
});
