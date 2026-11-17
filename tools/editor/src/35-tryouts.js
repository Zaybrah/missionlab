// ---------------------------------------------------------------- try-outs: one-click experiments in a running mission
const TRY_HINTS = {
  message: 'Shows a yellow notice on every player\'s screen. It tells us whether the mod can talk to friends who have no mod.',
  'power:off': 'Switches off the map\'s breaker box and the lights wired to it. Clean House has one; it shows best at night.',
  'power:on': 'Switches the breaker box back on.',
  walk: 'Gives every enemy that can walk a route of one stop: the spot where you stand. In a run they came, but only set off some twenty seconds later; they are now told to leave at once.',
  doors: 'Tells every door on the map to open. Seen to work.',
  sides: 'Moves every second enemy to another team. Watch whether the two sides start to fight each other.',
  thing: 'Places it where the selected enemy marker is, or 6 m in front of you when no marker is selected. Two seconds later the game reports whether it is still there. A rifle or the chair is the easiest to see; the flashbang and the landmine of the last run could not be seen.',
  respawn: 'Asks the game to bring every player back. It brought a dead player back in a run; it did nothing to a living one.',
  fail: 'Tells every player the mission has failed and, five seconds later, ends the operation: everyone goes back to the lobby, without a result screen.',
  endop: 'Calls the game\'s own "end operation": everyone goes back to the lobby, with no win and no loss shown.',
  complete: 'Ends the mission as if the team had extracted. Whether that run is marked as cheated is the FairPlay setting in the config. Keep it for last.',
};
const THINGS_FIRST = ['AKM', 'Chair_Leather_Interactable', 'Hard Drive', 'Landmine', 'Proximity Explosive_IED '];
function syncTry() {
  const box = $('tryBox'); if (!box) return;
  box.hidden = !(live.on && live.mission);
  if (box.hidden) return;
  const what = $('tryWhat').value.split(':')[0];
  $('tryTextRow').hidden = what !== 'message'; $('tryThingRow').hidden = what !== 'thing';
  $('tryHint').textContent = TRY_HINTS[$('tryWhat').value] || '';
  const sel = $('tryThing'), names = (live.things || []).slice().sort((a, b) => (THINGS_FIRST.indexOf(a) < 0 ? 99 : THINGS_FIRST.indexOf(a)) - (THINGS_FIRST.indexOf(b) < 0 ? 99 : THINGS_FIRST.indexOf(b)) || a.localeCompare(b));
  const key = names.join('|');
  if (sel.dataset.key !== key) {
    const keep = sel.value; sel.dataset.key = key; sel.textContent = '';
    for (const n of names) { const o = document.createElement('option'); o.value = n; o.textContent = n.trim(); sel.appendChild(o); }
    if (names.indexOf(keep) >= 0) sel.value = keep;
  }
  if (!syncTry.asked) $('tryNote').textContent = live.tried ? 'The game says: ' + live.tried : '';
}
function tryOut() {
  const parts = $('tryWhat').value.split(':'), what = parts[0];
  const more = { what, arg: what === 'message' ? $('tryText').value : what === 'thing' ? $('tryThing').value : (parts[1] || '') };
  const e = state.enemies[state.selected];
  if (what === 'thing' && e) more.pos = e.pos.slice(0, 3);
  syncTry.asked = true; $('tryNote').textContent = 'Asked the game...';
  return liveCommand('try', more).then(() => { syncTry.asked = false; syncTry(); });
}
$('tryWhat').addEventListener('change', syncTry);
$('tryGo').addEventListener('click', tryOut);

window.missionlabEditor = { readyRows, readyReport, scenarioProblems, tagsOf, doorOf, zonesOut, syncTry, tryOut, missionCheck, eitherSets, selectThing, thingsOut, state, live, view3d, openFiles, scenarioText, fileName, groundAt, levelsAt, toScreen, fit, setMode, click3d, selectDoor, doorLine, showAt, replay, loadReplay, showTab, canSee, dangers, picked, setSquad, placeSquad, squadData, addWaves };   // for automated tests
})();
