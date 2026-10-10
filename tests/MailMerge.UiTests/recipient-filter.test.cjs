const {test} = require('node:test');
const assert = require('node:assert/strict');
const {readFileSync} = require('node:fs');
const {JSDOM} = require('jsdom');
const html = readFileSync(`${__dirname}/../../src/MailMerge.Api/wwwroot/index.html`, 'utf8');

async function editor(t, overrides = {}, listColumns = ['Email', 'RSVP', 'SEND_EMAIL']) {
  const dom = new JSDOM(html.replace('start().catch(e=>toast(e.message,true));', ''), {runScripts: 'dangerously', url: 'http://mailmerge.test'});
  t.after(() => dom.window.close());
  const w = dom.window, writes = [], actions = [];
  const campaign = {id: 1, name: 'Test', status: 'idle', listId: 1, subject: 'Hello', body: 'Welcome', attachments: [], counts: {}, ...overrides};
  w.testApi = async (method, url, body) => {
    if (method === 'PUT') {writes.push(structuredClone(body)); Object.assign(campaign, body); return campaign;}
    if (method === 'POST') {actions.push(url); return {message: 'Test sent'};}
    if (url === '/api/accounts') return [];
    if (url === '/api/lists') return [{id: 1, name: 'Guests', count: 4, columns: listColumns}];
    if (url.endsWith('/plan')) return {plan: {contacts: 4, toSend: 4, toDraft: 4, skipped: 0, filteredOut: 0}, supportsDrafts: true};
    return campaign;
  };
  // Flush the actual autosave callback explicitly, avoiding timer-dependent assertions.
  const setTimeout = w.setTimeout.bind(w);
  w.setTimeout = (fn, ms, ...args) => {
    if (ms === 700) {w.flushSave = () => fn(...args); return 0;}
    return setTimeout(fn, ms, ...args);
  };
  w.eval(`api=testApi; WS={user:{},workspace:{retentionDays:90}};`);
  await w.editEmail(1);
  return {w, writes, actions, $: s => w.document.querySelector(s)};
}

test('filter controls open without a sending account, use arbitrary columns, and save AND/OR', async t => {
  const {w, $, writes} = await editor(t, {}, ['Email', 'Attending?', 'Please send']);
  assert.ok($('#faddfirst'));
  $('#faddfirst').onclick();
  assert.equal($('.fcol').value, 'Attending?');
  $('#fadd').onclick();
  const second = w.document.querySelectorAll('.fcol')[1];
  second.value = 'Please send'; second.onchange({target: second});
  await w.flushSave();
  assert.equal(writes.at(-1).filter.match, 'all');
  assert.equal(writes.at(-1).filter.conditions.length, 2);
  $('#fmatch').value = 'any'; $('#fmatch').onchange({target: $('#fmatch')});
  await w.flushSave();
  assert.equal(writes.at(-1).filter.match, 'any');
  $('#fclear').onclick({preventDefault(){}}); await w.flushSave();
  assert.equal(writes.at(-1).filter.conditions.length, 0);
});

test('unfinished comparison does not clear restrictions or allow a send', async t => {
  const {w, $, writes, actions} = await editor(t);
  $('#faddfirst').onclick(); await w.flushSave();
  const before = writes.length;
  $('.fop').value = 'equals'; $('.fop').onchange({target: $('.fop')});
  await w.flushSave();
  assert.equal(writes.length, before);
  assert.equal($('#send').disabled, true);
  await $('#test').onclick();
  assert.equal(actions.length, 0);
  $('.fval').value = 'TRUE'; $('.fval').oninput({target: $('.fval')});
  await w.flushSave();
  assert.equal(writes.at(-1).filter.conditions[0].value, 'TRUE');
});

test('a visible default list offers filters even before it is saved on the campaign', async t => {
  const {$} = await editor(t, {listId: null});
  assert.ok($('#faddfirst'));
});

test('saved rules referencing removed columns block sending', async t => {
  const {$} = await editor(t, {filter: {match:'all', conditions:[{column:'Removed', operator:'isNo'}]}});
  assert.equal($('#send').disabled, true);
  assert.match($('#filterbox').textContent, /Removed/);
});

test('failed autosave keeps sending blocked and the next edit can save successfully', async t => {
  const {w, $, writes} = await editor(t);
  const originalApi = w.api;
  w.api = async (method, ...args) => {
    if (method === 'PUT') throw new Error('Save failed');
    return originalApi(method, ...args);
  };
  $('#faddfirst').onclick(); await w.flushSave();
  assert.equal(writes.length, 0);
  assert.equal($('#send').disabled, true);
  assert.match($('#ferror').textContent, /Save failed/);
  w.api = originalApi;
  $('.fcol').value='SEND_EMAIL'; $('.fcol').onchange({target:$('.fcol')});
  await w.flushSave();
  assert.equal(writes.at(-1).filter.conditions[0].column, 'SEND_EMAIL');
  assert.equal($('#send').disabled, false);
});

test('import makes replacing blank flags an explicit choice', async t => {
  const {w, $} = await editor(t);
  w.showImport({sheets:['Guests'], sheet:'Guests', headers:['Email','SEND_EMAIL'], emailColumn:'Email', sample:[], rowCount:4});
  assert.equal($('#blankoption').hidden, true);
  $('#upd').checked=true; $('#upd').onchange();
  assert.equal($('#blankoption').hidden, false);
  assert.equal($('#replaceblanks').checked, true);
  assert.match($('#box').textContent, /Recipient rules/);
});
