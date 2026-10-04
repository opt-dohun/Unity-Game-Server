(() => {
  'use strict';
  const $ = id => document.getElementById(id);
  const endpoint = 'http://127.0.0.1:8001/api';
  let completedRun = null;

  function sorted(scores) {
    return scores.sort((a, b) => a.turns - b.turns || b.remainingHp - a.remainingHp || String(a.createdAt || '').localeCompare(String(b.createdAt || ''))).slice(0, 20);
  }
  async function request(path, options = {}) {
    const controller = new AbortController();
    const timeout = setTimeout(() => controller.abort(), 1800);
    try {
      const response = await fetch(endpoint + path, { ...options, signal: controller.signal });
      if (!response.ok) throw new Error(`HTTP ${response.status}`);
      return await response.json();
    } finally { clearTimeout(timeout); }
  }
  function onFinish(run) {
    completedRun = run.won ? { turns: run.turns, remainingHp: run.remainingHp, battleId: run.battleId } : null;
    $('registerButton').classList.toggle('hidden', !run.won);
  }
  function onReset() {
    completedRun = null;
    $('registration').classList.add('hidden');
    $('ranking').classList.add('hidden');
    $('registerButton').classList.add('hidden');
  }
  function openRegistration() {
    if (!completedRun) return;
    $('registrationSummary').textContent = `${completedRun.turns}턴 · 남은 체력 ${completedRun.remainingHp}%`;
    $('registrationStatus').textContent = '';
    $('registration').classList.remove('hidden');
    $('playerName').focus();
  }
  function closeRegistration() { $('registration').classList.add('hidden'); }
  function closeRanking() { $('ranking').classList.add('hidden'); }
  function render(scores) {
    const list = $('rankingList');
    list.replaceChildren();
    for (const [index, score] of sorted(scores).entries()) {
      const row = document.createElement('li');
      for (const [className, value] of [
        ['rank-number', String(index + 1)], ['rank-name', score.name],
        ['rank-turns', `${score.turns}턴`], ['rank-hp', `${score.remainingHp}%`]
      ]) {
        const span = document.createElement('span');
        span.className = className;
        span.textContent = value;
        row.append(span);
      }
      list.append(row);
    }
    if (!scores.length) $('rankingStatus').textContent = '아직 등록된 기록이 없습니다.';
  }
  async function openRanking() {
    closeRegistration();
    $('ranking').classList.remove('hidden');
    $('rankingStatus').textContent = '랭킹을 불러오는 중입니다.';
    try {
      const data = await request('/scores');
      render(Array.isArray(data.scores) ? data.scores : []);
      if (data.scores?.length) $('rankingStatus').textContent = '서버에서 검증된 승리 기록입니다.';
    } catch (_) {
      render([]);
      $('rankingStatus').textContent = 'C# 서버에 연결할 수 없습니다.';
    }
  }
  async function submit(event) {
    event.preventDefault();
    if (!completedRun) return;
    const name = $('playerName').value.trim();
    if (!name || [...name].length > 12) { $('registrationStatus').textContent = '이름을 1–12자로 입력하세요.'; return; }
    const battleId = completedRun.battleId;
    $('submitScoreButton').disabled = true;
    $('registrationStatus').textContent = '기록을 등록하는 중입니다.';
    try {
      await request(`/battles/${battleId}/score`, { method: 'POST', headers: { 'Content-Type': 'application/json' }, body: JSON.stringify({name}) });
      completedRun = null;
      $('registerButton').classList.add('hidden');
      await openRanking();
    } catch (_) {
      $('registrationStatus').textContent = '등록하지 못했습니다. 서버 연결을 확인하고 다시 시도하세요.';
    } finally { $('submitScoreButton').disabled = false; }
  }

  $('menuRankingButton').addEventListener('click', openRanking);
  $('resultRankingButton').addEventListener('click', openRanking);
  $('registerButton').addEventListener('click', openRegistration);
  $('closeRegistrationButton').addEventListener('click', closeRegistration);
  $('closeRankingButton').addEventListener('click', closeRanking);
  $('registrationForm').addEventListener('submit', submit);
  window.leaderboard = { onFinish, onReset };
})();
