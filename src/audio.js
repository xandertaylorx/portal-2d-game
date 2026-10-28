// All sound effects are synthesised at startup (no audio files). Port of Game/Audio.cs onto the Web Audio API.
// Browsers only allow audio after a user gesture, so everything is built lazily on the first click or key press.
import { clamp, TAU } from './core.js';
import { Sfx } from './world.js';

const SR = 44100;

export class Audio {
  constructor() {
    this.ctx = null;
    this.ready = false;
    this.buffers = new Map();
    this.master = 0.8; this.sfxVolume = 1; this.ambienceVolume = 0.5;
    this.phases = [];
    this.oscIndex = 0;
    this.ambientSource = null;
  }

  /** Call from a user-gesture handler. Safe to call repeatedly. */
  unlock() {
    if (this.ready) { if (this.ctx.state === 'suspended') this.ctx.resume(); return; }
    const AC = window.AudioContext || window.webkitAudioContext;
    if (!AC) return;
    try {
      this.ctx = new AC();
      this.out = this.ctx.createGain();
      // everything passes through a low-pass that closes briefly after a portal trip (the muffled-to-clear swell)
      this.filter = this.ctx.createBiquadFilter();
      this.filter.type = 'lowpass'; this.filter.frequency.value = 20000; this.filter.Q.value = 0.4;
      this.out.connect(this.filter);
      this.filter.connect(this.ctx.destination);
      this.ambGain = this.ctx.createGain();
      this.ambGain.connect(this.out);
      this.build();
      this.ready = true;
      this.applyVolumes();
      this.startAmbience();
      this.startLoops();
    } catch (e) {
      this.ready = false;
    }
  }

  applyVolumes() {
    if (!this.ready) return;
    this.out.gain.value = this.master;
    this.ambGain.gain.value = this.ambienceVolume * 0.6;
  }

  startAmbience() {
    const src = this.ctx.createBufferSource();
    src.buffer = this.buffers.get('ambient');
    src.loop = true;
    src.connect(this.ambGain);
    src.start();
    this.ambientSource = src;
  }

  /** Continuous layers whose volume follows the scene: beam hum, portal hum, wind rush that rises with speed. */
  startLoops() {
    const mk = (name, rate = 1) => {
      const src = this.ctx.createBufferSource();
      src.buffer = this.buffers.get(name);
      src.loop = true;
      src.playbackRate.value = rate;
      const gain = this.ctx.createGain();
      gain.gain.value = 0;
      src.connect(gain); gain.connect(this.out);
      src.start();
      return { src, gain };
    };
    this.loops = { laser: mk('laserHum'), portal: mk('portalHum'), wind: mk('wind') };
    this.rainLayer = mk('rain');
  }

  /** laser / portal / speed / rain / wind are 0..1: how close the nearest beam and portal are, how fast the
    // player is moving, and how heavy the storm is. */
  setScene(sc) {
    if (!this.ready || !this.loops) return;
    const t = this.ctx.currentTime;
    const L = this.loops;
    L.laser.gain.gain.setTargetAtTime(sc.laser * 0.11 * this.sfxVolume, t, 0.08);
    L.portal.gain.gain.setTargetAtTime(sc.portal * 0.13 * this.sfxVolume, t, 0.1);
    L.wind.gain.gain.setTargetAtTime((sc.wind || sc.speed) * 0.16 * this.sfxVolume, t, 0.12);
    L.wind.src.playbackRate.setTargetAtTime(0.75 + (sc.wind || sc.speed) * 0.7, t, 0.1);
    L.portal.src.playbackRate.setTargetAtTime(0.92 + sc.portal * 0.1 + (sc.wind || sc.speed) * 0.15, t, 0.15);
    if (this.rainLayer) {
      this.rainLayer.gain.gain.setTargetAtTime((sc.rain || 0) * 0.18 * this.ambienceVolume, t, 0.2);
      this.rainLayer.src.playbackRate.setTargetAtTime(0.9 + (sc.rain || 0) * 0.3, t, 0.2);
    }
  }

  /** Dull the whole mix, then let it open back up. */
  muffle(depth = 1) {
    if (!this.ready) return;
    const f = this.filter.frequency, t = this.ctx.currentTime;
    f.cancelScheduledValues(t);
    f.setValueAtTime(Math.max(500, 4200 - depth * 3600), t);
    f.exponentialRampToValueAtTime(20000, t + 0.25 + depth * 0.45);
  }

  build() {
    const add = (name, data) => this.buffers.set(name, this.makeBuffer(data));
    add(Sfx.PortalBlue, this.portalShot(320, 980));
    add(Sfx.PortalOrange, this.portalShot(230, 700));
    add(Sfx.PortalFail, this.gen(0.2, (t) => (this.square(140 - t * 220) * 0.4 + this.noise() * 0.3) * env(t, 0.005, 0.2)));
    add(Sfx.Jump, this.gen(0.14, (t) => this.sine(sweep(280, 520, t, 0.14)) * env(t, 0.004, 0.14) * 0.35));
    add(Sfx.Land, this.lowNoise(0.14, 0.5, 90, 45));
    add(Sfx.Teleport, this.whoosh());
    add(Sfx.ButtonDown, this.gen(0.18, (t) => this.noise() * env(t, 0.001, 0.015) * 0.6 + this.sine(520) * env(t, 0.005, 0.18) * 0.35));
    add(Sfx.ButtonUp, this.gen(0.14, (t) => this.noise() * env(t, 0.001, 0.012) * 0.5 + this.sine(360) * env(t, 0.005, 0.14) * 0.3));
    add(Sfx.DoorOpen, this.door(true));
    add(Sfx.DoorClose, this.door(false));
    add(Sfx.Pickup, this.gen(0.12, (t) => this.sine(sweep(600, 950, t, 0.12)) * env(t, 0.004, 0.12) * 0.3));
    add(Sfx.Drop, this.gen(0.12, (t) => this.sine(sweep(520, 300, t, 0.12)) * env(t, 0.004, 0.12) * 0.3));
    add(Sfx.Death, this.gen(0.8, (t) => (this.saw(sweep(320, 50, t, 0.8)) * 0.35 + this.noise() * 0.25 * (1 - t / 0.8)) * env(t, 0.005, 0.8)));
    add(Sfx.Win, this.win());
    add(Sfx.Fizzle, this.gen(0.45, (t) => (this.noise() * (Math.random() < 0.15 ? 0.7 : 0.1) + this.sine(sweep(1400, 2200, t, 0.45)) * 0.15) * env(t, 0.005, 0.45)));
    add(Sfx.CubeDissolve, this.gen(0.7, (t) => (this.sine(800 + t * 900) + this.sine(1210 + t * 700) * 0.6) * 0.2 * (0.6 + 0.4 * Math.sin(t * 70)) * env(t, 0.02, 0.7)));
    add(Sfx.CubeSpawn, this.gen(0.25, (t) => this.sine(sweep(380, 720, t, 0.25)) * env(t, 0.01, 0.25) * 0.3));
    add(Sfx.ReceiverOn, this.gen(0.5, (t) => (this.sine(sweep(180, 260, t, 0.5)) + this.sine(sweep(360, 520, t, 0.5)) * 0.5) * env(t, 0.03, 0.5) * 0.3));
    add('hover', this.gen(0.05, (t) => this.sine(1100) * env(t, 0.002, 0.05) * 0.12));
    add('click', this.gen(0.1, (t) => this.sine(sweep(700, 1200, t, 0.1)) * env(t, 0.002, 0.1) * 0.25));
    add(Sfx.TurretSpot, this.gen(0.18, (t) => this.square(t < 0.07 ? 880 : 1320) * env(t, 0.003, 0.18) * 0.22));
    add(Sfx.TurretFire, this.gen(0.35, (t) => (this.saw(sweep(1400, 120, t, 0.3)) * 0.5 + this.noise() * 0.35 * Math.max(0, 1 - t * 6)) * env(t, 0.002, 0.35)));
    add(Sfx.Bolt, this.gen(0.2, (t) => (this.saw(sweep(1900, 500, t, 0.18)) * 0.4 + this.sine(sweep(900, 300, t, 0.18)) * 0.3) * env(t, 0.002, 0.2) * 0.8));
    add(Sfx.Hit, this.gen(0.18, (t) => (this.noise() * 0.6 + this.square(sweep(160, 60, t, 0.15)) * 0.5) * env(t, 0.002, 0.18) * 0.9));
    add('ambient', this.ambience());
    add('laserHum', this.gen(1, (t) => (this.saw(100) * 0.5 + this.saw(200) * 0.25 + this.sine(300) * 0.25) * (0.8 + 0.2 * Math.sin(TAU * 4 * t)) * 0.5));
    add('portalHum', this.gen(1, (t) => (this.sine(220) * 0.5 + this.sine(330) * 0.3 + this.sine(440) * 0.12) * (0.75 + 0.25 * Math.sin(TAU * 2 * t)) * 0.6));
    add('wind', this.windLoop());
    add('rain', this.rainLoop());
  }

  makeBuffer(data) {
    const buf = this.ctx.createBuffer(1, data.length, SR);
    const ch = buf.getChannelData(0);
    for (let i = 0; i < data.length; i++) ch[i] = clamp(data[i], -1, 1) * (32000 / 32768);
    return buf;
  }

  /** Play one effect; pos/listener give distance attenuation and a little stereo pan. */
  play(sfx, pos, listener, pitch = 1) {
    if (!this.ready) return;
    const buf = this.buffers.get(sfx);
    if (!buf) return;
    const dist = Math.hypot(pos.x - listener.x, pos.y - listener.y);
    const vol = clamp(1.15 - dist / 1300, 0.35, 1) * this.sfxVolume;
    this.blip(buf, vol, pitch * (1 + (Math.random() - 0.5) * 0.08), clamp((pos.x - listener.x) / 2400, -0.35, 0.35));
  }

  blip(buf, vol, rate = 1, pan = 0) {
    const src = this.ctx.createBufferSource();
    src.buffer = buf;
    src.playbackRate.value = rate;
    const gain = this.ctx.createGain();
    gain.gain.value = vol;
    let node = gain;
    src.connect(gain);
    if (this.ctx.createStereoPanner) {
      const p = this.ctx.createStereoPanner();
      p.pan.value = pan;
      gain.connect(p);
      node = p;
    }
    node.connect(this.out);
    src.start();
  }

  hover() { if (this.ready) this.blip(this.buffers.get('hover'), this.sfxVolume); }
  click() { if (this.ready) this.blip(this.buffers.get('click'), this.sfxVolume); }

  // ------------------------------------------------------------------ synthesis

  osc(f) {
    if (this.oscIndex >= this.phases.length) this.phases.push(0);
    const p = this.phases[this.oscIndex] + f / SR * TAU;
    this.phases[this.oscIndex++] = p;
    return p % TAU;
  }
  sine(f) { return Math.sin(this.osc(f)); }
  square(f) { return Math.sin(this.osc(f)) > 0 ? 1 : -1; }
  saw(f) { const p = this.osc(f) / TAU; return 2 * (p - Math.floor(p + 0.5)); }
  noise() { return Math.random() * 2 - 1; }

  /** Generic generator: each oscillator call inside f gets its own phase accumulator (by call order). */
  gen(dur, f) {
    const n = Math.floor(dur * SR);
    const data = new Float32Array(n);
    this.phases.length = 0;
    for (let i = 0; i < n; i++) {
      this.oscIndex = 0;
      data[i] = f(i / SR, i);
    }
    return data;
  }

  portalShot(f0, f1) {
    const dur = 0.42, n = Math.floor(dur * SR), d = new Float32Array(n);
    let p1 = 0, p2 = 0, lp = 0;
    for (let i = 0; i < n; i++) {
      const t = i / SR;
      const f = sweep(f0, f1, t, 0.18) * (1 + 0.03 * Math.sin(t * 90));
      p1 += f / SR * TAU; p2 += f * 1.5 / SR * TAU;
      lp += (this.noise() - lp) * 0.2;
      const s = Math.sin(p1) * 0.45 + Math.sin(p2) * 0.2 + lp * 0.5 * env(t, 0.001, 0.08);
      d[i] = s * env(t, 0.003, dur) * 0.8;
    }
    return d;
  }

  lowNoise(dur, amp, f0, f1) {
    const n = Math.floor(dur * SR), d = new Float32Array(n);
    let lp = 0, p = 0;
    for (let i = 0; i < n; i++) {
      const t = i / SR;
      lp += (this.noise() - lp) * 0.05;
      p += sweep(f0, f1, t, dur) / SR * TAU;
      d[i] = (lp * 1.5 + Math.sin(p) * 0.5) * amp * env(t, 0.002, dur);
    }
    return d;
  }

  whoosh() {
    const dur = 0.4, n = Math.floor(dur * SR), d = new Float32Array(n);
    let lp = 0, bp = 0, p = 0;
    for (let i = 0; i < n; i++) {
      const t = i / SR;
      const cut = 0.02 + 0.25 * Math.sin(Math.PI * t / dur);
      lp += (this.noise() - lp) * cut;
      bp += (lp - bp) * 0.5;
      p += sweep(700, 180, t, dur) / SR * TAU;
      d[i] = (bp * 0.8 + Math.sin(p) * 0.25) * Math.sin(Math.PI * t / dur) * 0.7;
    }
    return d;
  }

  door(open) {
    const dur = 0.55, n = Math.floor(dur * SR), d = new Float32Array(n);
    let lp = 0, p = 0;
    for (let i = 0; i < n; i++) {
      const t = i / SR;
      lp += (this.noise() - lp) * 0.04;
      p += (open ? sweep(90, 160, t, dur) : sweep(160, 80, t, dur)) / SR * TAU;
      const clunk = t > dur - 0.08 ? this.noise() * env(t - (dur - 0.08), 0.001, 0.08) * 0.5 : 0;
      d[i] = (lp * 2.2 + Math.sin(p) * 0.3) * 0.45 * Math.min(1, t * 30) * (1 - t / dur * 0.5) + clunk;
    }
    return d;
  }

  win() {
    const notes = [523.25, 659.25, 783.99, 1046.5], dur = 1.1, n = Math.floor(dur * SR), d = new Float32Array(n);
    for (let k = 0; k < notes.length; k++) {
      const start = k * 0.09;
      for (let i = Math.floor(start * SR); i < n; i++) {
        const t = i / SR - start;
        const s = Math.sin(TAU * notes[k] * t) * 0.6 + Math.sin(TAU * notes[k] * 2 * t) * 0.15;
        d[i] += s * env(t, 0.005, dur - start) * 0.22;
      }
    }
    return d;
  }

  windLoop() {
    const n = 2 * SR, d = new Float32Array(n);
    let a = 0, b = 0;
    for (let i = 0; i < n; i++) { a += (this.noise() - a) * 0.12; b += (a - b) * 0.12; d[i] = (a - b) * 3.2; }
    const x = 2000;   // fade the seam so the loop does not tick
    for (let i = 0; i < x; i++) { const k = i / x; d[i] = d[i] * k + d[n - x + i] * (1 - k) * (1 - k); }
    return d;
  }
  rainLoop() {
    // hiss of rain: band-passed noise with a slow swell so it breathes instead of roaring
    const n = 2 * SR, d = new Float32Array(n);
    let lp = 0, bp = 0;
    for (let i = 0; i < n; i++) {
      const t = i / SR;
      const lfo = 0.55 + 0.45 * Math.sin(TAU * t / 1.7);
      lp += (this.noise() - lp) * 0.05;
      bp += (lp - bp) * 0.08;
      d[i] = bp * 3.4 * lfo;
    }
    const x = 2000;
    for (let i = 0; i < x; i++) { const k = i / x; d[i] = d[i] * k + d[n - x + i] * (1 - k) * (1 - k); }
    return d;
  }
  ambience() {
    const dur = 6, n = Math.floor(dur * SR), d = new Float32Array(n);
    let lp = 0;
    for (let i = 0; i < n; i++) {
      const t = i / SR;
      lp += (this.noise() - lp) * 0.01;
      const lfo = 0.6 + 0.4 * Math.sin(TAU * t / dur);
      const s = Math.sin(TAU * 55 * t) * 0.35 + Math.sin(TAU * 82.5 * t) * 0.2 * lfo + Math.sin(TAU * 110 * t) * 0.08 + lp * 0.9;
      d[i] = s * 0.35;
    }
    return d;
  }
}

function sweep(a, b, t, dur) { return a * Math.pow(b / a, clamp(t / dur, 0, 1)); }
function env(t, attack, dur) {
  if (t < attack) return t / attack;
  const d = (t - attack) / Math.max(0.0001, dur - attack);
  return Math.max(0, Math.pow(1 - Math.min(1, d), 2.2));
}
