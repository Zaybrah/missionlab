// ------------------------------------------------------------------ view
const dpr = () => window.devicePixelRatio || 1;
const size = () => ({ w: canvas.clientWidth, h: canvas.clientHeight });
const toScreen = (x, z) => { const { w, h } = size(), v = state.view; return [w / 2 + (x - v.cx) * v.scale, h / 2 - (z - v.cz) * v.scale]; };
const toWorld = (sx, sy) => { const { w, h } = size(), v = state.view; return [v.cx + (sx - w / 2) / v.scale, v.cz - (sy - h / 2) / v.scale]; };
function fit() {
  if (state.mode === '3d') { view3d.home(); return; }
  let b = null;
  if (state.map) b = state.map.bounds;
  else if (state.enemies.length) {
    b = { minX: Infinity, maxX: -Infinity, minZ: Infinity, maxZ: -Infinity };
    for (const e of state.enemies) for (const p of [e.pos, ...e.patrol]) { b.minX = Math.min(b.minX, p[0] - 15); b.maxX = Math.max(b.maxX, p[0] + 15); b.minZ = Math.min(b.minZ, p[2] - 15); b.maxZ = Math.max(b.maxZ, p[2] + 15); }
  }
  if (!b) return;
  const { w, h } = size();
  state.view.cx = (b.minX + b.maxX) / 2; state.view.cz = (b.minZ + b.maxZ) / 2;
  state.view.scale = Math.max(0.15, Math.min(80, Math.min((w - 48) / Math.max(1, b.maxX - b.minX), (h - 130) / Math.max(1, b.maxZ - b.minZ))));
  invalidate();
}
function zoomAt(sx, sy, factor) {
  const [wx, wz] = toWorld(sx, sy), v = state.view;
  v.scale = Math.max(0.15, Math.min(80, v.scale * factor));
  const { w, h } = size();
  v.cx = wx - (sx - w / 2) / v.scale; v.cz = wz + (sy - h / 2) / v.scale;
  invalidate();
}

