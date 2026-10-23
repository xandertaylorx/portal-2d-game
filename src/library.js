// The "My Maps" screen: every map the player has built, with play / edit / share / delete, plus the featured list.
// Port of Game/MapLibrary.cs.
import { K, C, withAlpha, clamp, clamp01, contains, V, R, roundEven } from './core.js';
import { PP } from './art.js';
import { World } from './world.js';
import { UI, Align, PANEL_EDGE } from './ui.js';
import { Input } from './input.js';
import { CustomMap, CustomMaps, FeaturedMaps, cleanName } from './maps.js';
import { levelPreview } from './preview.js';

const COLS = 3, PER_PAGE = 6;
const CW = 372, CH = 232, GX = 18, GY = 14, Y0 = 134;

/** Copies text, falling back to a prompt() the player can copy from (Safari and some embeds refuse async clipboard writes). */
async function copyText(text) {
  try { await navigator.clipboard.writeText(text); return true; }
  catch { window.prompt('Copy this link to share your map:', text); return false; }
}

async function readClipboard() {
  try { const t = await navigator.clipboard.readText(); if (t) return t; } catch { /* fall through to the prompt */ }
  return window.prompt('Paste a map code or link here:') || '';
}

export class MapLibrary {
  constructor(g) {
    this.g = g;
    this.maps = [];
    this.page = 0;
    this.featuredView = false;
    this.confirmDelete = null;
    this.toast = ''; this.toastT = 0;
    this.thumbs = new Map();
  }

  reload() {
    this.thumbs.clear();
    this.featuredView = false;
    this.maps = CustomMaps.loadAll();
    this.page = Math.min(this.page, Math.max(0, Math.ceil((this.maps.length + 1) / PER_PAGE) - 1));
  }

  showToast(s) { this.toast = s; this.toastT = 3.5; }

  thumb(m, state) {
    const stamp = m.modified;
    const t = this.thumbs.get(m.id);
    if (t && t.stamp === stamp) return t.cv;
    if (state.madeOne) return null;          // at most one new thumbnail per frame
    state.madeOne = true;
    try {
      const cv = levelPreview(new World(m.toDef())).toCanvas();
      this.thumbs.set(m.id, { stamp, cv });
      return cv;
    } catch { return null; }
  }

  drawThumb(area, tex) {
    const g = this.g;
    g.rectR(area, C(10, 12, 16));
    if (tex) {
      const sc = Math.min(area.w / tex.width, area.h / tex.height), tw = tex.width * sc, th = tex.height * sc;
      g.tex(tex, 0, 0, tex.width, tex.height, area.x + (area.w - tw) / 2, area.y + (area.h - th) / 2, tw, th);
    }
    g.rectLines(area, 2, PANEL_EDGE);
  }

  /** Returns { act: 'none'|'new'|'play'|'edit', map }. */
  draw(dt, stateT, dim) {
    this.toastT = Math.max(0, this.toastT - dt);
    let act = 'none', target = null;
    const state = { madeOne: false };
    const modal = this.confirmDelete != null;
    if (this.featuredView) { this.drawFeatured(stateT, dim, state); return { act, map: target }; }

    const slots = this.maps.length + 1, pages = Math.ceil(slots / PER_PAGE);
    this.page = clamp(this.page, 0, pages - 1);
    const x0 = roundEven((K.ScreenW - (COLS * CW + (COLS - 1) * GX)) / 2);

    for (let s = this.page * PER_PAGE; s < Math.min(slots, this.page * PER_PAGE + PER_PAGE); s++) {
      const local = s - this.page * PER_PAGE;
      const r = R(x0 + (local % COLS) * (CW + GX), Y0 + Math.floor(local / COLS) * (CH + GY), CW, CH);
      if (stateT * 5 - local * 0.1 < 0) continue;
      if (s === 0) { if (this.newCard(r, modal)) act = 'new'; continue; }
      const m = this.maps[s - 1];
      const a = this.mapCard(r, m, modal, state);
      if (a !== 'none') { act = a; target = m; }
    }

    if (this.maps.length === 0 && this.page === 0)
      UI.text('NO MAPS YET. MAKE YOUR FIRST CHAMBER!', V(x0 + CW + GX + 24, Y0 + 100), 16, dim);

    // footer
    const fy = Y0 + 2 * (CH + GY) + 6, fw = COLS * CW + (COLS - 1) * GX;
    if (!modal && UI.smallButton('lib_paste', R(x0, fy, 250, 44), 'PASTE MAP CODE', PP.Exit)) this.importFromClipboard();
    if (!modal && UI.smallButton('lib_backup', R(x0 + 262, fy, 250, 44), 'BACK UP ALL MAPS')) {
      if (this.maps.length === 0) this.showToast('NO MAPS TO BACK UP YET'); else CustomMaps.exportAll();
    }
    if (!modal && UI.smallButton('lib_featured', R(x0 + 524, fy, 250, 44), 'FEATURED MAPS', PP.OrangeRamp[2])) {
      this.featuredView = true; this.page = 0;
      if (FeaturedMaps.status === 'idle' || FeaturedMaps.status === 'failed') FeaturedMaps.load();
    }
    if (pages > 1) {
      const px = x0 + fw - 230;
      if (this.page > 0 && !modal && UI.smallButton('lib_prev', R(px, fy, 60, 44), '<')) this.page--;
      UI.text(`PAGE ${this.page + 1}/${pages}`, V(px + 115, fy + 22), 16, PP.Cream, Align.Center);
      if (this.page < pages - 1 && !modal && UI.smallButton('lib_next', R(px + 170, fy, 60, 44), '>')) this.page++;
    }
    UI.text('SHARE: BEAT YOUR MAP, PRESS SHARE, SEND THE LINK. ADD ONE: PRESS PASTE. BACK UP TO KEEP A COPY.',
      V(x0 + 2, fy + 58), 16, withAlpha(dim, 0.8));

    this.drawToast();
    if (this.confirmDelete) this.drawConfirm();
    return { act, map: target };
  }

  drawFeatured(stateT, dim, state) {
    const items = FeaturedMaps.items, g = this.g;
    const pages = Math.max(1, Math.ceil(items.length / PER_PAGE));
    this.page = clamp(this.page, 0, pages - 1);
    const x0 = roundEven((K.ScreenW - (COLS * CW + (COLS - 1) * GX)) / 2), fw = COLS * CW + (COLS - 1) * GX;
    const st = FeaturedMaps.status;
    if (st !== 'ready' || items.length === 0) {
      const msg = st === 'loading' ? 'LOADING FEATURED MAPS...'
        : st === 'failed' ? 'COULD NOT REACH THE FEATURED LIST. CHECK YOUR CONNECTION.'
        : st === 'ready' ? 'NO FEATURED MAPS YET. CHECK BACK SOON!' : '';
      UI.text(msg, V(K.ScreenW / 2, 300), 16, dim, Align.Center);
      if (st === 'failed' && UI.smallButton('feat_retry', R(K.ScreenW / 2 - 100, 340, 200, 44), 'TRY AGAIN', PP.Exit)) FeaturedMaps.load();
    }
    for (let s = this.page * PER_PAGE; s < Math.min(items.length, this.page * PER_PAGE + PER_PAGE); s++) {
      const local = s - this.page * PER_PAGE;
      const r = R(x0 + (local % COLS) * (CW + GX), Y0 + Math.floor(local / COLS) * (CH + GY), CW, CH);
      if (stateT * 5 - local * 0.1 < 0) continue;
      const e = items[s], m = e.map;
      UI.panel(r, 1, withAlpha(PP.OrangeRamp[2], 0.8));
      this.drawThumb(R(r.x + 12, r.y + 12, r.w - 24, 124), this.thumb(m, state));
      UI.text(UI.fit(e.name, 16, r.w - 28), V(r.x + 14, r.y + 146), 16, PP.CreamHi, Align.Left, true);
      UI.text(UI.fit(e.author.length > 0 ? 'BY ' + e.author : e.note, 16, r.w - 100), V(r.x + 14, r.y + 168), 16, PP.OrangeRamp[2]);
      UI.text(`${m.w}X${m.h}`, V(r.x + r.w - 14, r.y + 168), 16, C(110, 118, 132), Align.Right);
      const have = this.maps.some((x) => x.name === m.name && x.map.join('\n') === m.map.join('\n'));
      const br = R(r.x + 14, r.y + r.h - 46, r.w - 28, 34);
      if (have) {
        g.rectR(br, C(30, 34, 44));
        UI.text('IN YOUR MAPS', V(br.x + br.w / 2, br.y + 17), 16, PP.Exit, Align.Center);
      } else if (UI.smallButton('fa' + s, br, 'ADD TO MY MAPS', PP.Exit)) {
        const copy = m.clone();
        copy.id = CustomMaps.newId();
        copy.verified = false; copy.best = 0;
        if (CustomMaps.save(copy)) { this.maps.unshift(copy); this.showToast(`ADDED ${copy.name} - FIND IT IN MY MAPS`); }
        else this.showToast('COULD NOT SAVE THE MAP');
      }
    }
    const fy = Y0 + 2 * (CH + GY) + 6;
    if (UI.smallButton('feat_back', R(x0, fy, 250, 44), 'BACK TO MY MAPS', PP.OrangeRamp[2])) { this.featuredView = false; this.page = 0; }
    if (pages > 1) {
      const px = x0 + fw - 230;
      if (this.page > 0 && UI.smallButton('feat_prev', R(px, fy, 60, 44), '<')) this.page--;
      UI.text(`PAGE ${this.page + 1}/${pages}`, V(px + 115, fy + 22), 16, PP.Cream, Align.Center);
      if (this.page < pages - 1 && UI.smallButton('feat_next', R(px + 170, fy, 60, 44), '>')) this.page++;
    }
    UI.text("FEATURED MAPS COME FROM THE GAME'S GITHUB PAGE. ADD ONE, THEN PLAY IT FROM MY MAPS.", V(x0 + 2, fy + 58), 16, withAlpha(dim, 0.8));
    this.drawToast();
  }

  drawToast() {
    if (this.toastT <= 0) return;
    const al = clamp01(this.toastT * 2), m = UI.measure(this.toast, 16);
    const tr = R(K.ScreenW / 2 - m.x / 2 - 24, 96, m.x + 48, 40);
    UI.panel(tr, al, PP.OrangeRamp[2]);
    UI.text(this.toast, V(tr.x + tr.w / 2, tr.y + tr.h / 2), 16, withAlpha(PP.CreamHi, al), Align.Center);
  }

  newCard(r, modal) {
    const g = this.g;
    const over = !modal && contains(r, UI.mouse);
    if (over) r = { ...r, y: r.y - 4 };
    UI.panel(r, 1, over ? PP.Exit : null);
    const c = V(r.x + r.w / 2, r.y + r.h / 2 - 24);
    const col = over ? PP.Exit : withAlpha(PP.Exit, 0.7);
    g.rect(c.x - 6, c.y - 34, 12, 68, col);
    g.rect(c.x - 34, c.y - 6, 68, 12, col);
    UI.text('NEW MAP', V(c.x, r.y + r.h - 68), 30, PP.CreamHi, Align.Center);
    UI.text('START FROM AN EMPTY ROOM', V(c.x, r.y + r.h - 30), 16, C(150, 158, 172), Align.Center);
    if (over && UI.click) { UI.onClick?.(); return true; }
    return false;
  }

  mapCard(r, m, modal, state) {
    const g = this.g;
    let act = 'none';
    UI.panel(r, 1, m.verified ? withAlpha(PP.Exit, 0.8) : null);
    const area = R(r.x + 12, r.y + 12, r.w - 24, 124);
    const tex = this.thumb(m, state);
    this.drawThumb(area, tex);
    if (!tex) UI.text('...', V(area.x + area.w / 2, area.y + area.h / 2), 16, C(90, 98, 110), Align.Center);

    UI.text(m.name, V(r.x + 14, r.y + 146), 16, PP.CreamHi, Align.Left, true);
    const status = m.verified ? (m.best > 0 ? `BEATEN - BEST ${UI.formatTime(m.best)}` : 'BEATEN') : 'NOT BEATEN YET';
    UI.text(status, V(r.x + 14, r.y + 168), 16, m.verified ? PP.Exit : PP.OrangeRamp[2]);
    UI.text(`${m.w}X${m.h}`, V(r.x + r.w - 14, r.y + 168), 16, C(110, 118, 132), Align.Right);

    const bw = (r.w - 28 - 3 * 6) / 4, by = r.y + r.h - 46, id = m.id;
    if (!modal) {
      if (UI.smallButton('lp' + id, R(r.x + 14, by, bw, 34), 'PLAY', PP.Exit)) act = 'play';
      if (UI.smallButton('le' + id, R(r.x + 14 + (bw + 6), by, bw, 34), 'EDIT', PP.OrangeRamp[2])) act = 'edit';
      if (UI.smallButton('ls' + id, R(r.x + 14 + 2 * (bw + 6), by, bw, 34), 'SHARE', m.verified ? null : C(110, 116, 128))) {
        if (!m.verified) this.showToast('BEAT YOUR MAP FIRST - THEN YOU CAN SHARE IT');
        else this.share(m);
      }
      if (UI.smallButton('ld' + id, R(r.x + 14 + 3 * (bw + 6), by, bw, 34), 'DELETE', C(255, 110, 100))) this.confirmDelete = m;
    } else {
      ['PLAY', 'EDIT', 'SHARE', 'DELETE'].forEach((label, i) => {
        const br = R(r.x + 14 + i * (bw + 6), by, bw, 34);
        g.rectR(br, C(30, 34, 44));
        UI.text(label, V(br.x + bw / 2, br.y + 17), 16, C(100, 104, 112), Align.Center);
      });
    }
    return act;
  }

  async share(m) {
    const code = await m.toShareCode();
    const link = `${location.origin}${location.pathname}#${code}`;
    const ok = await copyText(link);
    this.showToast(ok ? `LINK FOR ${m.name} COPIED - SEND IT TO A FRIEND` : 'COPY THE LINK FROM THE BOX THAT OPENED');
  }

  async importFromClipboard() {
    const m = await CustomMap.fromShareCode(await readClipboard());
    if (!m) { this.showToast('NO MAP CODE FOUND. COPY A MAP LINK FIRST.'); return; }
    this.addMap(m);
  }

  addMap(m) {
    if (!CustomMaps.save(m)) { this.showToast('COULD NOT SAVE THE MAP'); return; }
    this.maps.unshift(m);
    this.page = 0;
    this.showToast(`ADDED ${m.name}`);
  }

  drawConfirm() {
    const g = this.g;
    g.rect(0, 0, K.ScreenW, K.ScreenH, C(6, 8, 12, 190));
    const p = R(K.ScreenW / 2 - 270, K.ScreenH / 2 - 110, 540, 220);
    UI.panel(p, 1, C(255, 110, 100));
    UI.text('DELETE THIS MAP?', V(K.ScreenW / 2, p.y + 42), 30, PP.CreamHi, Align.Center);
    UI.text(this.confirmDelete.name, V(K.ScreenW / 2, p.y + 90), 16, PP.OrangeRamp[2], Align.Center);
    UI.text('THIS CANNOT BE UNDONE.', V(K.ScreenW / 2, p.y + 116), 16, C(150, 158, 172), Align.Center);
    if (UI.smallButton('del_yes', R(p.x + 40, p.y + 148, 210, 48), 'DELETE', C(255, 110, 100))) {
      CustomMaps.delete(this.confirmDelete);
      this.thumbs.delete(this.confirmDelete.id);
      this.maps = this.maps.filter((x) => x !== this.confirmDelete);
      this.confirmDelete = null;
    }
    if (UI.smallButton('del_no', R(p.x + 290, p.y + 148, 210, 48), 'KEEP IT', PP.Exit)) this.confirmDelete = null;
    if (Input.pressed('Escape')) this.confirmDelete = null;
  }

  cancelModal() {
    if (this.featuredView) { this.featuredView = false; this.page = 0; return true; }
    if (!this.confirmDelete) return false;
    this.confirmDelete = null;
    return true;
  }
}
