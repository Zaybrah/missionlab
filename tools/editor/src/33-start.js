// ------------------------------------------------------------------ start
readTokens();
new MutationObserver(readTokens).observe(document.documentElement, { attributes: true, attributeFilter: ['data-theme'] });
matchMedia('(prefers-color-scheme: dark)').addEventListener('change', readTokens);
if (document.fonts && document.fonts.ready) document.fonts.ready.then(invalidate);
new ResizeObserver(invalidate).observe(canvas);
const served = /^https?:$/.test(location.protocol) && /^(localhost|127\.0\.0\.1)$/.test(location.hostname);
if (served) startLive(); else if (!restoreDraft()) loadExample();
startFolds(served); foldHints();
setTool('select'); syncSide();
{ let tab = 'map'; try { tab = localStorage.getItem('missionlab-tab') || (served ? 'enemies' : 'map'); } catch (e) { /* storage unavailable */ } showTab(window.__mlAllTabs ? 'all' : ['map', 'enemies', 'triggers', 'doors', 'mission'].includes(tab) ? tab : 'map'); }
startViews();
requestAnimationFrame(fit);
