// The map builder. The map on screen is a real World rebuilt after every edit, so what you see is exactly what you
// will play. Left click paints, right click erases, and everything autosaves. Port of Game/MapEditor.cs.
import { K, C, withAlpha, clamp, clamp01, contains, V, WHITE, vdist2, R, roundEven } from './core.js';
import { PP, Sprites, THEME_COUNT } from './art.js';
import { World } from './world.js';
import { UI, Align, PANEL_EDGE } from './ui.js';
import { Input } from './input.js';
import { CustomMaps, cleanName, MIN_W, MIN_H, MAX_W, MAX_H, MAX_NAME } from './maps.js';

const T = K.Tile;

const SECTIONS = ['BLOCKS', 'GEL AND PLATES', 'OBJECTS', 'WIRING', 'LASERS'];

// wire: 0 button, 1 timed button, 2 door, 3 laser target (these take the number picked below the tools)
const tool = (id, name, help, ch, section, o = {}) => ({ id, name, help, ch, section, wire: o.wire ?? -1, fillable: !!o.fillable, unique: !!o.unique });
const TOOLS = [
  tool('panel', 'WHITE PANEL', 'A WALL PORTALS CAN STICK TO.', '#', 0, { fillable: true }),
  tool('metal', 'METAL WALL', "A WALL PORTALS CAN'T STICK TO.", 'X', 0, { fillable: true }),
  tool('glass', 'GLASS', 'YOU CAN SEE AND SHOOT LASERS THROUGH IT, BUT NOT WALK.', '=', 0, { fillable: true }),
  tool('goo', 'TOXIC GOO', 'DEADLY. CROSS IT WITH PORTALS.', '~', 0, { fillable: true }),
  tool('fizz', 'EMANCIPATION GRID', 'FIZZLES YOUR PORTALS AND ANY CUBE THAT TOUCHES IT.', '|', 0, { fillable: true }),
  tool('erase', 'ERASER', 'CLEARS A TILE. RIGHT CLICK ALSO ERASES.', ' ', 0, { fillable: true }),

  tool('bluegel', 'BLUE GEL', 'BOUNCY. JUMP ONTO IT TO FLY HIGH.', 'J', 1, { fillable: true }),
  tool('speedgel', 'ORANGE GEL', 'RUN FAST ACROSS IT TO JUMP HUGE GAPS.', 'G', 1, { fillable: true }),
  tool('plateR', 'LAUNCH PLATE RIGHT', 'THROWS YOU UP AND TO THE RIGHT.', 'Q', 1),
  tool('plateL', 'LAUNCH PLATE LEFT', 'THROWS YOU UP AND TO THE LEFT.', 'q', 1),

  tool('start', 'START', 'WHERE THE PLAYER BEGINS. ONLY ONE.', 'P', 2, { unique: true }),
  tool('exit', 'EXIT', 'REACH THIS TO WIN. ONLY ONE.', 'E', 2, { unique: true }),
  tool('cube', 'CUBE', 'PICK UP WITH E. WEIGHS DOWN BUTTONS.', 'C', 2),
  tool('mirrorR', 'MIRROR CUBE (/)', 'BENDS A LASER BEAM.', 'M', 2),
  tool('mirrorL', 'MIRROR CUBE (BACKSLASH)', 'BENDS A LASER BEAM THE OTHER WAY.', 'N', 2),

  tool('button', 'FLOOR BUTTON', 'OPENS DOORS WITH THE SAME NUMBER WHILE PRESSED.', '1', 3, { wire: 0 }),
  tool('timed', 'TIMED BUTTON', 'HOLDS DOORS OPEN FOR A FEW SECONDS.', 'T', 3, { wire: 1 }),
  tool('door', 'DOOR', 'OPENS WHEN ITS NUMBER IS TRIGGERED.', 'a', 3, { wire: 2, fillable: true }),
  tool('target', 'LASER TARGET', 'OPENS DOORS WITH THE SAME NUMBER WHEN HIT BY A LASER.', '4', 3, { wire: 3 }),

  tool('laserR', 'LASER RIGHT', 'SHOOTS A LASER TO THE RIGHT.', '>', 4),
  tool('laserL', 'LASER LEFT', 'SHOOTS A LASER TO THE LEFT.', '<', 4),
  tool('laserU', 'LASER UP', 'SHOOTS A LASER UP.', '^', 4),
  tool('laserD', 'LASER DOWN', 'SHOOTS A LASER DOWN.', 'v', 4),
];
const byId = (id) => TOOLS.find((t) => t.id === id);

export const GROUP_COL = [C(255, 99, 99), C(255, 214, 90), C(94, 224, 138), C(90, 200, 255), C(190, 130, 255), C(255, 150, 205)];

const MAP_AREA = R(244, 64, K.ScreenW - 244, K.ScreenH - 64 - 34);
const wallish = (c) => c === '#' || c === 'X' || c === '=' || c === '~';

export class MapEditor {
  constructor(map, g) {
    this.g = g;
    this.map = map;
    this.grid = map.map.map((r) => r.split(''));
    this.world = null;
    this.worldDirty = true;
    this.tool = TOOLS[0];
    this.brush = 'draw';
    this.group = 0;
    this.undo = []; this.redo = [];
    this.stroking = false; this.strokeBtn = 0; this.strokeTool = TOOLS[0];
    this.strokeStart = V(0, 0); this.strokeLast = V(0, 0); this.hover = V(-1, -1);
    this.strokeSnap = null; this.boxFilled = false;
    this.pan = V(0, 0); this.lastMouse = V(0, 0); this.panning = false;
    this.wantExit = false; this.wantTest = false; this.showHelp = false;
    this.showSettings = false; this.naming = false; this.modalSkip = 0; this.nameBuf = '';
    this.toast = ''; this.toastT = 0; this.savedT = 0;
    this.persist = true;
    this.issues = [];
    this.hasStart = false; this.hasExit = false;
    this.drawingModal = false;
    // start at the bottom-left, where the player usually begins (small maps simply centre)
    this.pan = V(MAP_AREA.w / 2, this.H * T - MAP_AREA.h / 2);
    this.clampPan();
    this.rebuildWorld();
  }

  get W() { return this.grid[0].length; }
  get H() { return this.grid.length; }
  get ready() { return this.hasStart && this.hasExit; }
  rows() { return this.grid.map((r) => r.join('')); }

  rebuildWorld() {
    this.world = new World({ name: 'Editor', map: this.rows(), gun: this.map.gun, theme: this.map.theme, blueDir: V(0, 0), orangeDir: V(0, 0), hints: [], fling: false, label: '' });
    this.world.computeLasers();
    this.worldDirty = false;
    this.computeIssues();
  }

  // ---------------------------------------------------------------- validation

  computeIssues() {
    this.issues = [];
    this.hasStart = this.hasExit = false;
    const doors = Array(6).fill(false), triggers = Array(6).fill(false), targets = Array(6).fill(false);
    let emitter = false;
    for (const row of this.grid) for (const c of row) {
      if (c === 'P') this.hasStart = true;
      else if (c === 'E') this.hasExit = true;
      else if (World.isDoor(c)) doors[c.charCodeAt(0) - 97] = true;
      else if (World.buttonGroup(c) >= 0) triggers[World.buttonGroup(c)] = true;
      else if (World.receiverGroup(c) >= 0) { triggers[World.receiverGroup(c)] = true; targets[World.receiverGroup(c)] = true; }
      else if ('<>^v'.includes(c)) emitter = true;
    }
    if (!this.hasStart) this.issues.push(['PLACE A START - THE PLAYER NEEDS SOMEWHERE TO BEGIN', true]);
    if (!this.hasExit) this.issues.push(['PLACE AN EXIT - THE PLAYER NEEDS SOMEWHERE TO GO', true]);
    for (let i = 0; i < 6; i++) {
      if (doors[i] && !triggers[i]) this.issues.push([`DOOR ${i + 1} HAS NO BUTTON OR LASER TARGET, SO IT STAYS SHUT`, false]);
      if (!doors[i] && triggers[i]) this.issues.push([`NUMBER ${i + 1} HAS A BUTTON OR TARGET BUT NO DOOR`, false]);
      if (targets[i] && !emitter) this.issues.push([`LASER TARGET ${i + 1} NEEDS A LASER`, false]);
    }
  }

  // ---------------------------------------------------------------- editing

  pushUndo(snap) {
    this.undo.push(snap);
    if (this.undo.length > 120) this.undo.shift();
    this.redo.length = 0;
  }

  restore(rows) {
    this.grid = rows.map((r) => r.split(''));
    this.changed();
    this.clampPan();
  }

  /** The map content changed: it needs testing again, and it is saved. */
  changed() {
    this.worldDirty = true;
    this.map.verified = false;
    this.map.best = 0;
    this.save();
  }

  save() {
    this.map.map = this.rows();
    if (!this.persist) return;
    if (CustomMaps.save(this.map)) this.savedT = 1.6;
    else this.showToast('COULD NOT SAVE - IS BROWSER STORAGE FULL OR BLOCKED?');
  }

  /** Back from a test play: say so if the map was just beaten. */
  afterTest() {
    this.stroking = false;
    if (this.map.verified) this.showToast("YOU BEAT YOUR MAP! IT'S READY TO SHARE");
  }

  showToast(s) { this.toast = s; this.toastT = 3; }

  doUndo() {
    if (this.undo.length === 0) { this.showToast('NOTHING TO UNDO'); return; }
    this.redo.push(this.rows());
    this.restore(this.undo.pop());
  }

  doRedo() {
    if (this.redo.length === 0) { this.showToast('NOTHING TO REDO'); return; }
    this.undo.push(this.rows());
    this.restore(this.redo.pop());
  }

  charFor(t, grp) {
    switch (t.wire) {
      case 0: return World.buttonChar(grp);
      case 1: return World.timedButtonChar(grp);
      case 2: return World.doorChar(grp);
      case 3: return World.receiverChar(grp);
      default: return t.ch;
    }
  }

  inMap(x, y) { return x >= 0 && y >= 0 && x < this.W && y < this.H; }

  put(x, y, t) {
    if (!this.inMap(x, y)) return;
    if (t.unique) for (const row of this.grid) for (let i = 0; i < row.length; i++) if (row[i] === t.ch) row[i] = ' ';
    this.grid[y][x] = this.charFor(t, this.group);
  }

  putRect(a, b, t) {
    for (let y = Math.min(a.y, b.y); y <= Math.max(a.y, b.y); y++) for (let x = Math.min(a.x, b.x); x <= Math.max(a.x, b.x); x++) this.put(x, y, t);
  }

  putLine(a, b, t) {
    const dx = Math.abs(b.x - a.x), dy = -Math.abs(b.y - a.y), sx = a.x < b.x ? 1 : -1, sy = a.y < b.y ? 1 : -1;
    let err = dx + dy, x = a.x, y = a.y;
    for (let guard = 0; guard < 4096; guard++) {
      this.put(x, y, t);
      if (x === b.x && y === b.y) break;
      const e2 = 2 * err;
      if (e2 >= dy) { err += dy; x += sx; }
      if (e2 <= dx) { err += dx; y += sy; }
    }
  }

  floodFill(sx, sy, t) {
    const target = this.grid[sy][sx], withCh = this.charFor(t, this.group);
    if (target === withCh) return;
    const q = [[sx, sy]];
    this.grid[sy][sx] = withCh;
    while (q.length) {
      const [x, y] = q.shift();
      for (const [nx, ny] of [[x + 1, y], [x - 1, y], [x, y + 1], [x, y - 1]])
        if (this.inMap(nx, ny) && this.grid[ny][nx] === target) { this.grid[ny][nx] = withCh; q.push([nx, ny]); }
    }
  }

  // ---- size, outline, clear

  resize(dw, dh) {
    const nw = clamp(this.W + dw, MIN_W, MAX_W), nh = clamp(this.H + dh, MIN_H, MAX_H);
    if (nw === this.W && nh === this.H) return;
    const snap = this.rows();
    const g = this.grid;
    // columns go in / come out just inside the right wall, rows just above the floor
    while (this.W < nw) for (let y = 0; y < this.H; y++) {
      const src = g[y][this.W - 2];
      g[y].splice(this.W - 1, 0, wallish(src) ? src : ' ');
    }
    while (this.W > nw) for (let y = 0; y < this.H; y++) g[y].splice(this.W - 2, 1);
    while (this.H < nh) {
      const copy = g[this.H - 3].map((c) => (wallish(c) ? c : ' '));
      g.splice(this.H - 2, 0, copy);
    }
    while (this.H > nh) g.splice(this.H - 3, 1);
    this.pushUndo(snap);
    this.changed();
    this.clampPan();
  }

  ring() {
    const snap = this.rows(), g = this.grid, W = this.W, H = this.H;
    for (let x = 0; x < W; x++) { g[0][x] = 'X'; g[H - 1][x] = 'X'; g[H - 2][x] = 'X'; }
    for (let y = 0; y < H; y++) { g[y][0] = 'X'; g[y][W - 1] = 'X'; }
    this.pushUndo(snap);
    this.changed();
    this.showToast('WALLED IN. CTRL+Z TO UNDO');
  }

  clearAll() {
    const snap = this.rows(), g = this.grid, W = this.W, H = this.H;
    for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) g[y][x] = x === 0 || y === 0 || x === W - 1 || y >= H - 2 ? 'X' : ' ';
    g[H - 3][Math.min(4, W - 2)] = 'P';
    g[H - 3][Math.max(W - 5, 1)] = 'E';
    this.pushUndo(snap);
    this.changed();
    this.showToast('MAP CLEARED. CTRL+Z TO UNDO');
  }

  // ---------------------------------------------------------------- view

  /** Where the game camera should look so the map sits inside the editor's map area. */
  get camTarget() {
    return V(this.pan.x - (MAP_AREA.x + MAP_AREA.w / 2 - K.ScreenW / 2), this.pan.y - (MAP_AREA.y + MAP_AREA.h / 2 - K.ScreenH / 2));
  }

  clampPan() {
    const margin = 96, hw = MAP_AREA.w / 2, hh = MAP_AREA.h / 2;
    this.pan.x = this.W * T <= MAP_AREA.w ? this.W * T / 2 : clamp(this.pan.x, hw - margin, this.W * T - hw + margin);
    this.pan.y = this.H * T <= MAP_AREA.h ? this.H * T / 2 : clamp(this.pan.y, hh - margin, this.H * T - hh + margin);
  }

  // ---------------------------------------------------------------- update

  get modal() { return this.showSettings || this.showHelp; }

  update(dt, cam, inputOn) {
    this.toastT = Math.max(0, this.toastT - dt);
    this.savedT = Math.max(0, this.savedT - dt);
    const mouse = UI.mouse;
    const delta = V(mouse.x - this.lastMouse.x, mouse.y - this.lastMouse.y);
    this.lastMouse = V(mouse.x, mouse.y);

    if (!inputOn) { this.stroking = false; return; }

    if (this.naming) this.updateNaming();
    else this.updateKeys();

    // view: arrows, trackpad scroll, middle mouse or space + drag
    if (!this.naming && !this.modal) {
      let mx = 0, my = 0;
      if (Input.down('ArrowLeft')) mx -= 1;
      if (Input.down('ArrowRight')) mx += 1;
      if (Input.down('ArrowUp')) my -= 1;
      if (Input.down('ArrowDown')) my += 1;
      this.pan.x += mx * 700 * dt; this.pan.y += my * 700 * dt;
      if (contains(MAP_AREA, mouse)) { this.pan.x -= Input.wheelX * 48; this.pan.y -= Input.wheelY * 48; }
    }
    const wantPan = !this.modal && !this.stroking && contains(MAP_AREA, mouse)
      && (Input.mouseDown(1) || (Input.down('Space') && Input.mouseDown(0)));
    if (wantPan) { this.pan.x -= delta.x; this.pan.y -= delta.y; }
    this.panning = wantPan;
    this.clampPan();

    // hover cell
    const wp = V((mouse.x * K.Px - cam.offset.x) / cam.zoom + cam.target.x, (mouse.y * K.Px - cam.offset.y) / cam.zoom + cam.target.y);
    const cx = Math.floor(wp.x / T), cy = Math.floor(wp.y / T);
    const inArea = contains(MAP_AREA, mouse) && !this.modal;
    this.hover = inArea && this.inMap(cx, cy) ? V(cx, cy) : V(-1, -1);
    const clamped = V(clamp(cx, 0, this.W - 1), clamp(cy, 0, this.H - 1));

    if (!this.stroking && !this.panning && this.hover.x >= 0 && !this.naming) {
      const lp = Input.mousePressed(0), rp = Input.mousePressed(2);
      if ((lp || rp) && !Input.down('Space')) this.beginStroke(lp ? 0 : 2, this.hover);
    } else if (this.stroking) {
      if (Input.mouseDown(this.strokeBtn) && !this.panning) this.continueStroke(clamped);
      else this.endStroke(clamped);
    }

    if (this.worldDirty) this.rebuildWorld();
  }

  updateNaming() {
    for (const ch of Input.chars) {
      const c = ch.toUpperCase();
      if (this.nameBuf.length < MAX_NAME && (/^[A-Z0-9]$/.test(c) || " -'!.,&".includes(c))) this.nameBuf += c;
    }
    if ((Input.pressed('Backspace') || Input.pressedRepeat('Backspace')) && this.nameBuf.length > 0) this.nameBuf = this.nameBuf.slice(0, -1);
    if (Input.pressed('Enter') || Input.pressed('NumpadEnter')) this.commitName();
    else if (Input.pressed('Escape')) this.naming = false;
  }

  commitName() {
    this.naming = false;
    const n = cleanName(this.nameBuf);
    if (this.nameBuf.trim().length > 0 && n !== this.map.name) {
      this.map.name = n;
      this.save();
    }
  }

  updateKeys() {
    if (Input.pressed('Escape')) {
      if (this.showSettings) this.showSettings = false;
      else if (this.showHelp) this.showHelp = false;
      else if (this.stroking) {
        this.stroking = false;
        if (this.strokeSnap) { this.grid = this.strokeSnap.map((r) => r.split('')); this.worldDirty = true; }
      } else { this.save(); this.wantExit = true; }
      return;
    }
    if (this.modal) return;
    const ctrl = Input.ctrl;
    if (ctrl && Input.pressed('KeyZ')) { if (Input.shift) this.doRedo(); else this.doUndo(); return; }
    if (ctrl && Input.pressed('KeyY')) { this.doRedo(); return; }
    if (ctrl && Input.pressed('KeyS')) { this.save(); this.showToast('SAVED IN THIS BROWSER'); return; }
    if (Input.pressed('F5')) { this.requestTest(); return; }
    if (ctrl) return;
    if (Input.pressed('KeyB')) this.brush = 'draw';
    if (Input.pressed('KeyR')) this.brush = 'box';
    if (Input.pressed('KeyF')) this.brush = 'fill';
    if (Input.pressed('KeyE')) this.tool = byId('erase');
    for (let i = 0; i < 6; i++) if (Input.pressed(`Digit${i + 1}`)) this.group = i;
  }

  requestTest() {
    if (!this.ready) { this.showToast(this.hasStart ? 'ADD AN EXIT FIRST' : 'ADD A START FIRST'); return; }
    this.save();
    this.wantTest = true;
  }

  // ---- strokes

  beginStroke(btn, cell) {
    this.stroking = true;
    this.strokeBtn = btn;
    this.strokeTool = btn === 2 ? byId('erase') : this.tool;
    this.strokeSnap = this.rows();
    this.strokeStart = this.strokeLast = V(cell.x, cell.y);
    this.boxFilled = this.strokeTool.fillable || this.strokeTool.id === 'erase';
    if (this.brush === 'fill' && this.strokeTool.fillable) this.floodFill(cell.x, cell.y, this.strokeTool);
    else if (this.brush === 'box' && this.boxFilled) { /* applied on release */ }
    else this.put(cell.x, cell.y, this.strokeTool);
    this.worldDirty = true;
  }

  continueStroke(cell) {
    if (this.brush === 'draw' || (this.brush === 'box' && !this.boxFilled) || (this.brush === 'fill' && !this.strokeTool.fillable)) {
      if (cell.x !== this.strokeLast.x || cell.y !== this.strokeLast.y) {
        this.putLine(this.strokeLast, cell, this.strokeTool);
        this.strokeLast = V(cell.x, cell.y);
        this.worldDirty = true;
      }
    }
    this.strokeLast = this.brush === 'box' && this.boxFilled ? V(cell.x, cell.y) : this.strokeLast;
  }

  endStroke(cell) {
    this.stroking = false;
    if (this.brush === 'box' && this.boxFilled) this.putRect(this.strokeStart, cell, this.strokeTool);
    const snap = this.strokeSnap, now = this.rows();
    if (now.some((r, i) => r !== snap[i]) || now.length !== snap.length) {
      this.pushUndo(snap);
      this.changed();
    }
    this.worldDirty = true;
  }

  // ---------------------------------------------------------------- drawing: in the world

  drawOverlay() {
    const g = this.g, W = this.W, H = this.H;
    const gridCol = C(255, 255, 255, 16);
    for (let x = 0; x <= W; x++) g.rect(x * T - 1, 0, 2, H * T, gridCol);
    for (let y = 0; y <= H; y++) g.rect(0, y * T - 1, W * T, 2, gridCol);
    g.rectLines(R(-2, -2, W * T + 4, H * T + 4), 4, withAlpha(PP.BlueRamp[2], 0.5));

    // wiring: a faint line from each button / laser target to the doors with the same number
    for (let gi = 0; gi < 6; gi++) {
      const doors = [], trig = [];
      for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) {
        const c = this.grid[y][x], ctr = V(x * T + T / 2, y * T + T / 2);
        if (c === World.doorChar(gi)) doors.push(ctr);
        else if (World.buttonGroup(c) === gi || World.receiverGroup(c) === gi) trig.push(ctr);
      }
      const lit = this.tool.wire >= 0 && this.group === gi;
      for (const a of trig) {
        if (doors.length === 0) continue;
        let d = doors[0];
        for (const v of doors) if (vdist2(a, v) < vdist2(a, d)) d = v;
        g.line(a, d, lit ? 4 : 2, withAlpha(GROUP_COL[gi], lit ? 0.75 : 0.28));
      }
    }
    // number badges on every wired tile
    for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) {
      const c = this.grid[y][x];
      const gi = World.isDoor(c) ? c.charCodeAt(0) - 97 : Math.max(World.buttonGroup(c), World.receiverGroup(c));
      if (gi < 0) continue;
      const r = R(x * T + 3, y * T + 3, 18, 20);
      g.rectR(r, C(10, 12, 16, 220));
      g.rect(r.x + 2, r.y + 2, r.w - 4, r.h - 4, GROUP_COL[gi]);
      UI.text(String(gi + 1), V(r.x + 4, r.y + 3), 16, PP.Ink);
    }

    // start label (the game already draws its own EXIT sign)
    for (let y = 0; y < H; y++) for (let x = 0; x < W; x++) if (this.grid[y][x] === 'P') this.tag('START', x, y, PP.BlueRamp[2]);

    // brush preview
    if (this.stroking && this.brush === 'box' && this.boxFilled) {
      const a = this.strokeStart, b = this.strokeLast;
      const r = R(Math.min(a.x, b.x) * T, Math.min(a.y, b.y) * T, (Math.abs(a.x - b.x) + 1) * T, (Math.abs(a.y - b.y) + 1) * T);
      const er = this.strokeTool.id === 'erase';
      g.rectR(r, withAlpha(er ? C(255, 90, 80) : PP.OrangeRamp[2], 0.28));
      g.rectLines(r, 4, er ? C(255, 90, 80) : PP.OrangeRamp[2]);
    } else if (this.hover.x >= 0) {
      const cell = R(this.hover.x * T, this.hover.y * T, T, T);
      const er = this.tool.id === 'erase';
      if (!er && !(this.brush === 'fill' && this.tool.fillable)) toolIcon(g, this.tool, this.group, cell, 0.6);
      g.rectLines(cell, 4, withAlpha(er ? C(255, 90, 80) : WHITE, 0.9));
    }
  }

  tag(s, x, y, c) {
    const m = UI.measure(s, 16);
    const pos = V(x * T + T / 2 - m.x / 2, y * T - 22);
    this.g.rect(Math.trunc(pos.x) - 4, Math.trunc(pos.y) - 2, Math.trunc(m.x) + 8, 18, C(10, 12, 16, 210));
    UI.text(s, pos, 16, c);
  }

  // ---------------------------------------------------------------- drawing: the interface

  get live() { return !this.modal || this.drawingModal; }

  chip(r, label, selected, { accent = null, enabled = true, textCol = null } = {}) {
    const g = this.g;
    const acc = accent || PP.BlueRamp[2];
    const over = enabled && this.live && contains(r, UI.mouse);
    const fill = selected ? acc : over ? C(46, 52, 66) : C(28, 31, 40);
    const edge = selected ? withAlpha(WHITE, 0.85) : over ? acc : C(58, 65, 80);
    g.rect(r.x + 2, r.y + 3, r.w, r.h, C(0, 0, 0, 100));
    g.rectR(r, fill);
    g.rectLines(r, 2, edge);
    const tc = textCol || (!enabled ? C(90, 96, 108) : selected ? PP.Ink : PP.Cream);
    UI.text(label, V(r.x + r.w / 2, r.y + r.h / 2), 16, tc, Align.Center);
    if (over && UI.click) { UI.onClick?.(); return true; }
    return false;
  }

  drawUi() {
    let hoverHelp = null;
    this.drawTopBar();
    hoverHelp = this.drawPalette(hoverHelp);
    this.drawStatusStrip(hoverHelp);

    if (this.toastT > 0) {
      const a = clamp01(this.toastT * 2), m = UI.measure(this.toast, 16);
      const r = R(MAP_AREA.x + MAP_AREA.w / 2 - m.x / 2 - 24, MAP_AREA.y + MAP_AREA.h - 64, m.x + 48, 44);
      UI.panel(r, a, PP.OrangeRamp[2]);
      UI.text(this.toast, V(r.x + r.w / 2, r.y + r.h / 2), 16, withAlpha(PP.CreamHi, a), Align.Center);
    }
    if (this.showSettings) this.drawSettings();
    if (this.showHelp) this.drawHelp();
  }

  drawTopBar() {
    const g = this.g;
    g.rect(0, 0, K.ScreenW, 62, C(14, 16, 22, 245));
    g.rect(0, 62, K.ScreenW, 2, PANEL_EDGE);

    if (this.chip(R(14, 10, 112, 44), '< BACK', false)) { this.save(); this.wantExit = true; }

    // name field
    const nr = R(140, 10, 330, 44);
    const over = this.live && contains(nr, UI.mouse);
    if (!this.naming && over && UI.click) { this.naming = true; this.nameBuf = this.map.name; }
    else if (this.naming && UI.click && !over) this.commitName();
    g.rectR(nr, this.naming ? C(36, 42, 56) : C(24, 27, 36));
    g.rectLines(nr, 2, this.naming ? PP.OrangeRamp[2] : over ? PP.BlueRamp[2] : PANEL_EDGE);
    let shown = this.naming ? this.nameBuf : this.map.name;
    if (this.naming && Math.trunc(UI.time * 2) % 2 === 0) shown += '_';
    else if (this.naming) shown += ' ';
    UI.text(shown, V(nr.x + 14, nr.y + 15), 16, PP.CreamHi);
    if (!this.naming) UI.text('RENAME', V(nr.x + nr.w - 12, nr.y + 15), 16, C(110, 118, 132), Align.Right);

    let x = 484;
    if (this.chip(R(x, 10, 150, 44), 'MAP OPTIONS', this.showSettings, { accent: PP.BlueRamp[2] })) { this.showSettings = !this.showSettings; this.modalSkip = 1; }
    x += 164;
    if (this.chip(R(x, 10, 64, 44), 'UNDO', false, { enabled: this.undo.length > 0 })) this.doUndo();
    x += 72;
    if (this.chip(R(x, 10, 64, 44), 'REDO', false, { enabled: this.redo.length > 0 })) this.doRedo();
    x += 72;
    if (this.chip(R(x, 10, 44, 44), '?', this.showHelp, { accent: PP.OrangeRamp[2] })) this.showHelp = !this.showHelp;

    const tr = R(K.ScreenW - 14 - 200, 10, 200, 44);
    if (this.ready) { if (this.chip(tr, 'TEST PLAY (F5)', true, { accent: PP.Exit })) this.requestTest(); }
    else if (this.chip(tr, 'TEST PLAY', false, { textCol: C(110, 116, 128) })) this.requestTest();

    // status next to the play button
    let st, sc;
    if (this.savedT > 0) { st = 'SAVED'; sc = PP.Exit; }
    else if (!this.ready) { st = 'NOT READY'; sc = C(255, 110, 100); }
    else if (this.map.verified) { st = 'BEATEN - READY TO SHARE'; sc = PP.Exit; }
    else { st = 'NOT TESTED YET'; sc = PP.OrangeRamp[2]; }
    UI.text(st, V(tr.x - 16, 24), 16, sc, Align.Right);
  }

  drawPalette(hoverHelp) {
    const g = this.g;
    const panel = R(0, 64, 238, K.ScreenH - 64 - 34);
    g.rectR(panel, C(14, 16, 22, 245));
    g.rect(236, 64, 2, panel.h, PANEL_EDGE);

    let y = 76;
    UI.text('BRUSH', V(14, y), 16, C(130, 138, 152));
    y += 22;
    const brushes = [
      ['draw', 'DRAW', 'PAINT TILE BY TILE. HOLD AND DRAG. (B)'],
      ['box', 'BOX', 'DRAG OUT A RECTANGLE OF TILES. (R)'],
      ['fill', 'FILL', 'FLOOD-FILL AN AREA OF THE SAME TILE. (F)'],
    ];
    for (let i = 0; i < 3; i++) {
      const r = R(14 + i * 70, y, 66, 34);
      if (this.chip(r, brushes[i][1], this.brush === brushes[i][0], { accent: PP.OrangeRamp[2] })) this.brush = brushes[i][0];
      if (this.live && contains(r, UI.mouse)) hoverHelp = brushes[i][2];
    }
    y += 44;

    for (let s = 0; s < SECTIONS.length; s++) {
      UI.text(SECTIONS[s], V(14, y), 16, C(130, 138, 152));
      y += 20;
      const inSec = TOOLS.filter((t) => t.section === s);
      for (let i = 0; i < inSec.length; i++) {
        const t = inSec[i];
        const r = R(14 + (i % 4) * 54, y + Math.floor(i / 4) * 52, 46, 46);
        const sel = this.tool === t;
        const over = this.live && contains(r, UI.mouse);
        g.rect(r.x + 2, r.y + 3, 46, 46, C(0, 0, 0, 100));
        g.rectR(r, sel ? C(60, 50, 40) : over ? C(46, 52, 66) : C(28, 31, 40));
        g.rectLines(r, 2, sel ? PP.OrangeRamp[2] : over ? PP.BlueRamp[2] : C(58, 65, 80));
        toolIcon(g, t, this.group, R(r.x + 5, r.y + 5, 36, 36), 1);
        if (over) {
          hoverHelp = `${t.name}: ${t.help}`;
          if (UI.click) { UI.onClick?.(); this.tool = t; }
        }
      }
      y += Math.floor((inSec.length + 3) / 4) * 52;
      if (s === 3) {
        // the number that buttons, doors and laser targets share
        const active = this.tool.wire >= 0;
        for (let i = 0; i < 6; i++) {
          const r = R(14 + i * 35, y, 31, 31);
          const sel = this.group === i, col = GROUP_COL[i];
          g.rectR(r, withAlpha(col, active ? (sel ? 1 : 0.45) : 0.22));
          g.rectLines(r, sel ? 4 : 2, sel && active ? WHITE : C(10, 12, 16));
          UI.text(String(i + 1), V(r.x + 11, r.y + 9), 16, active ? PP.Ink : C(120, 124, 134));
          if (this.live && contains(r, UI.mouse)) {
            hoverHelp = `NUMBER ${i + 1}: BUTTONS, TARGETS AND DOORS WITH THE SAME NUMBER ARE WIRED TOGETHER. (KEY ${i + 1})`;
            if (UI.click) { UI.onClick?.(); this.group = i; if (!active) this.tool = byId('button'); }
          }
        }
        y += 40;
      } else y += 2;
    }
    return hoverHelp;
  }

  drawStatusStrip(hoverHelp) {
    const g = this.g;
    const r = R(0, K.ScreenH - 34, K.ScreenW, 34);
    g.rectR(r, C(14, 16, 22, 250));
    g.rect(0, r.y, K.ScreenW, 2, PANEL_EDGE);
    let text, col = C(150, 158, 172);
    const blocking = this.issues.filter((i) => i[1]);
    if (hoverHelp != null) text = hoverHelp;
    else if (this.issues.length > 0) {
      const first = blocking.length > 0 ? blocking[0] : this.issues[0];
      text = first[0] + (this.issues.length > 1 ? `  (+${this.issues.length - 1} MORE)` : '');
      col = first[1] ? C(255, 130, 120) : PP.Hazard;
    } else text = 'LEFT CLICK PLACES   RIGHT CLICK ERASES   SPACE + DRAG MOVES AROUND   CTRL+Z UNDOES';
    UI.text(text, V(16, r.y + 11), 16, col);
    const size = this.hover.x >= 0 ? `${this.hover.x + 1},${this.hover.y + 1}   ${this.W}X${this.H}` : `${this.W}X${this.H}`;
    UI.text(size, V(K.ScreenW - 16, r.y + 11), 16, C(110, 118, 132), Align.Right);
  }

  drawBackdrop() { this.g.rect(0, 0, K.ScreenW, K.ScreenH, C(6, 8, 12, 170)); }

  drawSettings() {
    this.drawBackdrop();
    this.drawingModal = true;
    const p = R(K.ScreenW / 2 - 290, 110, 580, 480);
    UI.panel(p, 1, PP.BlueRamp[2]);
    UI.textShadow('MAP OPTIONS', V(p.x + 40, p.y + 28), 30, PP.CreamHi);
    const x = p.x + 40, w = p.w - 80;
    let y = p.y + 84;

    const stepper = (label, value, change) => {
      UI.text(label, V(x, y + 15), 16, C(150, 158, 172));
      const sx = x + w - (4 * 52 + 80 + 4 * 6);
      if (this.chip(R(sx, y, 52, 44), '-5', false)) change(-5);
      if (this.chip(R(sx + 58, y, 52, 44), '-1', false)) change(-1);
      UI.text(String(value), V(sx + 116 + 40, y + 22), 16, PP.CreamHi, Align.Center);
      if (this.chip(R(sx + 116 + 86, y, 52, 44), '+1', false)) change(1);
      if (this.chip(R(sx + 116 + 144, y, 52, 44), '+5', false)) change(5);
      y += 58;
    };
    stepper('WIDTH (TILES)', this.W, (d) => this.resize(d, 0));
    stepper('HEIGHT (TILES)', this.H, (d) => this.resize(0, d));

    UI.text('LOOK', V(x, y + 15), 16, C(150, 158, 172));
    const lx = x + w - 230;
    if (this.chip(R(lx, y, 52, 44), '<', false)) { this.map.theme = (this.map.theme + THEME_COUNT - 1) % THEME_COUNT; this.changed(); }
    UI.text(`STYLE ${this.map.theme + 1}`, V(lx + 115, y + 22), 16, PP.CreamHi, Align.Center);
    if (this.chip(R(lx + 178, y, 52, 44), '>', false)) { this.map.theme = (this.map.theme + 1) % THEME_COUNT; this.changed(); }
    y += 58;

    UI.text('PORTAL GUN', V(x, y), 16, C(150, 158, 172));
    y += 26;
    const guns = [[0, 'NO GUN'], [1, 'BLUE ONLY'], [2, 'BOTH PORTALS']];
    for (let i = 0; i < 3; i++)
      if (this.chip(R(x + i * (w / 3 + 2), y, w / 3 - 6, 44), guns[i][1], this.map.gun === guns[i][0], { accent: PP.OrangeRamp[2] })) {
        this.map.gun = guns[i][0];
        this.changed();
      }
    y += 62;

    if (this.chip(R(x, y, w / 2 - 6, 44), 'WALL IT IN', false)) this.ring();
    if (this.chip(R(x + w / 2 + 6, y, w / 2 - 6, 44), 'CLEAR MAP', false, { accent: C(255, 110, 100) })) this.clearAll();
    y += 62;
    if (this.chip(R(x, y, w, 48), 'DONE', true, { accent: PP.Exit })) this.showSettings = false;
    this.drawingModal = false;
    if (this.modalSkip > 0) this.modalSkip--;
    else if (UI.click && !contains(p, UI.mouse)) this.showSettings = false;
  }

  drawHelp() {
    this.drawBackdrop();
    this.drawingModal = true;
    const p = R(K.ScreenW / 2 - 440, 50, 880, 620);
    UI.panel(p, 1, PP.OrangeRamp[2]);
    UI.textShadow('BUILD YOUR OWN CHAMBER', V(p.x + 40, p.y + 28), 30, PP.CreamHi);
    const steps = [
      '1  PICK A TOOL ON THE LEFT, THEN CLICK THE MAP TO PLACE IT.',
      '2  RIGHT CLICK ERASES. HOLD AND DRAG TO PAINT. TRY BOX AND FILL.',
      '3  EVERY MAP NEEDS A START AND AN EXIT.',
      '4  DOORS: PICK A NUMBER, THEN PLACE A BUTTON AND A DOOR\n    WITH THE SAME NUMBER. COLOURED BADGES SHOW THE LINKS.',
      '5  PRESS TEST PLAY (F5) AND BEAT YOUR OWN MAP TO MARK IT BEATEN.',
      '6  EVERYTHING SAVES BY ITSELF. FIND YOUR MAPS IN MAP BUILDER.',
    ];
    let y = p.y + 84;
    for (const st of steps) {
      for (const line of st.split('\n')) { UI.text(line, V(p.x + 40, y), 16, PP.Cream); y += 26; }
      y += 12;
    }
    y += 8;
    UI.text('SHORTCUTS', V(p.x + 40, y), 16, C(130, 138, 152));
    y += 28;
    const keys = [['CTRL+Z', 'UNDO'], ['CTRL+Y', 'REDO'], ['CTRL+S', 'SAVE'], ['SPACE+DRAG', 'MOVE AROUND'],
      ['B R F', 'DRAW, BOX, FILL'], ['E', 'ERASER'], ['1-6', 'WIRING NUMBER'], ['ESC', 'BACK TO MY MAPS']];
    for (let i = 0; i < keys.length; i++) {
      const cx = p.x + 40 + (i % 2) * 340, cy = y + Math.floor(i / 2) * 36;
      UI.keyCap(keys[i][0], V(cx, cy));
      UI.text(keys[i][1], V(cx + UI.keyCapWidth(keys[i][0]) + 12, cy + 9), 16, PP.Cream);
    }
    if (this.chip(R(p.x + p.w - 40 - 220, p.y + p.h - 72, 220, 48), "LET'S BUILD", true, { accent: PP.Exit })) this.showHelp = false;
    this.drawingModal = false;
  }

  /** Fills the map with a wired-up example. Only used for the --demo query string. */
  demo() {
    this.group = 0;
    this.putRect(V(12, 10), V(14, 10), byId('panel'));
    this.put(6, 15, byId('button')); this.put(16, 15, byId('door')); this.put(16, 14, byId('door')); this.put(16, 13, byId('door'));
    this.group = 1; this.put(20, 8, byId('laserR')); this.put(26, 12, byId('target')); this.put(24, 15, byId('door')); this.put(24, 14, byId('door'));
    this.put(9, 15, byId('cube')); this.put(19, 15, byId('goo')); this.put(21, 15, byId('bluegel'));
    this.group = 1; this.tool = byId('door');
    this.worldDirty = true;
  }
}

/** Pixel pictures of every tool, drawn on a 16x16 grid so the palette and the map ghost agree. */
function toolIcon(g, t, group, r, a) {
  const id = t.id, u = r.w / 16;
  const P = (x, y, w, h, c) => {
    const x0 = roundEven(r.x + x * u), y0 = roundEven(r.y + y * u), x1 = roundEven(r.x + (x + w) * u), y1 = roundEven(r.y + (y + h) * u);
    g.rect(x0, y0, Math.max(1, x1 - x0), Math.max(1, y1 - y0), withAlpha(c, c.a / 255 * a));
  };
  const mirror = (x, y, w, h, c, flip) => P(flip ? 16 - x - w : x, y, w, h, c);
  const ink = C(16, 17, 22);
  switch (id) {
    case 'panel':
      P(0, 0, 16, 16, PP.Cream); P(0, 0, 16, 1, PP.CreamHi); P(0, 0, 1, 16, PP.CreamHi);
      P(0, 15, 16, 1, PP.CreamSeam); P(15, 0, 1, 16, PP.CreamSeam); P(7, 1, 2, 14, PP.CreamNoise); P(1, 7, 14, 2, PP.CreamNoise);
      break;
    case 'metal':
      P(0, 0, 16, 16, PP.Metal); P(0, 0, 16, 1, PP.MetalHi); P(0, 15, 16, 1, PP.MetalLo); P(7, 1, 2, 14, PP.MetalSeam); P(1, 7, 14, 2, PP.MetalSeam);
      for (const [x, y] of [[2, 2], [12, 2], [2, 12], [12, 12]]) P(x, y, 2, 2, PP.MetalHi);
      break;
    case 'glass':
      P(0, 0, 16, 16, C(143, 211, 255, 90)); P(0, 0, 16, 1, PP.Fizz); P(0, 15, 16, 1, PP.Fizz); P(0, 0, 1, 16, PP.Fizz); P(15, 0, 1, 16, PP.Fizz);
      for (let i = 0; i < 4; i++) P(3 + i * 2, 10 - i * 2, 2, 2, C(255, 255, 255, 190));
      break;
    case 'goo':
      P(0, 5, 16, 11, PP.Goo2); P(0, 4, 16, 2, PP.Goo1); P(0, 3, 4, 1, PP.GooTop); P(6, 3, 5, 1, PP.GooTop); P(12, 4, 4, 1, PP.GooTop);
      P(4, 9, 2, 2, PP.GooTop); P(10, 12, 2, 2, PP.Goo1); P(0, 13, 16, 3, PP.Goo3);
      break;
    case 'fizz':
      P(3, 0, 10, 3, PP.MetalMid); P(3, 13, 10, 3, PP.MetalMid); P(3, 0, 10, 1, PP.MetalHi);
      for (const x of [4, 7, 10]) P(x, 3, 2, 10, withAlpha(PP.Fizz, 0.85));
      break;
    case 'erase':
      P(2, 5, 12, 8, C(90, 100, 120)); P(7, 5, 7, 8, C(240, 150, 160)); P(2, 5, 12, 1, C(150, 160, 185)); P(2, 13, 12, 1, C(40, 46, 60));
      break;
    case 'bluegel':
    case 'speedgel': {
      const ramp = id === 'bluegel' ? PP.BlueRamp : PP.OrangeRamp;
      P(0, 0, 16, 16, ramp[1]); P(0, 0, 16, 3, ramp[2]); P(0, 13, 16, 3, ramp[0]); P(2, 4, 4, 2, ramp[3]); P(10, 9, 3, 2, ramp[3]);
      if (id === 'bluegel') { P(7, 5, 2, 2, ramp[4]); P(5, 7, 2, 2, ramp[4]); P(9, 7, 2, 2, ramp[4]); }
      else { P(5, 6, 2, 2, ramp[4]); P(7, 8, 2, 2, ramp[4]); P(5, 10, 2, 2, ramp[4]); P(9, 6, 2, 2, ramp[4]); P(11, 8, 2, 2, ramp[4]); P(9, 10, 2, 2, ramp[4]); }
      break;
    }
    case 'plateR':
    case 'plateL': {
      const left = id === 'plateL';
      P(0, 0, 16, 16, PP.MetalLo); P(0, 0, 16, 1, PP.MetalHi);
      for (let i = 0; i < 7; i++) mirror(3 + i, 12 - i, 3, 2, PP.OrangeRamp[2], left);
      mirror(8, 3, 6, 2, PP.OrangeRamp[2], left); mirror(12, 3, 2, 6, PP.OrangeRamp[2], left);
      break;
    }
    case 'start':
      g.tex(Sprites.playerTex, 0, 0, 16, 16, r.x, r.y, r.w, r.h, a);
      break;
    case 'exit':
      P(3, 0, 10, 16, PP.Exit); P(5, 2, 6, 14, C(18, 52, 36)); P(9, 9, 1, 2, PP.Exit); P(3, 0, 10, 1, withAlpha(WHITE, 0.6));
      break;
    case 'cube':
    case 'mirrorR':
    case 'mirrorL':
      P(1, 1, 14, 14, PP.MetalMid); P(1, 1, 14, 1, PP.MetalHi); P(1, 1, 1, 14, PP.MetalHi); P(14, 1, 1, 14, PP.MetalLo); P(1, 14, 14, 1, PP.MetalLo);
      P(4, 4, 8, 8, PP.MetalLo); P(5, 5, 6, 6, C(108, 116, 140));
      if (id !== 'cube') for (let i = 0; i < 9; i++) P(id === 'mirrorR' ? 3 + i : 12 - i, 11 - i, 2, 2, C(200, 240, 255));
      break;
    case 'button':
    case 'timed': {
      const timed = id === 'timed';
      P(1, 12, 14, 4, C(43, 47, 58)); P(1, 12, 14, 1, C(74, 81, 99));
      P(3, 8, 10, 4, timed ? C(230, 170, 40) : C(217, 69, 58)); P(4, 8, 6, 1, timed ? C(255, 230, 150) : C(255, 160, 140));
      if (timed) { P(2, 3, 12, 3, C(24, 27, 34)); P(3, 4, 7, 1, C(255, 214, 90)); }
      break;
    }
    case 'door':
      P(2, 0, 12, 16, C(22, 24, 30)); P(4, 0, 8, 16, C(93, 102, 124)); P(5, 0, 1, 16, C(130, 139, 160));
      P(4, 4, 8, 1, C(70, 76, 92)); P(4, 8, 8, 1, C(70, 76, 92));
      for (let i = 0; i < 4; i++) P(4 + i * 2, 12, 2, 4, i % 2 === 0 ? PP.Hazard : C(30, 30, 34));
      break;
    case 'target':
      P(1, 1, 14, 14, PP.Metal); P(1, 1, 14, 1, PP.MetalHi); P(1, 14, 14, 1, PP.MetalLo);
      P(4, 4, 8, 8, C(60, 20, 20)); P(5, 5, 6, 6, C(150, 40, 40)); P(6, 6, 4, 4, PP.Laser); P(7, 7, 2, 2, PP.LaserCore);
      break;
    case 'laserR': case 'laserL': case 'laserU': case 'laserD':
      P(1, 1, 14, 14, PP.Metal); P(1, 1, 14, 1, PP.MetalHi); P(1, 14, 14, 1, PP.MetalLo); P(3, 3, 10, 10, PP.MetalMid);
      switch (id) {
        case 'laserR': P(10, 6, 6, 4, PP.Laser); P(14, 7, 2, 2, PP.LaserCore); break;
        case 'laserL': P(0, 6, 6, 4, PP.Laser); P(0, 7, 2, 2, PP.LaserCore); break;
        case 'laserU': P(6, 0, 4, 6, PP.Laser); P(7, 0, 2, 2, PP.LaserCore); break;
        default: P(6, 10, 4, 6, PP.Laser); P(7, 14, 2, 2, PP.LaserCore); break;
      }
      break;
  }
  if (t.wire >= 0 && group >= 0 && group < 6) {
    P(10, 0, 6, 6, ink); P(11, 1, 4, 4, GROUP_COL[group]);
  }
}
