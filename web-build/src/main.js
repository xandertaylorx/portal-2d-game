// Boot: load the level data, build the app, run the frame loop and keep the canvas fitted to the window.
import { App } from './app.js';
import { Input } from './input.js';
import { CustomMap, CustomMaps } from './maps.js';

const canvas = document.getElementById('game');
const status = document.getElementById('status');

function fit() {
  const s = Math.min(window.innerWidth / 640, window.innerHeight / 360);
  const px = s >= 1 ? Math.floor(s) : s;          // whole-number pixel scale when it fits, like the desktop game
  canvas.style.width = `${640 * px}px`;
  canvas.style.height = `${360 * px}px`;
}
window.addEventListener('resize', fit);
fit();

async function boot() {
  let levels;
  try {
    levels = await (await fetch('data/levels.json')).json();
  } catch (e) {
    status.textContent = 'Could not load the game data. If you opened this file directly, serve the folder over http instead.';
    return;
  }
  const app = new App(canvas, { levels });
  window.portal2d = app;
  Input.attach(canvas);
  status.remove();
  canvas.style.visibility = 'visible';
  jumpFromUrl(app);
  app.importFromHash();
  window.addEventListener('hashchange', () => app.importFromHash());

  let last = performance.now();
  const loop = (now) => {
    const dt = Math.min((now - last) / 1000, 0.05);
    last = now;
    try { app.frame(dt); } catch (e) { console.error(e); }
    requestAnimationFrame(loop);
  };
  requestAnimationFrame(loop);
}

/** ?level=N opens a chamber directly; ?screen=select|options|controls|mymaps|editor opens that screen. */
function jumpFromUrl(app) {
  const q = new URLSearchParams(location.search);
  const skip = () => { app.fade = 0; app.fadeDir = 0; };
  if (q.has('level')) {
    const n = Math.max(1, Math.min(app.chambers.length, parseInt(q.get('level'), 10) || 1));
    app.level = n - 1; app.randomMode = false; app.customMode = false;
    app.loadLevel(true); app.setScreen('play'); skip();
  } else if (q.has('screen')) {
    const s = q.get('screen');
    if (['select', 'options', 'controls', 'mymaps'].includes(s)) { if (s === 'mymaps') app.library.reload(); app.setScreen(s); skip(); }
    else if (s === 'editor') {
      const m = CustomMap.blank('DEMO');
      app.openEditor(m); app.editor.persist = false; app.editor.demo(); app.editor.showHelp = q.has('help'); skip();
    }
  }
}

boot();
