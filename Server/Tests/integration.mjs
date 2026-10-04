import assert from 'node:assert/strict';

const base = 'http://127.0.0.1:8001/api';
async function call(path, method = 'GET', body, headers = {}) {
  const response = await fetch(base + path, {
    method,
    headers: { 'Content-Type': 'application/json', ...headers },
    body: body === undefined ? undefined : JSON.stringify(body)
  });
  return { status: response.status, data: await response.json() };
}

const key = crypto.randomUUID();
const started = await call('/battles', 'POST', {}, { 'Idempotency-Key': key });
assert.equal(started.status, 201);
const id = started.data.battleId;
assert.equal((await call('/battles', 'POST', {}, { 'Idempotency-Key': key })).data.battleId, id);
assert.equal((await call(`/battles/${id}/score`, 'POST', { name: 'early' })).status, 409);

const first = await call(`/battles/${id}/turns`, 'POST', { turn: 1, action: 'attack' });
assert.equal(first.status, 200);
assert(first.data.attackDamage >= 1 && first.data.attackDamage <= 20);
assert.deepEqual((await call(`/battles/${id}/turns`, 'POST', { turn: 1, action: 'attack' })).data, first.data);
assert.equal((await call(`/battles/${id}/turns`, 'POST', { turn: 1, action: 'guard' })).status, 409);
assert.equal((await call(`/battles/${id}/turns`, 'POST', { turn: 3, action: 'attack' })).status, 409);

let won = null;
for (let battleAttempt = 0; battleAttempt < 40 && !won; battleAttempt++) {
  let state = battleAttempt === 0 ? started.data : (await call('/battles', 'POST', {}, { 'Idempotency-Key': crypto.randomUUID() })).data;
  let next = battleAttempt === 0 ? first.data : null;
  if (next) state.turn = next.nextTurn;
  for (let steps = 0; steps < 30; steps++) {
    if (!next || battleAttempt !== 0) next = (await call(`/battles/${state.battleId}/turns`, 'POST', { turn: state.turn, action: 'attack' })).data;
    if (next.status === 'won') { won = next; break; }
    if (next.status === 'lost') break;
    state.turn = next.nextTurn;
    next = null;
  }
}
assert(won, 'expected at least one winning battle');
const scorePath = `/battles/${won.battleId}/score`;
const score = await call(scorePath, 'POST', { name: '통합검증' });
assert.equal(score.status, 201);
assert.equal(score.data.turns, won.turn);
assert.equal(score.data.remainingHp, won.heroHp);
assert.deepEqual((await call(scorePath, 'POST', { name: '통합검증' })).data, score.data);
assert.equal((await call(scorePath, 'POST', { name: '다른이름' })).status, 409);
const ranking = await call('/scores');
assert.equal(ranking.status, 200);
assert(ranking.data.scores.some(entry => entry.name === '통합검증' && entry.turns === won.turn));
console.log(`API checks passed: battle=${won.battleId}, turns=${won.turn}`);
