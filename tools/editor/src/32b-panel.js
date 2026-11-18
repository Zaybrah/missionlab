// ------------------------------------------------------------------ the inspector: folding sections, long explanations
// Every section of the column has one header band, and a click on it folds the section away, as an engine's inspector
// does. What is folded is remembered in this browser. Sections that someone new to the page does not need start folded,
// one click away; the selected enemy, the selected door, saving and the core of Play and Direct never fold.
const FOLD_KEY = 'missionlab-folded';
const FOLD_NEVER = ['panel', 'doorPanel', 'saveBox', 'playBox', 'directBox'];
const FOLD_FIRST = ['placesBox', 'squadSection', 'placementBox', 'waveSection', 'moduleBox', 'rhSection', 'sealSection', 'optBox', 'cardsAuthor', 'revBox', 'unusedBox', 'packBox'];
function foldsKept() {
  try { const o = JSON.parse(localStorage.getItem(FOLD_KEY) || '{}'); return o && typeof o === 'object' ? o : {}; } catch (e) { return {}; }
}
function startFolds(served) {
  const kept = foldsKept();
  // The quick mission needs the game; offline it starts folded.
  const first = served ? FOLD_FIRST : FOLD_FIRST.concat('quickBox');
  for (const section of document.querySelectorAll('#side > section')) {
    const h = section.firstElementChild;
    if (!h || h.tagName !== 'H2' || h.classList.contains('vh') || FOLD_NEVER.includes(section.id)) continue;
    const key = section.id || h.id, b = document.createElement('button');
    b.type = 'button'; b.className = 'fold';
    while (h.firstChild) b.appendChild(h.firstChild);
    h.appendChild(b);
    const put = shut => { section.classList.toggle('shut', shut); b.setAttribute('aria-expanded', String(!shut)); };
    put(key in kept ? kept[key] === true : first.includes(key));
    b.addEventListener('click', () => {
      const shut = !section.classList.contains('shut'), all = foldsKept(); put(shut); all[key] = shut;
      try { localStorage.setItem(FOLD_KEY, JSON.stringify(all)); } catch (e) { /* storage unavailable */ }
    });
  }
}

// A long explanation shows its first sentence and a More button; the rest follows word for word. Only fixed texts are
// shortened, never one the page writes while it runs, and never inside a <code> or <b>.
function foldHints() {
  for (const p of document.querySelectorAll('#side p.hint:not([id]), #rhAbout, #objectiveHint')) {
    const total = p.textContent.length;
    let seen = 0, cut = null;
    for (const node of [...p.childNodes]) {
      if (node.nodeType === 3) {
        const end = /[.!?](?=\s)/g; let m;
        while ((m = end.exec(node.data))) if (seen + m.index >= 40) { cut = { node, at: m.index + 1 }; break; }
        if (cut) break;
      }
      seen += node.textContent.length;
    }
    if (!cut || total - seen - cut.at < 50) continue;
    const more = document.createElement('span'), b = document.createElement('button');
    more.hidden = true;
    for (let n = cut.node.splitText(cut.at); n;) { const next = n.nextSibling; more.appendChild(n); n = next; }
    b.type = 'button'; b.className = 'more'; b.textContent = 'More'; b.setAttribute('aria-expanded', 'false');
    b.addEventListener('click', () => { more.hidden = !more.hidden; b.textContent = more.hidden ? 'More' : 'Less'; b.setAttribute('aria-expanded', String(!more.hidden)); });
    p.append(more, b);
  }
}
