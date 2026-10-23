// Saves, player-made maps, share codes and the featured list. Port of Game/Save.cs and Game/CustomMaps.cs, with
// localStorage standing in for the AppData folder. Share codes are byte-compatible with the desktop game
// (P2D1: + base64 of gzipped JSON), so a code made on either version plays on the other.
import { clamp } from './core.js';
import { THEME_COUNT } from './art.js';

const SAVE_KEY = 'portal2d.save.v1';
const MAPS_KEY = 'portal2d.maps.v1';

function lsGet(key) { try { return localStorage.getItem(key); } catch { return null; } }
function lsSet(key, v) { try { localStorage.setItem(key, v); return true; } catch { return false; } }

// ------------------------------------------------------------------ progress

export class SaveData {
  constructor() {
    this.times = {};             // best times keyed by chamber name (stable when chambers are added or reordered)
    this.master = 0.8; this.sfx = 1; this.ambience = 0.5;
    this.cones = true; this.shake = true; this.fullscreen = false;
    this.totalDeaths = 0; this.randomBestStreak = 0; this.randomCleared = 0;
    this.oneClickPortals = false; this.editorHelpSeen = false;
    this.best = [];
    this.names = [];
  }

  static load(names) {
    const d = new SaveData();
    try {
      const raw = lsGet(SAVE_KEY);
      if (raw) {
        const o = JSON.parse(raw);
        for (const k of Object.keys(d)) if (k !== 'best' && k !== 'names' && o[k] !== undefined && typeof o[k] === typeof d[k]) d[k] = o[k];
      }
    } catch { /* a corrupt save is just a fresh start */ }
    d.names = names;
    d.best = names.map((n) => d.times[n] || 0);
    return d;
  }

  store() {
    for (let i = 0; i < this.names.length && i < this.best.length; i++) if (this.best[i] > 0) this.times[this.names[i]] = this.best[i];
    const o = {};
    for (const k of Object.keys(this)) if (k !== 'best' && k !== 'names') o[k] = this[k];
    lsSet(SAVE_KEY, JSON.stringify(o));
  }
}

// ------------------------------------------------------------------ custom maps

export const MIN_W = 12, MIN_H = 10, MAX_W = 90, MAX_H = 45, MAX_NAME = 24;
/** Every tile character a player-made map may contain (no fixed-portal markers). */
const ALLOWED = ' #X=~|PECabcdef123xyz456rst<>^vJGQqMNTUVWYZ';
const CODE_PREFIX = 'P2D1:';

const newId = () => Array.from(crypto.getRandomValues(new Uint8Array(4)), (b) => b.toString(16).padStart(2, '0')).join('');

export function cleanName(n) {
  let s = '';
  for (const c of (n || '').toUpperCase()) if (/[A-Z0-9]/.test(c) || " -'!.,&".includes(c)) s += c;
  s = s.trim();
  if (s.length > MAX_NAME) s = s.slice(0, MAX_NAME).trimEnd();
  return s.length === 0 ? 'MY CHAMBER' : s;
}

export class CustomMap {
  constructor(o = {}) {
    this.id = o.id || newId();
    this.name = o.name ?? 'My Chamber';
    this.map = o.map || [];
    this.gun = o.gun ?? 2;          // 0 = no portal gun, 1 = blue only, 2 = both portals
    this.theme = o.theme ?? 0;
    this.verified = !!o.verified;   // set once the player has finished the map themselves
    this.best = o.best || 0;
    this.modified = o.modified || Date.now();
  }
  get w() { return this.map.length === 0 ? 0 : this.map[0].length; }
  get h() { return this.map.length; }

  /** A fresh room: a metal box with a two-tile floor, a start and an exit. */
  static blank(name) {
    const w = 32, h = 18;
    const rows = [];
    for (let y = 0; y < h; y++) {
      let r = '';
      for (let x = 0; x < w; x++) r += x === 0 || y === 0 || x === w - 1 || y >= h - 2 ? 'X' : ' ';
      rows.push(r);
    }
    const set = (x, y, c) => { rows[y] = rows[y].slice(0, x) + c + rows[y].slice(x + 1); };
    set(3, 15, 'P'); set(28, 15, 'E');
    return new CustomMap({ name, map: rows });
  }

  toDef() {
    return {
      name: this.name.trim().toLowerCase() === 'menu' ? 'My Menu' : this.name, map: [...this.map],
      gun: clamp(this.gun, 0, 2), blueDir: { x: 0, y: 0 }, orangeDir: { x: 0, y: 0 }, hints: [], fling: false, label: '', theme: this.theme, seed: 0,
    };
  }

  clone() { return new CustomMap({ ...this, map: [...this.map] }); }

  /** Makes a map safe to load whatever a file or a pasted code contained. Returns false if it is unusable. */
  sanitize() {
    if (!Array.isArray(this.map) || this.map.length < MIN_H) return false;
    const h = Math.min(this.map.length, MAX_H);
    const w = Math.min(Math.max(...this.map.map((r) => (typeof r === 'string' ? r.length : 0))), MAX_W);
    if (!(w >= MIN_W)) return false;
    const rows = [];
    let hasP = false, hasE = false;
    for (let y = 0; y < h; y++) {
      const src = typeof this.map[y] === 'string' ? this.map[y] : '';
      let row = '';
      for (let x = 0; x < w; x++) {
        let c = x < src.length ? src[x] : 'X';
        if (!ALLOWED.includes(c)) c = ' ';
        if (c === 'P') { if (hasP) c = ' '; hasP = true; }
        if (c === 'E') { if (hasE) c = ' '; hasE = true; }
        row += c;
      }
      rows.push(row);
    }
    this.map = rows;
    this.gun = clamp(Math.trunc(Number(this.gun)) || 0, 0, 2);
    this.theme = clamp(Math.trunc(Number(this.theme)) || 0, 0, THEME_COUNT - 1);
    this.name = cleanName(this.name);
    if (typeof this.id !== 'string' || this.id.length === 0 || this.id.length > 16 || !/^[A-Za-z0-9]+$/.test(this.id)) this.id = newId();
    if (!Number.isFinite(this.best) || this.best < 0) this.best = 0;
    return true;
  }

  // ---- sharing: a map as one line of text that can be pasted into a message

  async toShareCode() {
    // same shape as the C# serializer: PascalCase fields
    const json = JSON.stringify({ Name: this.name, Map: this.map, Gun: this.gun, Theme: this.theme });
    const stream = new Blob([json]).stream().pipeThrough(new CompressionStream('gzip'));
    const bytes = new Uint8Array(await new Response(stream).arrayBuffer());
    let bin = '';
    for (let i = 0; i < bytes.length; i += 8192) bin += String.fromCharCode(...bytes.subarray(i, i + 8192));
    return CODE_PREFIX + btoa(bin);
  }

  static async fromShareCode(text) {
    try {
      text = (text || '').trim();
      // accept a pasted link as well as the bare code
      const at = text.indexOf(CODE_PREFIX);
      if (at < 0) return null;
      text = text.slice(at).split(/[\s"'<>]/)[0];
      const bin = atob(text.slice(CODE_PREFIX.length));
      const bytes = Uint8Array.from(bin, (c) => c.charCodeAt(0));
      const stream = new Blob([bytes]).stream().pipeThrough(new DecompressionStream('gzip'));
      const buf = await new Response(stream).arrayBuffer();
      if (buf.byteLength > 1_000_000) return null;
      const o = JSON.parse(new TextDecoder().decode(buf));
      const m = new CustomMap({ name: o.Name, map: o.Map, gun: o.Gun, theme: o.Theme });
      return m.sanitize() ? m : null;       // an imported map is always a new, untested map of your own
    } catch {
      return null;
    }
  }
}

/** The player's maps, kept in localStorage as one JSON array. */
export const CustomMaps = {
  loadAll() {
    const list = [];
    try {
      const arr = JSON.parse(lsGet(MAPS_KEY) || '[]');
      const seen = new Set();
      for (const o of arr) {
        const m = new CustomMap(o);
        if (!m.sanitize()) continue;
        if (seen.has(m.id)) m.id = newId();
        seen.add(m.id);
        list.push(m);
      }
    } catch { /* unreadable storage just means no maps */ }
    return list.sort((a, b) => b.modified - a.modified);
  },
  _write(all) { return lsSet(MAPS_KEY, JSON.stringify(all)); },
  /** Writes the map. Returns false if the browser refused (storage full or blocked). */
  save(m) {
    m.modified = Date.now();
    const all = this.loadAll().filter((x) => x.id !== m.id);
    all.push(m);
    return this._write(all);
  },
  delete(m) { this._write(this.loadAll().filter((x) => x.id !== m.id)); },
  duplicate(m) {
    const c = m.clone();
    c.id = newId();
    c.name = cleanName(m.name.length > MAX_NAME - 5 ? m.name.slice(0, MAX_NAME - 5) + ' COPY' : m.name + ' COPY');
    c.verified = false; c.best = 0;
    this.save(c);
    return c;
  },
  /** Every map as a downloadable JSON backup. */
  exportAll() {
    const blob = new Blob([JSON.stringify(this.loadAll(), null, 2)], { type: 'application/json' });
    const a = document.createElement('a');
    a.href = URL.createObjectURL(blob);
    a.download = 'portal2d-maps.json';
    a.click();
    setTimeout(() => URL.revokeObjectURL(a.href), 2000);
  },
  newId,
};

// ------------------------------------------------------------------ featured maps

/** Hand-picked maps hosted in the game's repository (featured/maps.json), fetched only when the list is opened. */
export const FeaturedMaps = {
  url: 'https://raw.githubusercontent.com/xandertaylorx/portal-2d-game/main/featured/maps.json',
  status: 'idle',      // idle | loading | ready | failed
  items: [],
  async load() {
    if (this.status === 'loading') return;
    this.status = 'loading';
    try {
      const ctl = new AbortController();
      const timer = setTimeout(() => ctl.abort(), 10000);
      const res = await fetch(this.url, { signal: ctl.signal });
      clearTimeout(timer);
      if (!res.ok) throw new Error(String(res.status));
      const doc = await res.json();
      const items = [];
      for (const r of doc.maps || []) {
        const m = await CustomMap.fromShareCode(r.code);
        if (!m) continue;
        if (r.name && String(r.name).trim()) m.name = cleanName(r.name);
        items.push({ name: m.name, author: cleanName(r.author), note: r.note || '', map: m });
        if (items.length >= 60) break;
      }
      this.items = items;
      this.status = 'ready';
    } catch {
      this.status = 'failed';
    }
  },
};
