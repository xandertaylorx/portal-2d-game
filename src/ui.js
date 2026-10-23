// Pixel-art immediate-mode UI (port of UI/UI.cs and UI/Icons.cs). Coordinates are virtual 1280x720 units
// (2 units = 1 canvas pixel).
import { K, C, withAlpha, roundEven, approach, clamp01, contains, V, WHITE } from './core.js';
import { PixelFont } from './gfx.js';
import { PP } from './art.js';
import { World } from './world.js';

export const Align = { Left: 0, Center: 1, Right: 2 };

const PANEL_FILL = C(18, 20, 27, 240);
export const PANEL_EDGE = C(58, 65, 80);
const PANEL_HI = C(84, 93, 114);
const DIM = C(150, 158, 172);

const snapR = (r) => ({ x: roundEven(r.x / 2) * 2, y: roundEven(r.y / 2) * 2, w: roundEven(r.w / 2) * 2, h: roundEven(r.h / 2) * 2 });

export const UI = {
  g: null,
  mouse: V(0, 0),
  click: false,
  mouseDown: false,
  time: 0,
  dt: 0,
  hover: new Map(),
  lastHovered: null,
  onHover: null,
  onClick: null,
  Dim: DIM,

  init(g) { this.g = g; },

  beginFrame(dt, mouse, click, down) {
    this.dt = dt;
    this.time += dt;
    this.mouse = mouse;
    this.click = click;
    this.mouseDown = down;
  },

  /** Map a "font size" in virtual units to an integer pixel scale. */
  scale(size) { return size < 26 ? 1 : size < 40 ? 2 : size < 56 ? 3 : size < 80 ? 4 : Math.max(5, Math.trunc(size * K.Px / 7.5)); },
  measure(s, size) { return PixelFont.measure(s, this.scale(size)); },

  /** Trims text so it fits in maxW virtual units at the given size (ends with ".." when cut). */
  fit(s, size, maxW) {
    if (this.measure(s, size).x <= maxW) return s;
    while (s.length > 1 && this.measure(s + '..', size).x > maxW) s = s.slice(0, -1);
    return s.trimEnd() + '..';
  },

  text(s, pos, size, col, align = Align.Left, bold = false) {
    const sc = this.scale(size), m = PixelFont.measure(s, sc);
    const x = align === Align.Center ? pos.x - m.x / 2 : align === Align.Right ? pos.x - m.x : pos.x;
    const y = align === Align.Center ? pos.y - m.y / 2 : pos.y;
    if (bold) PixelFont.draw(this.g, s, V(x, y + sc / K.Px), sc, C(0, 0, 0, col.a * 0.6));
    PixelFont.draw(this.g, s, V(x, y), sc, col);
  },

  textShadow(s, pos, size, col, align = Align.Left) {
    const sc = this.scale(size), m = PixelFont.measure(s, sc);
    const x = align === Align.Center ? pos.x - m.x / 2 : align === Align.Right ? pos.x - m.x : pos.x;
    const y = align === Align.Center ? pos.y - m.y / 2 : pos.y;
    const u = sc / K.Px, shadow = C(10, 10, 14, col.a);
    for (const [dx, dy] of [[-1, 0], [1, 0], [0, -1], [0, 1], [1, 1], [0, 2], [1, 2]]) PixelFont.draw(this.g, s, V(x + dx * u, y + dy * u), sc, shadow);
    PixelFont.draw(this.g, s, V(x, y), sc, col);
  },

  /** Pixel panel: dark fill, 1 px border, highlight line, clipped corners. */
  panel(r, alpha = 1, accent = null) {
    const g = this.g;
    r = snapR(r);
    const x = Math.trunc(r.x), y = Math.trunc(r.y), w = Math.trunc(r.w), h = Math.trunc(r.h);
    const fill = withAlpha(PANEL_FILL, alpha * PANEL_FILL.a / 255);
    g.rect(x + 2, y, w - 4, h, fill);
    g.rect(x, y + 2, w, h - 4, fill);
    const e = withAlpha(accent || PANEL_EDGE, alpha * (accent ? accent.a / 255 : 1));
    g.rect(x + 2, y, w - 4, 2, e);
    g.rect(x + 2, y + h - 2, w - 4, 2, e);
    g.rect(x, y + 2, 2, h - 4, e);
    g.rect(x + w - 2, y + 2, 2, h - 4, e);
    g.rect(x + 4, y + 2, w - 8, 2, withAlpha(PANEL_HI, alpha * 0.5));
    g.rect(x + 2, y + h, w - 4, 2, C(0, 0, 0, 110 * alpha));
  },

  hoverAmount(id, over) {
    let h = this.hover.get(id) || 0;
    h = approach(h, over ? 1 : 0, this.dt * 10);
    this.hover.set(id, h);
    if (over && this.lastHovered !== id) { this.lastHovered = id; this.onHover?.(); }
    if (!over && this.lastHovered === id) this.lastHovered = null;
    return h;
  },

  contains,

  button(id, r, label, { enabled = true, accent = null, sub = null } = {}) {
    const g = this.g;
    const over = enabled && contains(r, this.mouse);
    const h = this.hoverAmount(id, over);
    const acc = accent || PP.BlueRamp[2];
    const hot = h > 0.5;
    const rr = { x: r.x + (hot ? 8 : 0), y: r.y, w: r.w, h: r.h };
    this.panel(rr, 1, hot ? acc : null);
    g.rect(Math.trunc(rr.x) + 2, Math.trunc(rr.y) + 2, hot ? 8 : 4, Math.trunc(rr.h) - 4, enabled ? acc : C(70, 70, 70));
    const col = enabled ? (hot ? WHITE : PP.Cream) : C(100, 104, 112);
    const tx = rr.x + 30;
    if (hot && Math.trunc(this.time * 3) % 2 === 0) this.text('>', V(rr.x + 16, rr.y + rr.h / 2 - 7), 16, acc);
    const maxW = rr.w - 30 - 16;
    label = this.fit(label, 30, maxW);
    if (sub == null) this.text(label, V(tx, rr.y + rr.h / 2 - 14), 30, col, Align.Left, true);
    else {
      this.text(label, V(tx, rr.y + 6), 30, col, Align.Left, true);
      this.text(this.fit(sub, 16, maxW), V(tx, rr.y + rr.h - 20), 16, C(150, 158, 172));
    }
    if (over && this.click) { this.onClick?.(); return true; }
    return false;
  },

  smallButton(id, r, label, accent = null) {
    const g = this.g;
    const over = contains(r, this.mouse);
    const h = this.hoverAmount(id, over);
    const acc = accent || PP.BlueRamp[2];
    r = snapR(r);
    const hot = h > 0.5;
    g.rect(r.x + 2, r.y + 2, r.w, r.h, C(0, 0, 0, 120));
    g.rectR(r, hot ? acc : C(30, 34, 44));
    g.rectLines(r, 2, hot ? withAlpha(WHITE, 0.7) : acc);
    this.text(this.fit(label, 16, r.w - 12), V(r.x + r.w / 2, r.y + r.h / 2), 16, hot ? PP.Ink : PP.Cream, Align.Center);
    if (over && this.click) { this.onClick?.(); return true; }
    return false;
  },

  /** Returns the new value, or null when it did not change. */
  slider(id, r, label, value) {
    const g = this.g;
    const over = contains({ x: r.x, y: r.y - 8, w: r.w, h: r.h + 16 }, this.mouse);
    this.hoverAmount(id, over);
    this.text(label, V(r.x, r.y - 26), 16, C(150, 158, 172));
    this.text(`${Math.round(value * 100)}%`, V(r.x + r.w, r.y - 26), 16, PP.Cream, Align.Right);
    const segs = 20, sw = r.w / segs;
    for (let i = 0; i < segs; i++) {
      const on = (i + 0.5) / segs <= value;
      g.rect(Math.trunc(r.x + i * sw), Math.trunc(r.y) + 4, Math.trunc(sw) - 2, 12, on ? PP.BlueRamp[2] : C(44, 50, 63));
    }
    if (over && this.mouseDown) {
      const nv = Math.round(clamp01((this.mouse.x - r.x) / r.w) * 20) / 20;
      if (Math.abs(nv - value) > 0.001) return nv;
    }
    return null;
  },

  /** Returns the new value when clicked, otherwise null. */
  toggle(id, r, label, value) {
    const g = this.g;
    const over = contains(r, this.mouse);
    const h = this.hoverAmount(id, over);
    this.panel(r, 1, h > 0.5 ? PP.BlueRamp[2] : null);
    this.text(this.fit(label, 16, r.w - 18 - 74 - 80), V(r.x + 18, r.y + r.h / 2 - 7), 16, PP.Cream);
    const sw = snapR({ x: r.x + r.w - 74, y: r.y + r.h / 2 - 12, w: 56, h: 24 });
    g.rectR(sw, value ? PP.BlueRamp[1] : C(44, 50, 63));
    g.rectLines(sw, 2, value ? PP.BlueRamp[3] : C(70, 78, 96));
    g.rect(value ? Math.trunc(sw.x) + 32 : Math.trunc(sw.x) + 4, Math.trunc(sw.y) + 4, 20, 16, PP.Cream);
    this.text(value ? 'ON' : 'OFF', V(sw.x - 12, r.y + r.h / 2 - 7), 16, value ? PP.BlueRamp[3] : C(120, 128, 140), Align.Right);
    if (over && this.click) { this.onClick?.(); return !value; }
    return null;
  },

  keyCap(key, pos, minW = 34) {
    const g = this.g;
    const w = this.keyCapWidth(key, minW);
    const r = snapR({ x: pos.x, y: pos.y, w, h: 32 });
    g.rect(r.x, r.y + 4, r.w, r.h, C(10, 12, 16));
    g.rectR(r, C(70, 78, 96));
    g.rect(r.x + 2, r.y + 2, r.w - 4, r.h - 8, C(96, 106, 128));
    this.text(key, V(r.x + r.w / 2, r.y + 13), 16, WHITE, Align.Center);
  },
  keyCapWidth(key, minW = 34) { return Math.max(minW, PixelFont.measure(key, 1).x + 20); },

  formatTime(t) {
    if (t <= 0) return '--:--.--';
    const m = Math.trunc(t / 60), s = t - m * 60;
    return `${String(m).padStart(2, '0')}:${s.toFixed(2).padStart(5, '0')}`;
  },
};

// ------------------------------------------------------------------ mechanic pictograms

/** Little pictograms for the mechanics used in a chamber (like the signs in the original game). */
export const Icons = {
  mechanics(def) {
    const all = def.map.join('');
    const list = ['portal'];
    if (all.includes('C')) list.push('cube');
    if ([...all].some((ch) => World.buttonGroup(ch) >= 0)) list.push('button');
    if (all.includes('~')) list.push('goo');
    if (all.includes('|')) list.push('grid');
    if (/[<>^v]/.test(all)) list.push('laser');
    if (all.includes('=')) list.push('glass');
    if (def.fling) list.push('fling');
    return list;
  },

  draw(g, def, pos, size, col, alignRight, filled = false) {
    const list = this.mechanics(def), step = size + 6;
    for (let i = 0; i < list.length; i++) {
      const x = alignRight ? pos.x - size - i * step : pos.x + i * step;
      this.box(g, x, pos.y, size, col, filled);
      this.glyph(g, list[i], V(x + size / 2, pos.y + size / 2), size * 0.5, filled ? C(236, 240, 243, col.a) : col);
    }
  },

  box(g, x, y, size, col, filled) {
    const ctx = g.ctx;
    g._local();
    ctx.globalAlpha = g.opacity;
    ctx.beginPath();
    ctx.roundRect(x, y, size, size, size * 0.1);
    if (filled) { ctx.fillStyle = col.css; ctx.fill(); }
    else { ctx.strokeStyle = col.css; ctx.lineWidth = 1.5; ctx.stroke(); }
    g._reset();
  },

  glyph(g, m, c, s, col) {
    const ctx = g.ctx;
    g._local();
    ctx.globalAlpha = g.opacity;
    ctx.strokeStyle = col.css; ctx.fillStyle = col.css; ctx.lineWidth = 1.5;
    const line = (a, b, w = 1.5) => { ctx.lineWidth = w; ctx.beginPath(); ctx.moveTo(a[0], a[1]); ctx.lineTo(b[0], b[1]); ctx.stroke(); };
    switch (m) {
      case 'portal':
        ctx.lineWidth = 2;
        for (const dx of [-0.35, 0.35]) { ctx.beginPath(); ctx.ellipse(c.x + dx * s, c.y, s * 0.22, s * 0.75, 0, 0, Math.PI * 2); ctx.stroke(); }
        break;
      case 'cube':
        ctx.lineWidth = 1.5; ctx.strokeRect(c.x - s * 0.6, c.y - s * 0.6, s * 1.2, s * 1.2);
        ctx.beginPath(); ctx.arc(c.x, c.y, s * 0.22, 0, Math.PI * 2); ctx.fill();
        break;
      case 'button':
        ctx.fillRect(c.x - s * 0.7, c.y + s * 0.3, s * 1.4, s * 0.25);
        ctx.beginPath(); ctx.moveTo(c.x - s * 0.45, c.y + s * 0.3); ctx.arc(c.x, c.y + s * 0.3, s * 0.45, Math.PI, Math.PI * 2); ctx.fill();
        break;
      case 'goo':
        for (let k = 0; k < 2; k++) {
          const y = c.y - s * 0.2 + k * s * 0.45;
          ctx.lineWidth = 1.5;
          ctx.beginPath(); ctx.moveTo(c.x - s * 0.7, y); ctx.quadraticCurveTo(c.x - s * 0.35, y - s * 0.4, c.x, y); ctx.quadraticCurveTo(c.x + s * 0.35, y + s * 0.4, c.x + s * 0.7, y); ctx.stroke();
        }
        break;
      case 'grid':
        for (let k = -1; k <= 1; k++) line([c.x + k * s * 0.4, c.y - s * 0.7], [c.x + k * s * 0.4, c.y + s * 0.7]);
        break;
      case 'laser':
        ctx.beginPath(); ctx.arc(c.x - s * 0.5, c.y, s * 0.25, 0, Math.PI * 2); ctx.fill();
        line([c.x - s * 0.5, c.y], [c.x + s * 0.75, c.y], 2);
        line([c.x + s * 0.45, c.y - s * 0.35], [c.x + s * 0.75, c.y]);
        line([c.x + s * 0.45, c.y + s * 0.35], [c.x + s * 0.75, c.y]);
        break;
      case 'glass':
        ctx.lineWidth = 1.5; ctx.strokeRect(c.x - s * 0.55, c.y - s * 0.7, s * 1.1, s * 1.4);
        line([c.x - s * 0.3, c.y + s * 0.3], [c.x + s * 0.2, c.y - s * 0.3]);
        break;
      case 'fling':
        ctx.lineWidth = 2; ctx.beginPath(); ctx.moveTo(c.x - s * 0.7, c.y + s * 0.6); ctx.quadraticCurveTo(c.x - s * 0.5, c.y - s * 0.8, c.x + s * 0.6, c.y - s * 0.2); ctx.stroke();
        ctx.beginPath(); ctx.moveTo(c.x + s * 0.75, c.y - s * 0.15); ctx.lineTo(c.x + s * 0.35, c.y - s * 0.45); ctx.lineTo(c.x + s * 0.4, c.y + s * 0.05); ctx.closePath(); ctx.fill();
        break;
    }
    g._reset();
  },
};
