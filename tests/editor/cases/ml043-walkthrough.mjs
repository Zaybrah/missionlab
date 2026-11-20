// ML-043: the inside/outside probe agrees with the shared membership fixture point for point and says what decided it; a
// two-player walkthrough with an injected event becomes exactly the saved test in examples/rehearsals; a fast stretch
// through a thin room is reported, not assumed; offline the trace is exported and labelled as not run; and two answers
// to one script name their first difference.
import fs from 'node:fs';
import path from 'node:path';

export default async function (h) {
  const { script, openEditor, load, check, ROOT, FIXTURES } = h;
  const read = rel => fs.readFileSync(path.join(ROOT, rel), 'utf8').replace(/\r\n/g, '\n');
  const text = JSON.stringify;
  const house = read('examples/rehearsals/painted_house.json');
  const twoPlayers = () => [
    { points: [{ t: 0, pos: [4, 1, 4] }], down: null },
    { points: [{ t: 0, pos: [17, 0.9, 3] }, { t: 2, pos: [17, 2.9, 5] }, { t: 4, pos: [17, 4.9, 8] }, { t: 5, pos: [14, 4.9, 16] }], down: null },
  ];
  const twoRows = () => [{ at: 1.25, kind: 'alert' }, { at: 1.5, kind: 'fired', id: 3 }, { at: 3, kind: 'phase', id: 2, phase: 'holding' }, { at: 6, kind: 'fired', id: 2 }];

  {   // Every point of the shared fixture: the probe's answer is the fixture's, areaContains's and membershipInside's.
    const api = openEditor(script).api, fx = JSON.parse(fs.readFileSync(path.join(FIXTURES, 'area_points.json'), 'utf8'));
    let all = 0;
    const wrong = [];
    for (const a of fx.areas) {
      const area = api.areaRead(a.area), t = { id: 1, when: 'zone', pos: [0, 0, 0], radius: 1, area };
      for (const pt of a.points) {
        all++;
        const e = api.membershipExplain(t, pt.p);
        if (!(e.inside === pt.in && e.inside === api.areaContains(area, pt.p) && e.inside === api.membershipInside(t, pt.p) && e.kind === 'area')) wrong.push(a.name + ' ' + text(pt.p) + ': ' + e.why);
      }
    }
    check('the probe agrees with the shared fixture and the inside test at all ' + all + ' points', !wrong.length && all >= 35, wrong.join(' | '));
    const at = (name, p) => { const a = fx.areas.find(x => x.name === name); return api.membershipExplain({ id: 1, when: 'zone', area: api.areaRead(a.area) }, p); };
    const fails = e => e.parts.map(q => q.fail).join(',');
    {
      const e = at('courtyard house', [10.02, 1, 15]);
      check('0.02 m into the courtyard: outside because hole 1 of part 1 leaves it out', !e.inside && e.hole === 0 && fails(e) === 'hole' && /hole 1/.test(e.why), e.why);
      const edge = at('courtyard house', [10.005, 1, 15]);
      check('0.005 m into the courtyard: inside, said to be on a hole\'s edge', edge.inside && edge.part === 0 && edge.edge && /hole/.test(edge.why), edge.why);
      const wall = at('courtyard house', [30.005, 1, 15]), out = at('courtyard house', [30.02, 1, 15]);
      check('0.005 m outside the outer wall is on the outline\'s edge; 0.02 m is off the outline', wall.inside && wall.edge && /outline/.test(wall.why) && !out.inside && fails(out) === 'outline', wall.why + ' | ' + out.why);
    }
    {
      const top = at('a band of heights', [5, 4, 5]), over = at('a band of heights', [5, 4.001, 5]), under = at('a band of heights', [5, 0.999, 5]);
      check('exactly at the top of the band is inside; 1 mm above fails the top bound, 1 mm below the bottom one', top.inside && !over.inside && fails(over) === 'above' && /ends at 4 m/.test(over.why) && !under.inside && fails(under) === 'below' && /starts at 1 m/.test(under.why), over.why + ' | ' + under.why);
    }
    {
      const name = 'two-floor house round a courtyard, with stairs';
      const stairs = at(name, [17, 2.9, 5]), gap = at(name, [4, 2.9, 4]), air = at(name, [10, 4, 10]);
      check('halfway up the stairs, between the floor bands, the stair part (part 3) takes it', stairs.inside && stairs.part === 2 && /part 3/.test(stairs.why), stairs.why);
      check('between the bands away from the stairs each part names its own reason', !gap.inside && fails(gap) === 'above,below,outline', fails(gap) + ' | ' + gap.why);
      check('above the courtyard: the ground floor is too low, the upper floor\'s hole leaves it out', !air.inside && fails(air) === 'above,hole,outline' && air.hole === 0, fails(air));
    }
    {   // An old circle: distance across against the radius, and 2 m up or down.
      const c = { id: 2, when: 'hold', pos: [0, 1, 0], radius: 5 };
      const cases = [[[3, 1, 4], true], [[3.01, 1, 4], false], [[0, 3, 0], true], [[0, 3.01, 0], false], [[0, -1, 0], true], [[0, -1.01, 0], false]];
      const bad = cases.filter(([p, inside]) => { const e = api.membershipExplain(c, p); return e.kind !== 'circle' || e.inside !== inside || e.inside !== api.membershipInside(c, p); });
      check('the legacy circle: the probe agrees with the inside test on the radius and at exactly 2 m up and down', !bad.length, text(bad));
      const e = api.membershipExplain(c, [3, 1, 4]), floor = api.membershipExplain(c, [0, 3.01, 0]);
      check('the circle\'s answer gives the distance against the radius, and names another floor', Math.abs(e.distance - 5) < 1e-9 && /5 m from the anchor across, the radius is 5 m/.test(e.why) && /another floor/.test(floor.why), e.why + ' | ' + floor.why);
    }
  }

  {   // Two test players and an injected alarm become the saved test, step for step, every move at a half second.
    const page = await load(script, 'painted_house.json', house), api = page.api, s = api.state;
    check('the painted house opens with its three triggers', s.zones.filter(z => z.id > 0).length === 3, text(s.zones.map(z => z.id)));
    s.walk.players = twoPlayers();
    const rows = twoRows();
    check('the walkthrough has nothing that stops it', api.walkProblems(s.walk, rows).length === 0, text(api.walkProblems(s.walk, rows)));
    const got = api.walkScript(s.walk, rows, { name: 'two players in different parts of the house', players: 2 });
    const saved = read('examples/rehearsals/sc-painted-house/two_players_in_one_house.json').trimEnd(), written = api.rehearseText(got.script);
    check('the two-player walk with an alarm converts to exactly the saved test', written === saved, written);
    const moves = got.script.steps.filter(st => st.players);
    check('every move is sampled at a half second, both players each time', moves.length === 11 && moves.every(st => st.at * 2 === Math.round(st.at * 2) && st.players.length === 2), text(moves.map(st => st.at)));
    check('the second player is on the landing at 2 s, where only the stair part counts', text(moves.find(st => st.at === 2).players[1]) === '[17,2.9,5]', text(moves));
    check('the alarm keeps its own time, between the samples', got.script.steps.findIndex(st => st.event === 'alert') === 3 && got.script.steps[3].at === 1.25, text(got.script.steps.slice(2, 5)));
    check('each step knows the row it came from, and samples belong to none', text(got.sources.filter(x => x.row).map(x => x.row)) === '[1,2,3,4]' && got.sources.filter(x => x.sample).length === 11, text(got.sources));
    check('no stretch of this walk goes through a place unseen', api.walkCrossings(s.walk, s.zones).length === 0, text(api.walkCrossings(s.walk, s.zones)));
    const back = api.walkFromRows(api.rehearseRows(JSON.parse(saved)));
    const again = back && api.rehearseText(api.walkScript({ players: back.players }, back.rest, { name: 'two players in different parts of the house', players: 2 }).script);
    check('the saved test turned back into paths converts to the same text', again === saved, again || 'not turned into paths');
    // A failure in a sampled walk is put on the row it came from, not on the row with the same number as the step.
    s.rehearse.rows = rows; s.rehearse.answerSources = got.sources; s.rehearse.prev = null;
    s.rehearse.answer = { passed: false, refused: '', blocked: '', assumptions: '', steps: [{ at: 6, what: "step 15 at 6 s: trigger 2 'Hold the house' has fired", expected: 'fired', actual: 'not fired: Waiting', ok: false }], looks: [] };
    api.rhShow();
    check('the failed check of step 15 marks row 4, the row it came from', text([...s.rehearse.failed]) === '[4]', text([...s.rehearse.failed]));
  }

  {   // A fast stretch through a thin room between two samples is reported; one with a sample on the room is not.
    const api = openEditor(script).api;
    const room = { id: 7, name: 'Corridor', when: 'zone', pos: [10.25, 0, 5], radius: 1, area: api.areaRead({ parts: [{ floor: [0, 3], outline: [[10, 0], [10.5, 0], [10.5, 10], [10, 10]] }] }) };
    const walk = pts => ({ players: [{ points: pts.map(([t, pos]) => ({ t, pos })), down: null }] });
    const fast = walk([[0, [4, 1, 5]], [0.5, [15, 1, 5]]]);
    const seen = api.walkSamples(fast).map(x => api.membershipInside(room, x.players[0]));
    check('neither sample of the fast stretch is in the 0.5 m room', text(seen) === '[false,false]', text(seen));
    const w = api.walkCrossings(fast, [room]);
    check('the fast stretch through the thin room is reported once, between 0 s and 0.5 s, inside the room', w.length === 1 && w[0].id === 7 && w[0].from === 0 && w[0].to === 0.5 && !w[0].leaves && w[0].at[0] >= 10 && w[0].at[0] <= 10.5, text(w));
    check('the warning asks for a waypoint', w.length === 1 && /passes through trigger 7 between 0 s and 0.5 s/.test(api.walkCrossingText(w[0], id => 'trigger ' + id)) && /add a waypoint near \(10\.\d, 5\.0\)/.test(api.walkCrossingText(w[0], id => 'trigger ' + id)), w.length ? api.walkCrossingText(w[0], id => 'trigger ' + id) : '');
    check('walked slowly, a sample lands on the room and nothing is reported', api.walkCrossings(walk([[0, [4, 1, 5]], [5.5, [15, 1, 5]]]), [room]).length === 0);
    check('passing over the room above its floor band is not reported', api.walkCrossings(walk([[0, [4, 5, 5]], [0.5, [15, 5, 5]]]), [room]).length === 0);
    check('a jump (two waypoints at one time) across the room is not walked, so not reported', api.walkCrossings(walk([[0, [4, 1, 5]], [0.25, [4, 1, 5]], [0.25, [15, 1, 5]]]), [room]).length === 0);
    const bend = api.walkCrossings(walk([[0, [4, 1, 5]], [0.25, [10.2, 1, 5]], [0.5, [4, 1, 6]]]), [room]);
    check('a waypoint inside the room between two samples outside it is reported', bend.length === 1 && bend[0].from === 0 && bend[0].to === 0.5, text(bend));
    const circle = { id: 8, when: 'zone', pos: [10, 0, 5], radius: 0.4 };
    check('a fast stretch through an old circle is reported too', api.walkCrossings(fast, [circle]).length === 1);
    const page = await load(script, 'painted_house.json', house);
    const across = page.api.walkCrossings(walk([[0, [4, 1, 10]], [0.5, [16, 1, 10]]]), page.api.state.zones);
    check('crossing the courtyard between two samples in the house is reported as leaving and coming back', across.length === 2 && across.every(c => c.leaves && c.at[0] > 8 && c.at[0] < 12), text(across));
  }

  {   // Offline nothing runs: the trace is exported and the result says it has not run.
    const page = await load(script, 'painted_house.json', house), api = page.api, s = api.state;
    api.live.on = false;
    s.walk.players = twoPlayers(); s.rehearse.rows = twoRows(); s.rehearse.players = 2;
    await api.rhRun();
    const rep = s.rehearse.report;
    check('offline Run exports the trace and labels it not run', rep && rep.level === 'not-run' && /^Not run/.test(rep.title) && /exported/.test(rep.title) && s.rehearse.answer === null, text(rep));
    check('an offline export claims no pass or failure', rep && !/passed|Passed|Failed/.test(rep.title.replace('nothing passed or failed', '')) && s.rehearse.failed.size === 0, rep && rep.title);
  }

  {   // Two answers to one script: the first moment or step that differs, after the house's front wall was moved.
    const api = openEditor(script).api;
    const stand = (n, at) => ({ at, what: 'step ' + n + ' at ' + at + ' s: 1 living player stands where the script says', expected: '', actual: '', ok: true });
    const answer = fired => ({
      passed: fired, steps: [stand(1, 0), stand(2, 2.5), stand(3, 3),
        { at: 3, what: "step 4 at 3 s: trigger 1 'House' has fired", expected: 'fired', actual: fired ? 'fired' : 'not fired: Waiting for a player to step inside', ok: fired },
        stand(5, 4), { at: 4, what: "step 6 at 4 s: trigger 2 'Hold the house' is holding", expected: 'holding', actual: 'holding: A player is inside', ok: true }],
      looks: [
        { at: 0, before: { 1: 'waiting', 2: 'waiting' }, after: { 1: 'waiting', 2: 'waiting' }, inside: { 1: '0/1', 2: '0/1' } },
        { at: 2.5, before: { 1: 'waiting', 2: 'waiting' }, after: { 1: 'waiting', 2: 'waiting' }, inside: { 1: '0/1', 2: '0/1' } },
        { at: 3, before: { 1: 'waiting', 2: 'waiting' }, after: { 1: fired ? 'fired' : 'waiting', 2: fired ? 'holding' : 'waiting' }, inside: { 1: fired ? '1/1' : '0/1', 2: fired ? '1/1' : '0/1' } },
        { at: 4, before: { 1: 'fired', 2: 'holding' }, after: { 1: 'fired', 2: 'holding' }, inside: { 1: '1/1', 2: '1/1' } },
      ],
    });
    const before = answer(true), after = answer(false), name = id => 'trigger ' + id;
    check('the same answer twice has no difference', api.rehearseDivergence(before, answer(true), name) === null);
    const d = api.rehearseDivergence(before, after, name);
    check('after the wall moved, the first difference is the doorway moment at 3 s, step 3, where trigger 1 no longer fires', d && d.at === 3 && d.step === 3 && d.index === 2 && /trigger 1 was fired, now waiting/.test(d.text), text(d));
    const old = x => Object.assign({}, x, { looks: undefined });
    const e = api.rehearseDivergence(old(before), old(after), name);
    check('without moments (an older plugin) the first differing step is named: step 4, the doorway check', e && e.at === 3 && e.step === 4 && /before: as expected \(fired\); now: not as expected/.test(e.text), text(e));
    const early = answer(false);
    early.looks[1] = { at: 2.5, before: { 1: 'waiting', 2: 'waiting' }, after: { 1: 'waiting', 2: 'holding' }, inside: { 1: '0/1', 2: '1/1' } };
    const f = api.rehearseDivergence(before, early, name);
    check('a phase that changes earlier than any check is the first difference: 2.5 s, step 2, trigger 2', f && f.at === 2.5 && f.step === 2 && /trigger 2 was waiting, now holding/.test(f.text), text(f));
    check('next event skips moves that change nothing and stops at the doorway and the checks', text(api.rehearseEvents(before)) === '[2,3,5]', text(api.rehearseEvents(before)));
    const words = api.rehearseLookWords(api.rehearseLook(before, 3), name);
    check('the state before and after the doorway moment says what changed and who is inside', words.changed[0] === 'trigger 1: waiting → fired (1 of 1 living player inside)' && words.changed.length === 2 && words.same.length === 0, text(words));
    // Through the result view: the run before is kept, the difference is found and its step can be chosen.
    const s = api.state;
    s.rehearse.prev = before; s.rehearse.answer = after; s.rehearse.answerSources = null; s.rehearse.cursor = -1;
    api.rhShow();
    check('the result view names the first difference and reports a failure', s.rehearse.divergence && s.rehearse.divergence.step === 3 && s.rehearse.report.level === 'failed', text(s.rehearse.divergence));
    api.rhPick(s.rehearse.divergence.index);
    check('choosing it selects that step and its moment on the map', s.rehearse.cursor === 2 && s.walk.at === 3, s.rehearse.cursor + ' ' + s.walk.at);
  }

  {   // A replay's tracks: positions only, straight runs simplified, names and times kept.
    const api = openEditor(script).api;
    const rec = { schema: 'missionlab-replay/1', players: ['Ann', 'Bob'], frames: [
      [0, [[0, 0, 0, 0, 0]], []], [1, [[0, 1, 0, 0, 0]], []], [2, [[0, 2, 0, 0, 0], [1, 5, 0, 5, 0]], []], [3, [[0, 3, 0, 1, 0]], []], [9, [[0, 3, 0, 1, 0]], []]] };
    const got = api.walkTracks(rec);
    check('a replay gives one path per recorded player, with names and the straight run simplified', got.players.length === 2 && got.players[0].name === 'Ann' && text(got.players[0].points.map(q => q.t)) === '[0,2,3,9]' && got.players[1].points.length === 1 && got.players[1].to === 2 && got.end === 9, text(got));
    let refused = '';
    try { api.walkTracks({ schema: 'missionlab-run/1', frames: [] }); } catch (err) { refused = err.message; }
    check('a file that is not a replay is refused', /not a MissionLab replay/.test(refused), refused);
  }
}
