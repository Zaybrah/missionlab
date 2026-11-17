<script>
(() => {
'use strict';
const $ = id => document.getElementById(id);
const canvas = $('map'), ctx = canvas.getContext('2d');
const ROLES = ['Guard', 'Wander', 'Patrol'];
const DRAFT_KEY = 'missionlab-editor-draft-1';

const state = {
  map: null,            // walkable surface, see makeMap()
  navFile: '',          // map file the scenario is meant for (kept even while that map is not open)
  enemies: [],          // {group, role, pos:[x,y,z], yaw, defensive, fixed, wanderDistance, patrolWaitSeconds, patrol:[[x,y,z]], ...}
  selected: -1,
  infils: [],           // insertion points from a recon file
  authored: [],         // the game's own spawn points from a recon file
  title: 'my_scenario',
  scene: '',
  tool: 'select',
  ceiling: Infinity,
  view: { cx: 0, cz: 0, scale: 4 },
  example: true,
  lastRole: 'Guard',
  lastGroup: 0,
  geo: null,            // solid geometry of the map exported from the game: { positions, indices, name }
  zones: [],            // triggers: { id, name, when: zone|alert|intel|time|after, pos, radius, delay, item, after, facePlayer, doors:[{name,pos,locked}] }
  fired: [],            // live: ids of the zones players have already reached
  objectives: [],       // the mission's own intel items: { name, label, pos:[x,y,z], yaw, obtained }
  doors: [],            // the map's doors: { name, pos:[hinge], mid:[x,y,z], locked, enemiesCanOpen (true, false, or null = left to the game), known }
  door: -1,             // the selected door
  squad: [],            // indices of all selected enemies when more than one is selected (the first one clicked stays "selected")
  placing: null,        // a squad waiting for a click on the map to say where it goes
  extractionAfter: 0,   // the extraction stays shut until this trigger has fired (0: the mission's own rule alone)
  insertion: '',        // the insertion every player gets ('': each player's own choice)
  insertions: [],       // the map's insertions by name, from the game or a recon file
  weather: { hour: null, rain: null, sandstorm: null },     // null: left to the game
  respawnAfter: 0, respawnTimes: 0,   // the scenario's respawn rule: seconds a player stays dead (0: the game's rule), and how often
  gameMaster: false,         // the mission is meant to be run by a Game Master
  refused: null,             // the last file that was not loaded, with what is wrong in it
  readNotes: [],             // what was adjusted when the last file was read
  allSkill: '', allNvg: false,   // every enemy of the scenario: 'recruit' or 'veteran', and night vision
  order: null,               // ids of live enemies waiting for a click on the map that tells them where to go
  power: '',                 // 'off' or 'on': the breaker box when the mission starts ('': left to the game)
  things: [],                // weapons and other things the game can place: {name, pos, yaw, trigger}
  thing: -1,                 // the selected one
  thingName: 'AKM',          // what the Add thing tool places
  briefing: [],              // up to four lines shown on every screen when the mission starts
  night: false,              // a night mission: enemies see 25 m, not 50
  insertionSpots: [],        // the map's insertions by name: {name, pos}
  weatherNow: null,     // live: what the map has and shows
  mode: '2d',
};
const undoStack = [];

