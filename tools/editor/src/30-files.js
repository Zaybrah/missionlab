// ------------------------------------------------------------------ files in, file out
$('openBtn').addEventListener('click', () => $('fileInput').click());
$('fileInput').addEventListener('change', ev => { openFiles([...ev.target.files]); ev.target.value = ''; });
let dragDepth = 0;
window.addEventListener('dragenter', ev => { ev.preventDefault(); dragDepth++; document.body.classList.add('dragging'); });
window.addEventListener('dragleave', () => { if (--dragDepth <= 0) { dragDepth = 0; document.body.classList.remove('dragging'); } });
window.addEventListener('dragover', ev => ev.preventDefault());
window.addEventListener('drop', ev => { ev.preventDefault(); dragDepth = 0; document.body.classList.remove('dragging'); if (ev.dataTransfer && ev.dataTransfer.files.length) openFiles([...ev.dataTransfer.files]); });

let saver = null;      // (name, text) => Promise; set when this view can hand a file to the viewer
if (location.protocol === 'file:') saver = async (name, text) => {
  const a = document.createElement('a'); a.href = URL.createObjectURL(new Blob([text], { type: 'application/json' })); a.download = name;
  document.body.appendChild(a); a.click(); a.remove(); setTimeout(() => URL.revokeObjectURL(a.href), 4000);
};
else if (window.claude && typeof window.claude.use === 'function') window.claude.use('downloads').then(d => { if (d) { saver = (name, text) => d.save({ filename: name, data: text }); $('save').hidden = false; } }).catch(() => {});
$('save').hidden = !saver;
// An inserted module with an outside reference still to bind is not a valid scenario yet (ML-019): nothing leaves the page.
const exportHeld = () => { const held = moduleBlock(); if (held) $('saveMsg').textContent = held; return held !== ''; };
async function saveFile() {
  if (exportHeld()) return;
  if (live.on) { await liveCommand('save'); $('saveMsg').textContent = 'Saved in BepInEx/MissionLab/scenarios. It loads the next time you host this map.'; return; }
  // Each save counts one revision; the id is made on the first save (scenarioObject) and kept from then on.
  state.revision++;
  try { await saver(fileName(), scenarioText()); $('saveMsg').textContent = 'Saved as ' + fileName() + ' (revision ' + state.revision + ').'; markSaved(fileName()); changed(); }
  catch (err) { state.revision--; $('saveMsg').textContent = err && err.code === 'declined' ? 'Not saved.' : 'Saving is not possible here. Use Copy file text and paste it into a new file named ' + fileName() + '.'; }
}
$('save').addEventListener('click', saveFile);
async function copyFile() {
  if (exportHeld()) return;
  const text = scenarioText();
  try { await navigator.clipboard.writeText(text); $('saveMsg').textContent = 'Copied. Paste it into a new text file named ' + fileName() + '.'; }
  catch (err) {
    const box = $('fileText'); box.closest('details').open = true; box.focus(); box.select();
    let ok = false; try { ok = document.execCommand('copy'); } catch (e) { /* not allowed here */ }
    $('saveMsg').textContent = ok ? 'Copied. Paste it into a new text file named ' + fileName() + '.' : 'The text is selected below. Press Ctrl+C, then paste it into a new file named ' + fileName() + '.';
  }
}
$('copy').addEventListener('click', copyFile);

