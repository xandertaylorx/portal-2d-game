// Keyboard, mouse and typed-character state, polled once per frame like Raylib's IsKeyPressed / IsKeyDown.
import { K, V } from './core.js';

export const Input = {
  keys: new Set(),            // currently held (event.code)
  pressedSet: new Set(),      // went down this frame
  repeatSet: new Set(),       // auto-repeat presses this frame
  mouseHeld: [false, false, false],
  mousePressedArr: [false, false, false],
  wheelX: 0, wheelY: 0,
  chars: [],
  mouse: V(0, 0),             // virtual 1280x720 coordinates
  onGesture: null,
  inside: false,
  captureF5: false,           // the map editor uses F5 for test play

  down(code) { return this.keys.has(code); },
  pressed(code) { return this.pressedSet.has(code); },
  pressedRepeat(code) { return this.repeatSet.has(code); },
  mouseDown(b) { return this.mouseHeld[b]; },
  mousePressed(b) { return this.mousePressedArr[b]; },
  get ctrl() { return this.keys.has('ControlLeft') || this.keys.has('ControlRight') || this.keys.has('MetaLeft') || this.keys.has('MetaRight'); },
  get shift() { return this.keys.has('ShiftLeft') || this.keys.has('ShiftRight'); },

  /** Call at the end of every frame. */
  endFrame() {
    this.pressedSet.clear();
    this.repeatSet.clear();
    this.mousePressedArr = [false, false, false];
    this.wheelX = 0; this.wheelY = 0;
    this.chars.length = 0;
  },

  attach(canvas) {
    const BLOCK = new Set(['Space', 'ArrowUp', 'ArrowDown', 'ArrowLeft', 'ArrowRight', 'Tab', 'F11', 'Backspace', 'Slash']);
    window.addEventListener('keydown', (e) => {
      this.onGesture?.();
      if (e.repeat) this.repeatSet.add(e.code);
      else { this.pressedSet.add(e.code); this.keys.add(e.code); }
      if (e.key.length === 1 && !e.ctrlKey && !e.metaKey) this.chars.push(e.key);
      // keep the page still while playing, and let ctrl+S / ctrl+Z reach the game instead of the browser
      if (BLOCK.has(e.code) || (e.code === 'F5' && this.captureF5) || ((e.ctrlKey || e.metaKey) && ['KeyS', 'KeyZ', 'KeyY'].includes(e.code))) e.preventDefault();
    });
    window.addEventListener('keyup', (e) => { this.keys.delete(e.code); });
    window.addEventListener('blur', () => { this.keys.clear(); this.mouseHeld = [false, false, false]; });

    const toVirtual = (e) => {
      const r = canvas.getBoundingClientRect();
      this.mouse = V((e.clientX - r.left) / r.width * K.ScreenW, (e.clientY - r.top) / r.height * K.ScreenH);
    };
    canvas.addEventListener('pointermove', toVirtual);
    canvas.addEventListener('pointerenter', (e) => { this.inside = true; toVirtual(e); });
    canvas.addEventListener('pointerleave', () => { this.inside = false; });
    canvas.addEventListener('pointerdown', (e) => {
      this.onGesture?.();
      toVirtual(e);
      const b = e.button === 1 ? 1 : e.button === 2 ? 2 : 0;
      this.mouseHeld[b] = true;
      this.mousePressedArr[b] = true;
      canvas.setPointerCapture?.(e.pointerId);
      e.preventDefault();
    });
    window.addEventListener('pointerup', (e) => {
      const b = e.button === 1 ? 1 : e.button === 2 ? 2 : 0;
      this.mouseHeld[b] = false;
    });
    canvas.addEventListener('contextmenu', (e) => e.preventDefault());
    canvas.addEventListener('wheel', (e) => {
      this.wheelX += Math.sign(e.deltaX); this.wheelY += Math.sign(e.deltaY);
      e.preventDefault();
    }, { passive: false });
  },
};
