// The real browser behind tests/browser/run.mjs: finds Chromium or Microsoft Edge, starts it headless with a throwaway
// profile, and drives it over the Chrome DevTools Protocol with Node's own WebSocket (Node 22 or later, no packages).
// Screenshots are decoded here too, so the checks can look at what the page actually drew.
import fs from 'node:fs';
import os from 'node:os';
import path from 'node:path';
import zlib from 'node:zlib';
import { spawn } from 'node:child_process';

export const sleep = ms => new Promise(done => setTimeout(done, ms));

// ---------------------------------------------------------------- finding and starting the browser
// CHROME_PATH first; then Edge where Windows installs it; then the usual Chromium names on PATH.
export function findBrowser() {
  const tried = [];
  const exists = p => { tried.push(p); try { return fs.statSync(p).isFile(); } catch (e) { return false; } };
  if (process.env.CHROME_PATH) return { exe: exists(process.env.CHROME_PATH) ? process.env.CHROME_PATH : null, tried };
  if (process.platform === 'win32') {
    for (const p of ['C:/Program Files (x86)/Microsoft/Edge/Application/msedge.exe', 'C:/Program Files/Microsoft/Edge/Application/msedge.exe']) if (exists(p)) return { exe: p, tried };
  }
  const dirs = (process.env.PATH || '').split(path.delimiter).filter(Boolean);
  for (const name of ['google-chrome', 'chromium', 'chromium-browser']) {
    for (const dir of dirs) {
      for (const file of process.platform === 'win32' ? [name + '.exe'] : [name]) if (exists(path.join(dir, file))) return { exe: path.join(dir, file), tried };
    }
  }
  return { exe: null, tried };
}

// Starts the browser and connects to it. Throws when it does not come up; close() ends it and removes its profile.
export async function launch(exe, downloads) {
  const profile = fs.mkdtempSync(path.join(os.tmpdir(), 'missionlab-browser-profile-'));
  const args = [
    '--headless=new', '--remote-debugging-port=0', '--user-data-dir=' + profile,
    '--enable-unsafe-swiftshader',                 // WebGL without a graphics card, as in CI
    '--no-first-run', '--no-default-browser-check', '--disable-extensions', '--disable-sync', '--disable-background-networking',
    '--disable-component-update', '--disable-default-apps', '--disable-features=Translate,MediaRouter',
    // The live page polls the fixture on timers; a tab the browser thinks is in the background would be slowed down.
    '--disable-background-timer-throttling', '--disable-renderer-backgrounding', '--disable-backgrounding-occluded-windows',
    '--window-size=1400,900', '--force-device-scale-factor=1',
  ];
  if (typeof process.getuid === 'function' && process.getuid() === 0) args.push('--no-sandbox');      // CI containers run as root
  args.push('about:blank');
  const proc = spawn(exe, args, { stdio: ['ignore', 'ignore', 'pipe'] });
  let stderr = '', exited = null, failed = null;
  proc.stderr.on('data', d => { stderr = (stderr + d).slice(-3000); });
  proc.on('exit', code => { exited = code === null ? 'killed' : code; });
  proc.on('error', e => { failed = e; });
  const closeAll = async cdp => {
    if (cdp) { try { await Promise.race([cdp.send('Browser.close'), sleep(3000)]); } catch (e) { /* it is going anyway */ } cdp.drop(); }
    for (let i = 0; i < 50 && exited === null; i++) await sleep(100);
    if (exited === null) { try { proc.kill(); } catch (e) { /* already gone */ } for (let i = 0; i < 30 && exited === null; i++) await sleep(100); }
    // The browser lets go of its profile a moment after it has exited, on Windows above all.
    try { fs.rmSync(profile, { recursive: true, force: true, maxRetries: 20, retryDelay: 250 }); } catch (e) { return 'the browser profile ' + profile + ' could not be removed: ' + e.message; }
    return '';
  };
  const portFile = path.join(profile, 'DevToolsActivePort');
  let port = 0, wsPath = '';
  for (const until = Date.now() + 30000; Date.now() < until && !port;) {
    if (failed || exited !== null) break;
    try { const [p, w] = fs.readFileSync(portFile, 'utf8').split(/\r?\n/); if (+p > 0 && w) { port = +p; wsPath = w; break; } } catch (e) { /* not written yet */ }
    await sleep(100);
  }
  if (!port) {
    const why = failed ? failed.message : exited !== null ? 'it exited with ' + exited : 'it wrote no DevToolsActivePort within 30 s';
    await closeAll(null);
    throw new Error(exe + ' did not start: ' + why + (stderr.trim() ? '\n' + stderr.trim() : ''));
  }
  let cdp;
  try {
    cdp = await Cdp.connect('ws://127.0.0.1:' + port + wsPath);
    await cdp.send('Browser.setDownloadBehavior', { behavior: 'allowAndName', downloadPath: downloads, eventsEnabled: true });
  } catch (e) { await closeAll(cdp); throw e; }
  return new Browser(cdp, downloads, () => closeAll(cdp), () => { try { proc.kill(); } catch (e) { /* gone */ } try { fs.rmSync(profile, { recursive: true, force: true }); } catch (e) { /* left behind */ } });
}

// ---------------------------------------------------------------- the protocol
class Cdp {
  static connect(url) {
    return new Promise((resolve, reject) => {
      const ws = new WebSocket(url);
      ws.onopen = () => resolve(new Cdp(ws));
      ws.onerror = () => reject(new Error('could not connect to the browser at ' + url));
    });
  }
  constructor(ws) {
    this.ws = ws; this.next = 1; this.calls = new Map(); this.listeners = [];
    ws.onmessage = ev => this.receive(JSON.parse(typeof ev.data === 'string' ? ev.data : Buffer.from(ev.data).toString('utf8')));
    ws.onclose = () => { for (const c of this.calls.values()) c.reject(new Error('the browser connection closed during ' + c.method)); this.calls.clear(); };
  }
  send(method, params, sessionId) {
    const id = this.next++, msg = { id, method, params: params || {} };
    if (sessionId) msg.sessionId = sessionId;
    return new Promise((resolve, reject) => {
      const timer = setTimeout(() => { this.calls.delete(id); reject(new Error('the browser did not answer ' + method + ' within 30 s')); }, 30000);
      this.calls.set(id, { method, resolve: v => { clearTimeout(timer); resolve(v); }, reject: e => { clearTimeout(timer); reject(e); } });
      this.ws.send(JSON.stringify(msg));
    });
  }
  receive(m) {
    if (m.id) {
      const c = this.calls.get(m.id); if (!c) return;
      this.calls.delete(m.id);
      if (m.error) c.reject(new Error(c.method + ': ' + m.error.message)); else c.resolve(m.result);
      return;
    }
    for (const l of this.listeners.slice()) if (l.method === m.method && (l.sessionId === null || l.sessionId === (m.sessionId || ''))) l.fn(m.params || {});
  }
  // sessionId '' listens to the browser itself, null to every session.
  on(method, fn, sessionId) { const l = { method, fn, sessionId: sessionId === undefined ? null : sessionId }; this.listeners.push(l); return () => { this.listeners = this.listeners.filter(x => x !== l); }; }
  drop() { try { this.ws.close(); } catch (e) { /* closed */ } }
}

class Browser {
  constructor(cdp, downloads, close, kill) {
    this.cdp = cdp; this.downloadDir = downloads; this.close = close; this.kill = kill;
    this.names = new Map(); this.done = []; this.waiting = [];
    cdp.on('Browser.downloadWillBegin', p => { this.names.set(p.guid, p.suggestedFilename); }, '');
    cdp.on('Browser.downloadProgress', p => {
      if (p.state !== 'completed' && p.state !== 'canceled') return;
      const got = { name: this.names.get(p.guid) || '', state: p.state, file: path.join(this.downloadDir, p.guid) };
      const w = this.waiting.shift(); if (w) w(got); else this.done.push(got);
    }, '');
  }
  async tab() {
    const { targetId } = await this.cdp.send('Target.createTarget', { url: 'about:blank' });
    const { sessionId } = await this.cdp.send('Target.attachToTarget', { targetId, flatten: true });
    const tab = new Tab(this.cdp, targetId, sessionId);
    await tab.start();
    return tab;
  }
  // The next finished download: { name, text }. Rejects when none finishes in time.
  nextDownload(ms) {
    const ready = this.done.shift();
    const got = ready ? Promise.resolve(ready) : new Promise((resolve, reject) => {
      const w = v => { clearTimeout(timer); resolve(v); };
      const timer = setTimeout(() => { this.waiting = this.waiting.filter(x => x !== w); reject(new Error('no download finished within ' + ms / 1000 + ' s')); }, ms);
      this.waiting.push(w);
    });
    return got.then(d => {
      if (d.state !== 'completed') throw new Error('the download of ' + d.name + ' was cancelled');
      return { name: d.name, text: fs.readFileSync(d.file, 'utf8') };
    });
  }
}

// ---------------------------------------------------------------- one tab
const KEYS = {
  Escape: { key: 'Escape', code: 'Escape', windowsVirtualKeyCode: 27 },
  PageDown: { key: 'PageDown', code: 'PageDown', windowsVirtualKeyCode: 34 },
  PageUp: { key: 'PageUp', code: 'PageUp', windowsVirtualKeyCode: 33 },
  z: { key: 'z', code: 'KeyZ', windowsVirtualKeyCode: 90 },
};
export const CTRL = 2, SHIFT = 8;
const describe = d => (d.exception && (d.exception.description || d.exception.value)) || ((d.text || 'error') + (d.url ? ' at ' + d.url + ':' + (d.lineNumber + 1) : ''));

class Tab {
  constructor(cdp, targetId, sessionId) { this.cdp = cdp; this.targetId = targetId; this.sessionId = sessionId; this.errors = []; this.loads = 0; this.off = []; }
  send(method, params) { return this.cdp.send(method, params, this.sessionId); }
  async start() {
    const on = (m, fn) => this.off.push(this.cdp.on(m, fn, this.sessionId));
    on('Runtime.exceptionThrown', p => this.errors.push('uncaught: ' + describe(p.exceptionDetails || {})));
    on('Log.entryAdded', p => { const e = p.entry || {}; if (e.level === 'error' && !/favicon\.ico$/.test(e.url || '')) this.errors.push('logged: ' + e.text + (e.url ? ' (' + e.url + ')' : '')); });
    on('Page.loadEventFired', () => { this.loads++; });
    for (const m of ['Page.enable', 'Runtime.enable', 'Log.enable', 'DOM.enable']) await this.send(m);
    await this.send('Emulation.setFocusEmulationEnabled', { enabled: true });       // keys reach the page as in a focused window
    await this.send('Page.bringToFront');                                           // a hidden tab gets no animation frames
    // Every page in this tab starts as a first-time visitor: nothing remembered from another check.
    await this.send('Page.addScriptToEvaluateOnNewDocument', { source: 'try { localStorage.clear(); sessionStorage.clear(); } catch (e) {}' });
  }
  async goto(url) {
    const before = this.loads, r = await this.send('Page.navigate', { url });
    if (r.errorText) throw new Error('could not open ' + url + ': ' + r.errorText);
    for (const until = Date.now() + 20000; this.loads === before;) { if (Date.now() > until) throw new Error(url + ' did not finish loading within 20 s'); await sleep(50); }
  }
  // Runs fn(window.missionlabEditor, ...args) in the page and returns its (awaited) value.
  async run(fn, ...args) {
    const expression = '(' + fn + ')(window.missionlabEditor' + args.map(a => ', ' + JSON.stringify(a)).join('') + ')';
    const r = await this.send('Runtime.evaluate', { expression, awaitPromise: true, returnByValue: true });
    if (r.exceptionDetails) throw new Error('in the page: ' + describe(r.exceptionDetails));
    return r.result.value;
  }
  async waitFor(what, ms, fn, ...args) {
    for (const until = Date.now() + ms; ;) {
      const v = await this.run(fn, ...args);
      if (v) return v;
      if (Date.now() > until) throw new Error('waited ' + ms / 1000 + ' s for ' + what);
      await sleep(60);
    }
  }
  async ready() {
    await this.waitFor('the editor to start', 15000, E => !!E && !!E.state && document.readyState === 'complete');
    await this.frames(3);
  }
  frames(n) { return this.run((E, k) => new Promise(done => { const f = () => (--k > 0 ? requestAnimationFrame(f) : done(true)); requestAnimationFrame(f); }), n || 2); }

  // ----- real input
  mouse(type, x, y, o) {
    o = o || {};
    const held = type === 'mousePressed' || (type === 'mouseMoved' && o.held);
    return this.send('Input.dispatchMouseEvent', { type, x, y, modifiers: o.modifiers || 0, pointerType: 'mouse',
      button: type === 'mouseMoved' && !o.held ? 'none' : 'left', buttons: held ? 1 : 0, clickCount: type === 'mouseMoved' ? 0 : 1 });
  }
  async press(p, o) { await this.mouse('mouseMoved', p.x, p.y, o); await this.mouse('mousePressed', p.x, p.y, o); }
  async moveHeld(from, to, steps, o) {
    for (let i = 1; i <= steps; i++) await this.mouse('mouseMoved', from.x + (to.x - from.x) * i / steps, from.y + (to.y - from.y) * i / steps, Object.assign({ held: true }, o));
  }
  release(p, o) { return this.mouse('mouseReleased', p.x, p.y, o); }
  async drag(from, to, o) { await this.press(from, o); await this.moveHeld(from, to, (o && o.steps) || 6, o); await this.release(to, o); await this.frames(1); }
  async click(p, o) { await this.press(p, o); await this.release(p, o); await this.frames(1); }
  async key(name, modifiers) {
    const k = Object.assign({ modifiers: modifiers || 0, nativeVirtualKeyCode: KEYS[name].windowsVirtualKeyCode }, KEYS[name]);
    await this.send('Input.dispatchKeyEvent', Object.assign({ type: 'rawKeyDown' }, k));
    await this.send('Input.dispatchKeyEvent', Object.assign({ type: 'keyUp' }, k));
    await this.frames(1);
  }
  // Clicks an element where it is on screen, as a person would: refused when it is hidden, disabled or covered.
  async clickOn(selector) {
    const box = await this.run((E, sel) => {
      const el = document.querySelector(sel);
      if (!el) return { error: 'there is no ' + sel };
      el.scrollIntoView({ block: 'center', inline: 'center' });
      const r = el.getBoundingClientRect();
      if (!r.width || !r.height) return { error: sel + ' is not shown' };
      if (el.disabled) return { error: sel + ' is disabled' };
      const x = r.left + r.width / 2, y = r.top + r.height / 2, top = document.elementFromPoint(x, y);
      if (!top || !(top === el || el.contains(top))) return { error: sel + ' is covered by ' + (top ? top.tagName.toLowerCase() + (top.id ? '#' + top.id : '') : 'nothing') };
      return { x, y };
    }, selector);
    if (box.error) throw new Error(box.error);
    await this.click(box);
  }
  // Where map points [x, z] are in the window, through the page's own toScreen. Throws when something covers one.
  async mapAt(list) {
    const got = await this.run((E, pts, id) => {
      const c = document.getElementById(id), r = c.getBoundingClientRect();
      return pts.map(([x, z]) => {
        const [sx, sy] = E.toScreen(x, z), px = r.left + sx, py = r.top + sy, top = document.elementFromPoint(px, py);
        return { x: px, y: py, on: top === c ? '' : top ? top.tagName.toLowerCase() + (top.id ? '#' + top.id : '') : 'nothing' };
      });
    }, list, 'map');
    got.forEach((p, i) => { if (p.on) throw new Error('map point ' + JSON.stringify(list[i]) + ' is covered by ' + p.on); });
    return got;
  }
  async screenshot() {
    const { data } = await this.send('Page.captureScreenshot', { format: 'png', fromSurface: true });
    return decodePng(Buffer.from(data, 'base64'));
  }
  async close() { for (const off of this.off) off(); try { await this.cdp.send('Target.closeTarget', { targetId: this.targetId }); } catch (e) { /* gone */ } }
}

// ---------------------------------------------------------------- screenshots
// 8-bit RGB or RGBA PNGs, as the browser writes them. px(x, y) gives [r, g, b] of the pixel covering window point (x, y).
export function decodePng(buf) {
  let pos = 8, width = 0, height = 0, depth = 0, type = 0, interlace = 0;
  const idat = [];
  while (pos + 8 <= buf.length) {
    const len = buf.readUInt32BE(pos), kind = buf.toString('ascii', pos + 4, pos + 8), data = buf.subarray(pos + 8, pos + 8 + len);
    if (kind === 'IHDR') { width = data.readUInt32BE(0); height = data.readUInt32BE(4); depth = data[8]; type = data[9]; interlace = data[12]; }
    else if (kind === 'IDAT') idat.push(data);
    else if (kind === 'IEND') break;
    pos += 12 + len;
  }
  if (depth !== 8 || interlace !== 0 || (type !== 2 && type !== 6)) throw new Error('unexpected screenshot format (depth ' + depth + ', colour type ' + type + ', interlace ' + interlace + ')');
  const bpp = type === 6 ? 4 : 3, stride = width * bpp, raw = zlib.inflateSync(Buffer.concat(idat)), out = Buffer.alloc(width * height * 3);
  let prev = Buffer.alloc(stride), line = Buffer.alloc(stride);
  for (let y = 0; y < height; y++) {
    const f = raw[y * (stride + 1)], src = raw.subarray(y * (stride + 1) + 1, (y + 1) * (stride + 1));
    for (let x = 0; x < stride; x++) {
      const a = x >= bpp ? line[x - bpp] : 0, b = prev[x], c = x >= bpp ? prev[x - bpp] : 0;
      let v = src[x];
      if (f === 1) v += a;
      else if (f === 2) v += b;
      else if (f === 3) v += (a + b) >> 1;
      else if (f === 4) { const p = a + b - c, pa = Math.abs(p - a), pb = Math.abs(p - b), pc = Math.abs(p - c); v += pa <= pb && pa <= pc ? a : pb <= pc ? b : c; }
      line[x] = v & 255;
    }
    for (let x = 0; x < width; x++) for (let k = 0; k < 3; k++) out[(y * width + x) * 3 + k] = line[x * bpp + k];
    [prev, line] = [line, prev];
  }
  return {
    width, height,
    px(x, y) { const X = Math.max(0, Math.min(width - 1, Math.floor(x))), Y = Math.max(0, Math.min(height - 1, Math.floor(y))), o = (Y * width + X) * 3; return [out[o], out[o + 1], out[o + 2]]; },
  };
}
