(async function () {
  await I18N.load();
  if (!Auth.requireAuth()) return;
  await UI.mountNavbar('integration');

  const statusBadge = document.getElementById('statusBadge');
  const lastSync = document.getElementById('lastSync');
  const runMsg = document.getElementById('runMsg');

  async function loadStatus() {
    const s = await API.get('/integrations/paymo/status');
    if (s.connected) { statusBadge.className = 'badge bg-success'; statusBadge.textContent = I18N.t('intg.connected'); }
    else { statusBadge.className = 'badge bg-secondary'; statusBadge.textContent = I18N.t('intg.notConnected'); }
    lastSync.textContent = s.lastSyncUtc ? `${I18N.t('intg.lastSync')}: ${new Date(s.lastSyncUtc).toLocaleString(I18N.lang)}` : '';
  }

  async function loadJobs() {
    const jobs = await API.get('/integrations/paymo/jobs');
    document.getElementById('jobsBody').innerHTML = jobs.map(j => `
      <tr>
        <td>#${j.id} <span class="text-muted small">${j.type === 0 ? 'Full' : 'Inc'}</span></td>
        <td>${statusLabel(j.status)}</td>
        <td>${j.processedRecords}/${j.totalRecords}</td>
        <td>${j.errorCount > 0 ? `<span class="text-danger">${j.errorCount}</span>` : '0'}</td>
        <td class="small text-muted">${j.startedUtc ? new Date(j.startedUtc).toLocaleString(I18N.lang) : ''}</td>
      </tr>`).join('');
  }

  function statusLabel(s) {
    const map = { 0: ['secondary', 'Pending'], 1: ['info', 'Running'], 2: ['success', 'Completed'],
                  3: ['warning', 'With errors'], 4: ['danger', 'Failed'] };
    const [color, text] = map[s] || ['secondary', '?'];
    return `<span class="badge bg-${color}">${text}</span>`;
  }

  document.getElementById('connectForm').onsubmit = async (e) => {
    e.preventDefault();
    const alertBox = document.getElementById('connectAlert');
    try {
      await API.post('/integrations/paymo/connect', { apiKey: document.getElementById('apiKey').value });
      alertBox.innerHTML = '';
      await loadStatus();
    } catch (ex) { alertBox.innerHTML = `<div class="alert alert-danger py-1">${ex.problem?.title || I18N.t('common.error')}</div>`; }
  };

  async function run(endpoint) {
    runMsg.innerHTML = `<div class="alert alert-info py-1">${I18N.t('intg.running')}</div>`;
    try {
      const job = await API.post(`/integrations/paymo/${endpoint}`);
      runMsg.innerHTML = `<div class="alert alert-success py-1">${I18N.t('intg.done')}: ${job.processedRecords} (${job.errorCount} ${I18N.t('intg.errors').toLowerCase()})</div>`;
    } catch (ex) { runMsg.innerHTML = `<div class="alert alert-danger py-1">${ex.problem?.title || I18N.t('common.error')}</div>`; }
    await loadStatus();
    await loadJobs();
  }
  document.getElementById('runFull').onclick = () => run('migrate');
  document.getElementById('runSync').onclick = () => run('sync');

  document.addEventListener('lang-changed', () => location.reload());
  await loadStatus();
  await loadJobs();
})();
