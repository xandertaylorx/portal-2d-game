// The game shell: screens, menus, HUD, level flow. Port of Game/App.cs (minus the desktop-only updater and quit).
import {
  K, C, withAlpha, clamp, clamp01, approach, lerp, smoothStep, easeOutCubic, contains, V, R, roundEven, WHITE,
} from './core.js';
import { World, makeDef, emptyInput, GunMode } from './world.js';
import { PP, Sprites, COLORS, HATS, FACE_NAMES, OUTFIT_NAMES, DEFAULT_LOOK, cleanLook, PLAYER_H } from './art.js';
import { Gfx, PixelFont } from './gfx.js';
import { Renderer } from './render.js';
import { Audio } from './audio.js';
import { UI, Align, Icons } from './ui.js';
import { Input } from './input.js';
import { SaveData, CustomMap, CustomMaps } from './maps.js';
import { MapEditor } from './editor.js';
import { MapLibrary } from './library.js';
import { pickRandomTest } from './random.js';

export const VERSION = '2.13.1';
const DIM = C(150, 158, 172);
const PANEL_EDGE_C = C(58, 65, 80);

export class App {
  constructor(canvas, data) {
    this.canvas = canvas;
    this.g = new Gfx(canvas);
    UI.init(this.g);
    PixelFont.load();
    Sprites.load();

    this.chambers = data.levels.chambers.map(makeDef);
    this.menuDef = makeDef(data.levels.menu);
    this.randomPools = null;          // loaded on first use
    this.save = SaveData.load(this.chambers.map((l) => l.recordKey));
    Sprites.setLook(this.save.look);
    this.audio = new Audio();
    this.audio.master = this.save.master; this.audio.sfxVolume = this.save.sfx; this.audio.ambienceVolume = this.save.ambience;
    UI.onHover = () => this.audio.hover();
    UI.onClick = () => this.audio.click();
    Input.onGesture = () => this.audio.unlock();

    this.renderer = new Renderer(this.g);
    this.renderer.windowsEnabled = this.save.cones;
    this.library = new MapLibrary(this.g);
    this.library.reload();

    this.screen = 'menu';
    this.menuWorld = new World(this.menuDef);
    this.world = null;
    this.level = 0;
    this.randomMode = false; this.customMode = false; this.fromEditor = false;
    this.randomDef = null; this.randomSeed = 0; this.streak = 0; this.usedRandom = new Set(); this.recentRandom = [];
    this.customMap = null;
    this.editor = null;
    this.selPage = -1;

    this.cam = { offset: V(K.CanvasW / 2, K.CanvasH / 2), target: V(0, 0), zoom: K.Px };
    this.camPos = V(this.menuWorld.widthPx / 2, this.menuWorld.heightPx / 2);
    this.shake = 0;

    this.paused = false; this.showIntro = false;
    this.time = 0; this.stateT = 0; this.levelTime = 0; this.wonT = 0; this.hintT = 0; this.hintEnd = 10; this.accumulator = 0;
    this.startPos = V(0, 0);
    this.deaths = 0; this.hintsOn = true; this.newBest = false;
    this.fade = 1; this.fadeDir = -1; this.fadeAction = null;
    this.pend = { jump: false, use: false, blue: false, orange: false, cycle: false };
    this.cursorHidden = false;
    this.toast = ''; this.toastT = 0;
    this.fades = {};
  }

  get currentDef() { return this.customMode ? this.customMap.toDef() : this.randomMode ? this.randomDef : this.chambers[this.level]; }

  // ================================================================ frame

  frame(dt) {
    UI.beginFrame(dt, Input.mouse, this.fadeAction ? false : Input.mousePressed(0), Input.mouseDown(0));
    this.dt = dt;
    Input.captureF5 = this.screen === 'editor';
    this.time += dt;
    this.stateT += dt;
    this.renderer.time = this.time;
    this.toastT = Math.max(0, this.toastT - dt);

    if (Input.pressed('F11')) this.toggleFullscreen();

    this.update(dt);
    this.draw();

    if (this.fadeDir > 0) {
      this.fade += dt * 5;
      if (this.fade >= 1) { this.fade = 1; const a = this.fadeAction; this.fadeAction = null; a?.(); this.fadeDir = -1; }
    } else if (this.fadeDir < 0) {
      this.fade -= dt * 3.5;
      if (this.fade <= 0) { this.fade = 0; this.fadeDir = 0; }
    }
    Input.endFrame();
  }

  toggleFullscreen() {
    if (document.fullscreenElement) document.exitFullscreen?.();
    else document.documentElement.requestFullscreen?.().catch(() => {});
  }

  go(a) {
    if (this.fadeAction) return;
    this.fadeAction = a;
    this.fadeDir = 1;
  }

  setScreen(s) { this.screen = s; this.stateT = 0; this.paused = false; }

  showToast(s) { this.toast = s; this.toastT = 3.5; }

  // ================================================================ map builder flow

  openMyMaps() {
    this.editor = null;
    this.library.reload();
    this.setScreen('mymaps');
  }

  newMap() {
    const names = new Set(this.library.maps.map((m) => m.name));
    let n = 1;
    while (names.has(`MY CHAMBER ${n}`)) n++;
    const map = CustomMap.blank(`MY CHAMBER ${n}`);
    CustomMaps.save(map);
    this.library.maps.unshift(map);
    this.openEditor(map);
  }

  openEditor(map) {
    this.editor = new MapEditor(map, this.g);
    this.editor.showHelp = !this.save.editorHelpSeen;
    if (!this.save.editorHelpSeen) { this.save.editorHelpSeen = true; this.save.store(); }
    this.setScreen('editor');
    this.camPos = this.editor.camTarget;
    this.updateCam(0);
  }

  playCustom(map, testing) {
    this.customMode = true; this.randomMode = false; this.customMap = map; this.fromEditor = testing;
    this.loadLevel(true);
    this.setScreen('play');
  }

  /** Leave a custom map: back to the editor if it was a test, otherwise to the map list. */
  exitCustom() {
    this.go(() => {
      if (this.fromEditor && this.editor) { this.setScreen('editor'); this.editor.afterTest(); }
      else this.openMyMaps();
    });
  }

  /** A map link (#P2D1:...) opened in the browser: add it to the player's maps. */
  async importFromHash() {
    const h = decodeURIComponent(location.hash.slice(1));
    if (!h.startsWith('P2D1:')) return;
    history.replaceState(null, '', location.pathname + location.search);
    const m = await CustomMap.fromShareCode(h);
    if (!m) { this.showToast('THAT MAP LINK COULD NOT BE READ'); return; }
    this.library.reload();
    const dup = this.library.maps.find((x) => x.name === m.name && x.map.join('\n') === m.map.join('\n'));
    if (!dup) { this.library.addMap(m); this.library.toastT = 0; }
    this.showToast(dup ? 'YOU ALREADY HAVE THAT MAP' : `ADDED ${m.name} - PRESS PLAY`);
    this.screen = 'mymaps'; this.stateT = 0;
  }

  // ================================================================ update

  update(dt) {
    const playing = (this.screen === 'play' || this.screen === 'complete') && this.world != null;
    const wantHidden = this.screen === 'play' && !this.paused && this.world?.player && !this.world.player.dead;
    if (!!wantHidden !== this.cursorHidden) {
      this.canvas.style.cursor = wantHidden ? 'none' : 'default';
      this.cursorHidden = !!wantHidden;
    }

    if (this.screen === 'editor' && this.editor) {
      this.editor.update(dt, this.cam, !this.fadeAction);
      this.camPos = this.editor.camTarget;
      this.updateCam(dt);
      if (this.editor.wantExit) { this.editor.wantExit = false; this.go(() => this.openMyMaps()); }
      if (this.editor.wantTest) { this.editor.wantTest = false; const tm = this.editor.map; this.go(() => this.playCustom(tm, true)); }
      return;
    }

    if (!playing) {
      this.step(this.menuWorld, dt, emptyInput(), true);
      this.camPos = V(this.menuWorld.widthPx / 2 + Math.sin(this.time * 0.15) * 10, this.menuWorld.heightPx / 2);
      this.updateCam(dt);
      if (Input.pressed('Escape') && this.screen !== 'menu' && !(this.screen === 'mymaps' && this.library.cancelModal())) this.go(() => this.setScreen(this.screen === 'character' ? 'options' : 'menu'));
      return;
    }

    const w = this.world;
    if (this.screen === 'play') {
      if (Input.pressed('Escape')) { this.paused = !this.paused; this.audio.click(); }
      if (!this.paused) {
        if (Input.pressed('KeyR')) this.go(() => this.loadLevel(false));
        if (Input.pressed('KeyH')) {
          const visible = this.hintsOn && this.hintT < this.hintEnd;
          this.hintsOn = !visible;
          if (this.hintsOn) { this.hintT = this.showIntro ? 3.3 : 0; this.hintEnd = this.hintT + 8; }
        }
        // once the player gets going, let the hints get out of the way
        const hp = w.player;
        if (hp && this.hintT > 4 && this.hintEnd > this.hintT + 1.2 && Math.hypot(hp.pos.x - this.startPos.x, hp.pos.y - this.startPos.y) > 220) this.hintEnd = this.hintT + 1.2;
        this.step(w, dt, this.readInput(), false);
        this.hintT += dt;

        const p = w.player;
        if (p) {
          if (!p.dead && !p.won) this.levelTime += dt;
          if (p.dead && p.deadTimer > 1.1 && !this.fadeAction) {
            this.deaths++;
            this.save.totalDeaths++;
            this.go(() => this.loadLevel(false));
          }
          if (p.won) {
            this.wonT += dt;
            if (this.wonT > 1.0 && !this.fadeAction) this.completeLevel();
          }
        }
      }
    } else {
      this.step(w, dt, emptyInput(), true);
      if (Input.pressed('Enter') || Input.pressed('NumpadEnter')) this.nextLevel();
      if (Input.pressed('Escape')) { if (this.customMode) this.exitCustom(); else this.go(() => this.setScreen('menu')); }
    }
    this.followPlayer(dt, false);
    this.updateCam(dt, this.playZoom());
  }

  mouseWorld() {
    return V((UI.mouse.x * K.Px - this.cam.offset.x) / this.cam.zoom + this.cam.target.x, (UI.mouse.y * K.Px - this.cam.offset.y) / this.cam.zoom + this.cam.target.y);
  }

  readInput() {
    let mx = 0;
    if (Input.down('KeyA') || Input.down('ArrowLeft')) mx -= 1;
    if (Input.down('KeyD') || Input.down('ArrowRight')) mx += 1;
    const one = this.save.oneClickPortals;
    return {
      moveX: mx,
      jumpPressed: Input.pressed('KeyW') || Input.pressed('Space') || Input.pressed('ArrowUp'),
      jumpHeld: Input.down('KeyW') || Input.down('Space') || Input.down('ArrowUp'),
      usePressed: Input.pressed('KeyE') || Input.pressed('KeyF'),
      fireBlue: !one && Input.mousePressed(0),
      fireOrange: !one && Input.mousePressed(2),
      fireCycle: one && Input.mousePressed(0),
      aimWorld: this.mouseWorld(),
    };
  }

  step(w, dt, input, silent) {
    this.accumulator += dt;
    // one-frame presses are latched until a sim step consumes them, so frames that run no step don't swallow a jump
    const pd = this.pend;
    pd.jump ||= input.jumpPressed; pd.use ||= input.usePressed; pd.blue ||= input.fireBlue; pd.orange ||= input.fireOrange; pd.cycle ||= input.fireCycle;
    let first = true, guard = 0;
    while (this.accumulator >= K.SimDt && guard++ < 10) {
      const i = { ...input, jumpPressed: first && pd.jump, usePressed: first && pd.use, fireBlue: first && pd.blue, fireOrange: first && pd.orange, fireCycle: first && pd.cycle };
      if (first) pd.jump = pd.use = pd.blue = pd.orange = pd.cycle = false;
      w.update(K.SimDt, i);
      first = false;
      this.accumulator -= K.SimDt;
    }
    if (this.accumulator > K.SimDt * 10) this.accumulator = 0;
    for (const e of w.events) {
      if (!silent) this.audio.play(e.sound, e.pos, this.camPos);
      if (!silent && this.save.shake) this.shake = Math.max(this.shake, e.shake);
    }
    w.events.length = 0;
  }

  playZoom() {
    return K.Px;
  }

  followPlayer(dt, snap) {
    const w = this.world, p = w.player;
    const target = p ? V(p.pos.x, p.pos.y - 24) : V(w.widthPx / 2, w.heightPx / 2);
    const viewW = K.ScreenW, viewH = K.ScreenH;
    // Clamp the follow position before looking: otherwise looking up at the bottom of a tall room does nothing.
    target.x = w.widthPx <= viewW ? w.widthPx / 2 : clamp(target.x, viewW / 2, w.widthPx - viewW / 2);
    target.y = w.heightPx <= viewH ? w.heightPx / 2 : clamp(target.y, viewH / 2, w.heightPx - viewH / 2);
    if (p && !p.dead && !p.won && this.screen === 'play') {
      // Screen-relative look-ahead has no feedback loop with mouseWorld(). Move the pointer to inspect a room.
      if (Input.inside) {
        target.x += clamp((UI.mouse.x - K.ScreenW / 2) * 0.45, -240, 240);
        target.y += clamp((UI.mouse.y - K.ScreenH / 2) * 0.55, -190, 190);
      }
    }
    target.x = w.widthPx <= viewW ? w.widthPx / 2 : clamp(target.x, viewW / 2, w.widthPx - viewW / 2);
    target.y = w.heightPx <= viewH ? w.heightPx / 2 : clamp(target.y, viewH / 2, w.heightPx - viewH / 2);
    if (snap) this.camPos = target;
    else { const k = 1 - Math.exp(-dt * 7); this.camPos = V(lerp(this.camPos.x, target.x, k), lerp(this.camPos.y, target.y, k)); }
  }

  updateCam(dt, zoom = K.Px) {
    this.shake = Math.max(0, this.shake - dt * 40);
    const ox = this.shake > 0 ? (Math.random() - 0.5) * this.shake * 2 : 0, oy = this.shake > 0 ? (Math.random() - 0.5) * this.shake * 2 : 0;
    this.cam = {
      offset: V(K.CanvasW / 2 + roundEven(ox * K.Px), K.CanvasH / 2 + roundEven(oy * K.Px)),
      target: V(roundEven(this.camPos.x * K.Px) / K.Px, roundEven(this.camPos.y * K.Px) / K.Px),
      zoom,
    };
  }

  // ================================================================ level flow

  startLevel(idx) {
    this.go(() => { this.randomMode = false; this.customMode = false; this.level = idx; this.loadLevel(true); this.setScreen('play'); });
  }

  async ensureRandomPools() {
    if (this.randomPools) return;
    const res = await fetch('data/random.json');
    this.randomPools = (await res.json()).pools.map((pool) => pool.map(makeDef));
  }

  pickRandom() {
    const pool = this.randomPools[Math.min(this.streak, this.randomPools.length - 1)];
    return pickRandomTest(pool, this.usedRandom, this.recentRandom);
  }

  async startRandom(newRun) {
    if (this.fadeAction) return;
    try { await this.ensureRandomPools(); } catch { this.showToast('COULD NOT LOAD RANDOM TESTS - CHECK YOUR CONNECTION'); return; }
    this.go(() => {
      if (newRun) { this.streak = 0; this.usedRandom.clear(); this.recentRandom.length = 0; }
      this.randomMode = true;
      this.customMode = false;
      this.randomDef = this.pickRandom();
      this.randomSeed = this.randomDef.seed;
      this.loadLevel(true);
      this.setScreen('play');
    });
  }

  loadLevel(fresh) {
    this.world = new World(this.currentDef);
    if (fresh) this.hintsOn = !!this.currentDef.tutorial;
    this.levelTime = 0;
    this.wonT = 0;
    this.startPos = this.world.player ? V(this.world.player.pos.x, this.world.player.pos.y) : V(0, 0);
    this.hintEnd = fresh ? 11 : 0;
    if (fresh) { this.deaths = 0; this.showIntro = true; this.hintT = 0; this.stateT = 0; }
    else this.showIntro = false;
    this.paused = false;
    this.accumulator = 0;
    this.followPlayer(0, true);
    this.updateCam(0, this.playZoom());
  }

  completeLevel() {
    const s = this.save;
    if (this.randomMode) {
      this.streak++;
      s.randomCleared++;
      s.randomBestStreak = Math.max(s.randomBestStreak, this.streak);
      this.newBest = false;
    } else if (this.customMode) {
      const cm = this.customMap;
      this.newBest = cm.best <= 0 || this.levelTime < cm.best;
      if (this.newBest) cm.best = this.levelTime;
      cm.verified = true;
      CustomMaps.save(cm);
    } else {
      const best = s.best[this.level];
      this.newBest = best <= 0 || this.levelTime < best;
      if (this.newBest) s.best[this.level] = this.levelTime;
    }
    s.store();
    this.setScreen('complete');
  }

  nextLevel() {
    if (this.customMode) this.exitCustom();
    else if (this.randomMode) this.startRandomNext();
    else if (this.level + 1 >= this.chambers.length) this.go(() => this.setScreen('ending'));
    else this.startLevel(this.level + 1);
  }

  startRandomNext() {
    this.go(() => {
      this.randomDef = this.pickRandom();
      this.randomSeed = this.randomDef.seed;
      this.loadLevel(true);
      this.setScreen('play');
    });
  }

  /** The first chamber you haven't finished yet (or the last one). */
  get continueLevel() {
    for (let i = 0; i < this.chambers.length; i++) if (this.save.best[i] <= 0) return i;
    return this.chambers.length - 1;
  }

  unlocked(i) { return i === 0 || this.save.best[i - 1] > 0 || this.save.best[i] > 0; }

  // ================================================================ draw

  draw() {
    const g = this.g;
    const playing = (this.screen === 'play' || this.screen === 'complete') && this.world != null;
    const shown = playing ? this.world : this.screen === 'editor' && this.editor ? this.editor.world : this.menuWorld;
    const p = shown.player;
    const eye = this.screen === 'editor' ? this.camPos : p && !p.dead
      ? V(p.pos.x, p.pos.y - 10)
      : V(this.camPos.x + Math.sin(this.time * 0.35) * 420, this.camPos.y - 60 + Math.cos(this.time * 0.27) * 140);

    g.setTarget(this.canvas);
    this.renderer.prepareWindows(shown, this.cam, this.canvas);
    g.clear(PP.Ink);
    this.renderer.drawScene(shown, this.cam, eye);
    if (this.screen === 'play' && !this.paused && this.world?.player) this.renderer.drawAimPreview(this.world, this.world.player);
    if (this.screen === 'editor') this.editor?.drawOverlay();

    g.identity();
    if (this.screen !== 'editor') this.vignette();
    switch (this.screen) {
      case 'menu': this.drawMenu(); break;
      case 'select': this.drawSelect(); break;
      case 'options': this.drawOptions(); break;
      case 'controls': this.drawControls(); break;
      case 'character': this.drawCharacter(); break;
      case 'play':
        this.drawHud();
        if (this.paused) this.drawPause(); else this.drawCrosshair();
        break;
      case 'complete': this.drawHud(); this.drawComplete(); break;
      case 'ending': this.drawEnding(); break;
      case 'mymaps': this.drawMyMaps(); break;
      case 'editor': this.editor?.drawUi(); break;
    }
    if (this.toastT > 0 && this.screen !== 'editor') {
      const al = clamp01(this.toastT * 2), m = UI.measure(this.toast, 16);
      const tr = R(K.ScreenW / 2 - m.x / 2 - 24, 96, m.x + 48, 40);
      UI.panel(tr, al, PP.OrangeRamp[2]);
      UI.text(this.toast, V(tr.x + tr.w / 2, tr.y + tr.h / 2), 16, withAlpha(PP.CreamHi, al), Align.Center);
    }
    if (this.fade > 0) g.rect(0, 0, K.ScreenW, K.ScreenH, C(6, 8, 11, 255 * smoothStep(this.fade)));
  }

  vignette() {
    const g = this.g;
    g.rectGradV(0, 0, K.ScreenW, 100, C(0, 0, 0, 90), C(0, 0, 0, 0));
    g.rectGradV(0, K.ScreenH - 100, K.ScreenW, 100, C(0, 0, 0, 0), C(0, 0, 0, 110));
  }

  // ---------------------------------------------------------------- menus

  title(pos) {
    const g = this.g;
    UI.textShadow('PORTAL', pos, 130, PP.CreamHi);
    const m = UI.measure('PORTAL', 130);
    UI.textShadow('2D', V(pos.x + m.x + 36, pos.y), 130, PP.OrangeRamp[2]);
    const y = pos.y + m.y + 18;
    g.rect(Math.trunc(pos.x), Math.trunc(y), Math.trunc(m.x * 0.48), 6, PP.BlueRamp[2]);
    g.rect(Math.trunc(pos.x + m.x * 0.52), Math.trunc(y), Math.trunc(m.x * 0.48), 6, PP.OrangeRamp[2]);
  }

  /** The hero, big, for menus. */
  mascot(feet, scale, facingLeft = false) {
    const g = this.g;
    const frame = (this.time % 3.4) < 0.14 ? 1 : 0;
    const bob = roundEven(Math.sin(this.time * 3) * 1) * scale * 2;
    const size = 16 * scale * 2, h = PLAYER_H * scale * 2;
    g.tex(Sprites.playerTex, frame * 16, 0, facingLeft ? -16 : 16, PLAYER_H, feet.x - size / 2, feet.y - h + bob, size, h);
    g.rect(Math.trunc(feet.x - size * 0.4), Math.trunc(feet.y) + 4, Math.trunc(size * 0.8), 6, C(0, 0, 0, 90));
  }

  drawMenu() {
    const g = this.g, s = this.save;
    // calm the busy background: a general dim plus a deeper shade behind the menu column
    g.rect(0, 0, K.ScreenW, K.ScreenH, C(10, 11, 16, 70));
    g.rectGradH(0, 0, 980, K.ScreenH, C(10, 11, 16, 250), C(10, 11, 16, 0));
    const intro = easeOutCubic(this.stateT * 2);
    const x = roundEven(80 - (1 - intro) * 40);
    this.title(V(x, 60));
    UI.text('A SIDE-SCROLLING TEST OF SPATIAL REASONING', V(x + 2, 236), 16, withAlpha(DIM, 0.8));
    this.mascot(V(x + 990, 196), 4, true);

    // three main actions, then the rest in one quiet row
    let y = 280;
    const h = 60, gap = 8, bw = 520;
    const cont = this.continueLevel;
    const started = s.best.some((b) => b > 0);
    const sub = `CHAMBER ${String(cont + 1).padStart(2, '0')} - ${this.chambers[cont].name}`;
    if (UI.button('play', R(x, y, bw, h), started ? 'CONTINUE' : 'BEGIN TESTING', { accent: PP.OrangeRamp[2], sub })) this.startLevel(cont);
    y += h + gap;
    const rsub = s.randomBestStreak > 0 ? `BEST STREAK ${s.randomBestStreak}` : 'ENDLESS GENERATED CHAMBERS';
    if (UI.button('random', R(x, y, bw, h), 'RANDOM TEST', { accent: PP.Exit, sub: rsub })) this.startRandom(true);
    y += h + gap;
    const msub = this.library.maps.length > 0 ? `${this.library.maps.length} SAVED` : 'BUILD YOUR OWN CHAMBERS';
    if (UI.button('builder', R(x, y, bw, h), 'MAP BUILDER', { accent: PP.BlueRamp[3], sub: msub })) this.go(() => this.openMyMaps());
    y += h + 20;
    const sw = (bw - 2 * gap) / 3;
    if (UI.smallButton('select', R(x, y, sw, 48), 'CHAMBERS')) this.go(() => { this.selPage = -1; this.setScreen('select'); });
    if (UI.smallButton('options', R(x + sw + gap, y, sw, 48), 'OPTIONS')) this.go(() => this.setScreen('options'));
    if (UI.smallButton('controls', R(x + 2 * (sw + gap), y, sw, 48), 'CONTROLS')) this.go(() => this.setScreen('controls'));

    const done = s.best.filter((b) => b > 0).length;
    UI.text(`${done}/${this.chambers.length} CHAMBERS COMPLETE`, V(x + 2, K.ScreenH - 30), 16, withAlpha(DIM, 0.8));
    UI.text(`V${VERSION}`, V(K.ScreenW - 24, K.ScreenH - 30), 16, withAlpha(DIM, 0.5), Align.Right);
  }

  header(title, sub, backTo = 'menu') {
    const g = this.g;
    g.rect(0, 0, K.ScreenW, K.ScreenH, C(10, 11, 16, 215));
    UI.textShadow(title, V(66, 40), 46, PP.CreamHi);
    UI.text(sub, V(68, 98), 16, DIM);
    if (UI.smallButton('back', R(K.ScreenW - 66 - 150, 44, 150, 44), 'BACK')) this.go(() => this.setScreen(backTo));
  }

  drawMyMaps() {
    this.header('MAP BUILDER', `${this.library.maps.length} ${this.library.maps.length === 1 ? 'MAP' : 'MAPS'} SAVED IN THIS BROWSER`);
    const { act, map } = this.library.draw(this.dt, this.stateT, DIM);
    if (act === 'new') this.go(() => this.newMap());
    else if (act === 'edit') this.go(() => this.openEditor(map));
    else if (act === 'play') this.go(() => this.playCustom(map, false));
  }

  drawSelect() {
    const g = this.g, s = this.save, n = this.chambers.length;
    const done = s.best.filter((b) => b > 0).length;
    this.header('SELECT CHAMBER', `${done} OF ${n} COMPLETE   TOTAL DEATHS ${s.totalDeaths}`);
    const cols = 6, perPage = 18, cw = 180, ch = 132, gx = 14, gy = 14;
    const x0 = roundEven((K.ScreenW - (cols * cw + (cols - 1) * gx)) / 2), y0 = 136;
    const pages = Math.ceil(n / perPage);
    if (this.selPage < 0) this.selPage = Math.min(Math.floor(this.continueLevel / perPage), pages - 1);
    if (Input.pressed('ArrowRight') && this.selPage < pages - 1) { this.selPage++; this.stateT = 0.2; this.audio.click(); }
    if (Input.pressed('ArrowLeft') && this.selPage > 0) { this.selPage--; this.stateT = 0.2; this.audio.click(); }
    const first = this.selPage * perPage;
    for (let i = first; i < Math.min(first + perPage, n); i++) {
      const col = (i - first) % cols, row = Math.floor((i - first) / cols);
      let r = R(x0 + col * (cw + gx), y0 + row * (ch + gy), cw, ch);
      const locked = !this.unlocked(i), complete = s.best[i] > 0;
      const over = !locked && contains(r, UI.mouse);
      if (this.stateT * 4 - (i - first) * 0.12 < 0) continue;
      if (over) r = { ...r, y: r.y - 4 };
      const accent = complete ? PP.Exit : locked ? C(70, 76, 86) : PP.BlueRamp[2];
      UI.panel(r, 1, over ? PP.BlueRamp[2] : null);
      g.rect(r.x + 2, r.y + 2, 4, r.h - 4, accent);
      const textCol = locked ? C(90, 98, 110) : PP.CreamHi;
      UI.text(String(i + 1).padStart(2, '0'), V(r.x + 18, r.y + 14), 46, textCol);
      const name = locked ? 'LOCKED' : this.chambers[i].name;
      let l1 = '', l2 = '';
      for (const wd of name.toUpperCase().split(' ')) {
        if (l2.length === 0 && UI.measure((l1 + ' ' + wd).trim(), 16).x < cw - 30) l1 = (l1 + ' ' + wd).trim();
        else l2 = (l2 + ' ' + wd).trim();
      }
      UI.text(l1, V(r.x + 18, r.y + 68), 16, textCol);
      if (l2.length > 0) UI.text(l2, V(r.x + 18, r.y + 86), 16, textCol);
      const status = locked ? '' : complete ? UI.formatTime(s.best[i]) : 'NEW';
      UI.text(status, V(r.x + 18, r.y + ch - 24), 16, complete ? PP.Exit : PP.OrangeRamp[2]);
      if (locked) this.drawLock(V(r.x + cw - 34, r.y + 30));
      else if (complete) this.drawCheck(V(r.x + cw - 30, r.y + 30));
      if (over && UI.click) { this.audio.click(); this.startLevel(i); }
    }
    const by = y0 + 3 * (ch + gy) + 6, full = cols * cw + (cols - 1) * gx;
    const rs = s.randomBestStreak > 0 ? `BEST STREAK ${s.randomBestStreak}   CLEARED ${s.randomCleared}` : 'VARIED PUZZLES - BUILD YOUR BEST STREAK';
    if (UI.button('sel_random', R(x0, by, full - 300, 60), 'RANDOM TEST', { accent: PP.Exit, sub: rs })) this.startRandom(true);
    const px = x0 + full - 284;
    if (this.selPage > 0 && UI.smallButton('sel_prev', R(px, by + 6, 70, 48), '<')) { this.selPage--; this.stateT = 0.2; }
    UI.text(`${first + 1}-${Math.min(first + perPage, n)}`, V(px + 142, by + 30), 16, PP.Cream, Align.Center);
    UI.text(`PAGE ${this.selPage + 1}/${pages}`, V(px + 142, by + 12), 16, DIM, Align.Center);
    if (this.selPage < pages - 1 && UI.smallButton('sel_next', R(px + 214, by + 6, 70, 48), '>')) { this.selPage++; this.stateT = 0.2; }
  }

  drawLock(c) {
    const g = this.g, col = C(90, 98, 110);
    g.rect(c.x - 8, c.y - 14, 4, 10, col);
    g.rect(c.x + 4, c.y - 14, 4, 10, col);
    g.rect(c.x - 6, c.y - 18, 12, 4, col);
    g.rect(c.x - 12, c.y - 4, 24, 18, col);
    g.rect(c.x - 2, c.y + 2, 4, 6, C(30, 34, 44));
  }

  drawCheck(c) {
    const g = this.g;
    for (let i = 0; i < 4; i++) g.rect(c.x - 10 + i * 4, c.y + i * 4 - 4, 4, 4, PP.Exit);
    for (let i = 0; i < 5; i++) g.rect(c.x + 2 + i * 4, c.y + 4 - i * 4, 4, 4, PP.Exit);
  }

  drawOptions() {
    const g = this.g, s = this.save;
    this.header('OPTIONS', 'AUDIO, VISUALS AND DISPLAY');
    const panel = R(K.ScreenW / 2 - 300, 118, 600, 596);
    UI.panel(panel);
    const x = panel.x + 40, w = panel.w - 80;
    let y = panel.y + 48;
    let changed = false;
    const sl = (id, label, key) => {
      const nv = UI.slider(id, R(x, y, w, 20), label, s[key]);
      if (nv !== null) { s[key] = nv; changed = true; }
    };
    sl('vol_master', 'MASTER VOLUME', 'master'); y += 66;
    sl('vol_sfx', 'EFFECTS', 'sfx'); y += 66;
    sl('vol_amb', 'AMBIENCE', 'ambience'); y += 42;
    if (changed) {
      this.audio.master = s.master; this.audio.sfxVolume = s.sfx; this.audio.ambienceVolume = s.ambience;
      this.audio.applyVolumes();
    }
    const tg = (id, label, key, after) => {
      const nv = UI.toggle(id, R(x - 10, y, w + 20, 54), label, s[key]);
      if (nv !== null) { s[key] = nv; changed = true; after?.(nv); }
      y += 60;
    };
    tg('opt_cones', 'PORTAL WINDOWS', 'cones', (v) => { this.renderer.windowsEnabled = v; });
    tg('opt_shake', 'SCREEN SHAKE', 'shake');
    const fsNow = !!document.fullscreenElement;
    const fs = UI.toggle('opt_fs', R(x - 10, y, w + 20, 54), 'FULLSCREEN (F11)', fsNow);
    if (fs !== null) this.toggleFullscreen();
    y += 60;
    const oc = UI.toggle('opt_oneclick', R(x - 10, y, w + 20, 54), 'ONE-BUTTON PORTALS', s.oneClickPortals);
    if (oc !== null) { s.oneClickPortals = oc; changed = true; }
    UI.text(s.oneClickPortals ? 'LEFT CLICK ALTERNATES BLUE AND ORANGE' : 'LEFT CLICK = BLUE   RIGHT CLICK = ORANGE', V(x, y + 58), 16, DIM);
    y += 88;
    if (UI.button('opt_char', R(x - 10, y, w + 20, 72), 'CUSTOMISE CHARACTER', { accent: PP.OrangeRamp[2], sub: 'COLOURS, HATS, FACES AND OUTFITS' })) this.go(() => this.setScreen('character'));
    y += 84;
    UI.text(`VERSION ${VERSION}   WEB EDITION`, V(x, y), 16, DIM);
    if (changed && !UI.mouseDown) s.store();
  }

  // ---------------------------------------------------------------- character customiser

  setLook(patch) {
    this.save.look = cleanLook({ ...this.save.look, ...patch });
    Sprites.setLook(this.save.look);
    this.save.store();
  }

  drawCharacter() {
    const g = this.g, look = this.save.look;
    this.header('CHARACTER', 'COLOURS, HATS, FACES AND OUTFITS', 'options');
    // preview
    const pv = R(66, 136, 440, 520);
    UI.panel(pv);
    g.additive = true;
    g.circleGrad(pv.x + pv.w / 2, pv.y + 300, 190, withAlpha(PP.BlueRamp[2], 70 / 255), withAlpha(PP.BlueRamp[2], 0));
    g.additive = false;
    this.mascot(V(pv.x + pv.w / 2, pv.y + 400), 8);
    UI.text(COLORS[look.color].name, V(pv.x + pv.w / 2, pv.y + 478), 30, PP.CreamHi, Align.Center);

    // controls
    const cx = 530, cw = 684;
    UI.panel(R(cx, 136, cw, 520));
    const x = cx + 40, w = cw - 80;
    let y = 160;
    UI.text('COLOUR', V(x, y), 16, DIM);
    COLORS.forEach((c, i) => {
      const r = R(x + i * 76, y + 28, 64, 64);
      const over = contains(r, UI.mouse);
      g.rect(r.x + 2, r.y + 3, r.w, r.h, C(0, 0, 0, 100));
      g.rectR(r, c.body);
      g.rect(r.x, r.y, r.w, 8, c.hi);
      g.rect(r.x, r.y + r.h - 8, r.w, 8, c.lo);
      g.rectLines(r, look.color === i ? 4 : 2, look.color === i ? WHITE : over ? PP.BlueRamp[2] : c.out);
      if (over && UI.click) { UI.onClick?.(); this.setLook({ color: i }); }
    });
    y += 116;
    const selector = (id, label, list, key) => {
      const n = list.length, cur = look[key];
      UI.text(label, V(x, y), 16, DIM);
      UI.text(`${cur + 1}/${n}`, V(x + w, y), 16, DIM, Align.Right);
      const nameR = R(x + 76, y + 24, w - 152, 52);
      g.rectR(nameR, C(24, 27, 36));
      g.rectLines(nameR, 2, PANEL_EDGE_C);
      UI.text(list[cur], V(nameR.x + nameR.w / 2, nameR.y + nameR.h / 2), 30, PP.CreamHi, Align.Center);
      if (UI.smallButton(id + '_l', R(x, y + 24, 64, 52), '<')) this.setLook({ [key]: (cur + n - 1) % n });
      if (UI.smallButton(id + '_r', R(x + w - 64, y + 24, 64, 52), '>')) this.setLook({ [key]: (cur + 1) % n });
      y += 96;
    };
    selector('hat', 'HAT', HATS.map((h) => h.name), 'hat');
    selector('face', 'FACE', FACE_NAMES, 'face');
    selector('outfit', 'OUTFIT', OUTFIT_NAMES, 'outfit');
    const bw = (w - 24) / 3, by = 584;
    if (UI.smallButton('look_rand', R(x, by, bw, 52), 'RANDOM', PP.Exit)) {
      const rnd = (n) => Math.floor(Math.random() * n);
      this.setLook({ color: rnd(COLORS.length), hat: rnd(HATS.length), face: rnd(FACE_NAMES.length), outfit: rnd(OUTFIT_NAMES.length) });
    }
    if (UI.smallButton('look_reset', R(x + bw + 12, by, bw, 52), 'RESET')) this.setLook(DEFAULT_LOOK);
    if (UI.smallButton('look_done', R(x + 2 * (bw + 12), by, bw, 52), 'DONE', PP.OrangeRamp[2])) this.go(() => this.setScreen('options'));
  }

  drawControls() {
    this.header('CONTROLS', 'EVERYTHING YOU NEED TO KNOW');
    const panel = R(K.ScreenW / 2 - 380, 150, 760, 500);
    UI.panel(panel);
    const oc = this.save.oneClickPortals;
    const rows = [
      [['A', 'D'], 'MOVE LEFT / RIGHT'],
      [['W', 'SPACE'], 'JUMP (HOLD FOR HIGHER)'],
      [['LEFT MOUSE'], oc ? 'FIRE A PORTAL (BLUE, THEN ORANGE)' : 'FIRE THE BLUE PORTAL'],
      [['RIGHT MOUSE'], oc ? '(NOT USED: ONE-BUTTON PORTALS)' : 'FIRE THE ORANGE PORTAL'],
      [['E'], 'PICK UP / DROP A CUBE'],
      [['R'], 'RESTART THE CHAMBER'],
      [['H'], 'SHOW / HIDE HINTS'],
      [['ESC'], 'PAUSE'],
      [['F11'], 'TOGGLE FULLSCREEN'],
    ];
    let y = panel.y + 30;
    for (const [keys, what] of rows) {
      let x = panel.x + 46;
      for (const k of keys) { UI.keyCap(k, V(x, y)); x += UI.keyCapWidth(k) + 8; }
      UI.text(what, V(panel.x + 330, y + 9), 16, PP.Cream);
      y += 50;
    }
  }

  drawEnding() {
    const g = this.g, s = this.save;
    g.rect(0, 0, K.ScreenW, K.ScreenH, C(10, 11, 16, 220));
    const a = clamp01(this.stateT * 1.5);
    UI.textShadow('ALL TESTS COMPLETE', V(K.ScreenW / 2, 170), 60, withAlpha(PP.CreamHi, a), Align.Center);
    UI.text(`COMBINED BEST TIME ${UI.formatTime(s.best.reduce((p, c) => p + c, 0))}`, V(K.ScreenW / 2, 250), 30, withAlpha(PP.OrangeRamp[2], a), Align.Center);
    const lines = ['YOU HAVE DEMONSTRATED AN EXCEPTIONAL GRASP', 'OF NON-EUCLIDEAN LOCOMOTION.', '', 'THE RANDOM TEST NEVER ENDS. HOW LONG CAN YOUR STREAK GO?'];
    lines.forEach((l, i) => UI.text(l, V(K.ScreenW / 2, 320 + i * 30), 16, withAlpha(PP.Cream, a), Align.Center));
    this.mascot(V(K.ScreenW / 2, 540), 3);
    if (UI.smallButton('end_random', R(K.ScreenW / 2 - 250, 590, 240, 50), 'RANDOM TEST', PP.Exit)) this.startRandom(true);
    if (UI.smallButton('end_menu', R(K.ScreenW / 2 + 10, 590, 240, 50), 'MAIN MENU', PP.OrangeRamp[2])) this.go(() => this.setScreen('menu'));
  }

  // ---------------------------------------------------------------- in-game UI

  hudNumber() { return this.customMode ? 'MY' : this.randomMode ? String(this.streak + 1).padStart(2, '0') : String(this.level + 1).padStart(2, '0'); }

  /** UI that sits over the game fades to 20% while the cursor is over it, so it never hides the action. */
  hudAlpha(key, r) {
    const hot = Input.inside && contains({ x: r.x - 10, y: r.y - 10, w: r.w + 20, h: r.h + 20 }, UI.mouse) && this.screen === 'play' && !this.paused;
    const v = approach(this.fades[key] ?? 1, hot ? 0.2 : 1, this.dt * 6);
    this.fades[key] = v;
    return v;
  }

  drawHud() {
    const g = this.g, w = this.world, def = w.def;
    const num = this.hudNumber();
    const label = this.customMode ? (this.fromEditor ? 'MAP TEST' : 'CUSTOM MAP') : this.randomMode ? `RANDOM TEST  SEED ${this.randomSeed}` : 'TEST CHAMBER';
    const name = this.customMode ? def.name.toUpperCase() : this.randomMode ? 'STREAK' : def.name.toUpperCase();
    const numM = UI.measure(num, 46);
    const nameX = 34 + numM.x + 24;
    const tagW = Math.max(UI.measure(name, 30).x, UI.measure(label, 16).x) + nameX + 24 - 16;
    const tag = R(16, 14, tagW, 74);
    g.opacity = this.hudAlpha('tag', tag);
    UI.panel(tag, 0.92);
    g.rect(tag.x + 2, tag.y + 2, 4, tag.h - 4, this.customMode ? PP.BlueRamp[3] : this.randomMode ? PP.Exit : PP.OrangeRamp[2]);
    UI.text(num, V(34, 30), 46, PP.CreamHi, Align.Left, true);
    UI.text(label, V(nameX, 26), 16, DIM);
    UI.text(name, V(nameX, 48), 30, PP.CreamHi, Align.Left, true);
    g.opacity = 1;

    const tr = R(K.ScreenW - 16 - 250, 14, 250, 74);
    g.opacity = this.hudAlpha('time', tr);
    UI.panel(tr, 0.92);
    UI.text(UI.formatTime(Math.max(this.levelTime, 0.001)), V(tr.x + tr.w - 18, tr.y + 14), 30, PP.CreamHi, Align.Right, true);
    UI.text(`DEATHS ${this.deaths}`, V(tr.x + tr.w - 18, tr.y + 50), 16, DIM, Align.Right);
    for (let i = 0; i < 2; i++) {
      const col = i === 0 ? PP.BlueRamp[2] : PP.OrangeRamp[2];
      const cx = Math.trunc(tr.x) + 18 + i * 16, cy = Math.trunc(tr.y) + 52;
      const placed = w.portals[i] != null;
      const usable = w.gunAllows(i) || w.portals[i]?.fixed === true;
      g.rect(cx, cy - 10, 10, 20, usable ? col : C(70, 76, 86));
      if (!placed) g.rect(cx + 2, cy - 8, 6, 16, C(18, 20, 27));
    }
    g.opacity = 1;

    if (this.showIntro && this.stateT < 3.2) {
      const t = this.stateT;
      const slide = t < 0.4 ? easeOutCubic(t / 0.4) : t > 2.8 ? 1 - easeOutCubic((t - 2.8) / 0.4) : 1;
      const cpos = V(K.ScreenW / 2 - 220, roundEven(200 - (1 - slide) * 24));
      g.opacity = this.hudAlpha('card', R(cpos.x, cpos.y, 440, 250));
      this.drawChamberCard(cpos, slide);
      g.opacity = 1;
    }

    if (def.exitGroups?.length) {
      const powered = def.exitGroups.filter((group) => w.groupActive[group]).length;
      const cr = R(16, K.ScreenH - 104, 356, 54);
      g.opacity = this.hudAlpha('circuits', cr);
      UI.panel(cr, 0.92);
      UI.text(`EXIT CIRCUITS ${powered}/${def.exitGroups.length}`, V(cr.x + 14, cr.y + 12), 16, w.exitReady ? PP.Exit : PP.Cream);
      def.exitGroups.forEach((group, i) => {
        g.rect(cr.x + 14 + i * 30, cr.y + 34, 22, 6, w.groupActive[group] ? PP.Exit : PP.Hazard);
      });
      g.opacity = 1;
    }

    const hints = this.save.oneClickPortals ? def.hints.map((h) => h.replace('RIGHT MOUSE fires orange.', 'Click again for orange.')) : def.hints;
    if (hints.length > 0) {
      const show = this.hintsOn ? clamp01((this.hintT - (this.showIntro ? 3.3 : 0.2)) * 3) * clamp01((this.hintEnd - this.hintT) / 0.8) : 0;
      if (show > 0.01) {
        const maxW = Math.max(...hints.map((h) => UI.measure(h, 16).x));
        const hr = R(K.ScreenW / 2 - maxW / 2 - 30, 104, maxW + 60, hints.length * 28 + 26);
        g.opacity = this.hudAlpha('hints', hr);
        UI.panel(hr, show);
        g.rect(hr.x + 2, hr.y + 2, 4, hr.h - 4, withAlpha(PP.BlueRamp[2], show));
        hints.forEach((h, i) => UI.text(h, V(K.ScreenW / 2 + 4, hr.y + 27 + i * 28), 16, withAlpha(PP.Cream, show), Align.Center));
        g.opacity = 1;
      } else if (!this.hintsOn || this.hintT > this.hintEnd) {
        g.opacity = this.hudAlpha('foot', R(20, K.ScreenH - 30, 400, 16));
        UI.text('H HINTS   R RESTART   ESC PAUSE', V(20, K.ScreenH - 30), 16, withAlpha(DIM, 0.6));
        g.opacity = 1;
      }
    }

    const pd = w.player;
    if (pd && pd.dead) {
      const a = clamp01(pd.deadTimer * 2);
      UI.textShadow('SUBJECT TERMINATED', V(K.ScreenW / 2, K.ScreenH / 2 - 20), 46, C(255, 90, 80, 255 * a), Align.Center);
      UI.text('RESTARTING CHAMBER', V(K.ScreenW / 2, K.ScreenH / 2 + 30), 16, withAlpha(DIM, a), Align.Center);
    }
  }

  drawChamberCard(pos, alpha) {
    const g = this.g, def = this.world.def;
    const r = R(roundEven(pos.x / 2) * 2, pos.y, 440, 250);
    const A = (v) => v * alpha;
    g.rect(r.x + 8, r.y + 8, r.w, r.h, C(0, 0, 0, A(110)));
    g.rectR(r, C(234, 229, 218, A(250)));
    g.rectLines(r, 2, C(30, 32, 40, A(255)));
    g.rect(r.x + 2, r.y + 2, r.w - 4, 2, C(255, 255, 255, A(200)));
    const ink = C(30, 32, 40, A(255));
    const num = this.hudNumber();
    UI.text(num, V(r.x + 28, r.y + 22), 120, ink);
    const nm = UI.measure(num, 120);
    UI.text(this.customMode ? 'MAP' : this.randomMode ? 'RANDOM' : `/${this.chambers.length}`, V(r.x + 40 + nm.x, r.y + 22 + nm.y - 28), 30, C(120, 122, 130, A(255)));
    g.rect(r.x + 28, r.y + 150, r.w - 56, 4, ink);
    UI.text(this.customMode ? def.name.toUpperCase() : this.randomMode ? 'GENERATED CHAMBER' : def.name.toUpperCase(), V(r.x + 28, r.y + 166), 30, ink);
    Icons.draw(g, def, V(r.x + 28, r.y + 206), 28, ink, false, true);
    g.rect(r.x + r.w - 64, r.y + 28, 32, 32, withAlpha(PP.OrangeRamp[2], alpha));
    g.rect(r.x + r.w - 64, r.y + 68, 32, 32, withAlpha(PP.BlueRamp[2], alpha));
  }

  drawCrosshair() {
    const g = this.g, w = this.world;
    if (!w.player || w.player.dead) return;
    const c = V(roundEven(UI.mouse.x / 2) * 2, roundEven(UI.mouse.y / 2) * 2);
    g.rect(c.x - 2, c.y - 2, 4, 4, WHITE);
    if (w.def.gun === GunMode.None) {
      for (const [dx, dy] of [[-12, 0], [12, 0], [0, -12], [0, 12]]) g.rect(c.x + dx - 2, c.y + dy - 2, 4, 4, C(255, 255, 255, 110));
      return;
    }
    for (let i = 0; i < 2; i++) {
      const col = i === 0 ? PP.BlueRamp[2] : PP.OrangeRamp[2];
      const placed = w.portals[i] != null, usable = w.gunAllows(i);
      const cc = usable ? withAlpha(col, placed ? 1 : 0.45) : C(120, 120, 120, 90);
      const sx = i === 0 ? -1 : 1;
      // pixel bracket: [  ] in blue / orange
      g.rect(c.x + sx * 14 - (sx < 0 ? 4 : 0), c.y - 12, 4, 24, cc);
      g.rect(c.x + sx * 14 - (sx < 0 ? 0 : 4) + (sx < 0 ? 0 : -4), c.y - 12, 8, 4, cc);
      g.rect(c.x + sx * 14 - (sx < 0 ? 0 : 4) + (sx < 0 ? 0 : -4), c.y + 8, 8, 4, cc);
      if (placed) g.rect(c.x + sx * 22 - 2, c.y - 2, 4, 4, col);
    }
  }

  drawPause() {
    const g = this.g;
    g.rect(0, 0, K.ScreenW, K.ScreenH, C(6, 8, 12, 170));
    const r = R(K.ScreenW / 2 - 230, K.ScreenH / 2 - 214, 460, 428);
    UI.panel(r);
    UI.textShadow('PAUSED', V(K.ScreenW / 2, r.y + 50), 46, PP.CreamHi, Align.Center);
    let y = r.y + 100;
    const x = r.x + 36, bw = r.w - 72;
    if (UI.button('p_resume', R(x, y, bw, 56), 'RESUME', { accent: PP.OrangeRamp[2] })) this.paused = false;
    y += 68;
    if (UI.button('p_restart', R(x, y, bw, 56), 'RESTART')) this.go(() => this.loadLevel(false));
    y += 68;
    if (this.customMode) { if (UI.button('p_select', R(x, y, bw, 56), this.fromEditor ? 'BACK TO EDITOR' : 'MY MAPS')) this.exitCustom(); }
    else if (UI.button('p_select', R(x, y, bw, 56), 'CHAMBERS')) this.go(() => this.setScreen('select'));
    y += 68;
    if (UI.button('p_menu', R(x, y, bw, 56), 'MAIN MENU')) this.go(() => this.setScreen('menu'));
  }

  drawComplete() {
    const g = this.g, s = this.save, w = this.world;
    const a = clamp01(this.stateT * 4);
    g.rect(0, 0, K.ScreenW, K.ScreenH, C(6, 8, 12, 160 * a));
    const r = R(K.ScreenW / 2 - 290, roundEven(K.ScreenH / 2 - 236 + (1 - easeOutCubic(this.stateT * 3)) * 30), 580, 472);
    UI.panel(r, a, PP.Exit);
    const head = this.customMode ? (this.fromEditor ? 'MAP TEST' : 'CUSTOM MAP') + '  ' + w.def.name.toUpperCase()
      : this.randomMode ? `RANDOM TEST  SEED ${this.randomSeed}` : `TEST CHAMBER ${String(this.level + 1).padStart(2, '0')}`;
    UI.text(head, V(K.ScreenW / 2, r.y + 40), 16, withAlpha(DIM, a), Align.Center);
    UI.textShadow(this.randomMode ? 'PASSED' : 'COMPLETE', V(K.ScreenW / 2, r.y + 90), 60, withAlpha(PP.Exit, a), Align.Center);

    let y = r.y + 150;
    const row = (k, v, vc, tag = null) => {
      UI.text(k, V(r.x + 50, y + 4), 16, withAlpha(DIM, a));
      UI.text(v, V(r.x + r.w - 50, y), 30, withAlpha(vc, a), Align.Right);
      if (tag) {
        const m = UI.measure(v, 30);
        const tr = R(r.x + r.w - 50 - m.x - 120, y, 106, 26);
        g.rectR(tr, withAlpha(PP.OrangeRamp[2], a));
        UI.text(tag, V(tr.x + tr.w / 2, tr.y + 13), 16, PP.Ink, Align.Center);
      }
      g.rect(r.x + 50, y + 38, r.w - 100, 2, C(255, 255, 255, 20 * a));
      y += 50;
    };
    row('TIME', UI.formatTime(this.levelTime), PP.CreamHi, this.newBest ? 'NEW BEST' : null);
    if (this.randomMode) row('STREAK', String(this.streak), PP.Exit, this.streak >= s.randomBestStreak && this.streak > 1 ? 'RECORD' : null);
    else if (this.customMode) row('BEST', UI.formatTime(this.customMap.best), PP.Cream);
    else row('BEST', UI.formatTime(s.best[this.level]), PP.Cream);
    row('PORTALS PLACED', String(w.portalsPlaced), PP.Cream);
    row('DEATHS', String(this.deaths), this.deaths === 0 ? PP.Exit : PP.Cream);

    const last = !this.randomMode && !this.customMode && this.level + 1 >= this.chambers.length;
    const by = r.y + r.h - 80;
    const next = this.customMode ? (this.fromEditor ? 'BACK TO EDITOR' : 'MY MAPS') : this.randomMode ? 'NEXT TEST' : last ? 'FINISH' : 'NEXT (ENTER)';
    if (UI.smallButton('c_next', R(r.x + r.w - 50 - 210, by, 210, 50), next, PP.Exit)) this.nextLevel();
    if (!this.randomMode && UI.smallButton('c_retry', R(r.x + 50, by, 124, 50), 'REPLAY')) {
      if (this.customMode) { const cm = this.customMap, fe = this.fromEditor; this.go(() => this.playCustom(cm, fe)); }
      else this.startLevel(this.level);
    }
    if (UI.smallButton('c_menu', R(r.x + (this.randomMode ? 50 : 186), by, 100, 50), 'MENU')) this.go(() => this.setScreen('menu'));
  }
}
