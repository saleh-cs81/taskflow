(async function () {
  await I18N.load();
  if (!Auth.requireAuth()) return;
  await UI.mountNavbar('integration');

  const statusBadge = document.getElementById('statusBadge');
  const lastSync = document.getElementById('lastSync');
  const runMsg = document.getElementById('runMsg');
  const wrap = document.getElementById('progressWrap');
  const bar = document.getElementById('progressBar');
  const pct = document.getElementById('progressPct');
  const label = document.getElementById('progressLabel');
  const runFull = document.getElementById('runFull');
  const runSync = document.getElementById('runSync');
  const resetBtn = document.getElementById('resetBtn');
  const catalogBody = document.getElementById('catalogBody');
  const catalogSummary = document.getElementById('catalogSummary');
  const batchSize = document.getElementById('batchSize');
  const migrateBatchBtn = document.getElementById('migrateBatchBtn');
  const refreshCatalogBtn = document.getElementById('refreshCatalogBtn');
  const migrateUsersBtn = document.getElementById('migrateUsersBtn');
  const stopBtn = document.getElementById('stopBtn');

  const RUNNING = s => s === 0 || s === 1; // Pending or Running
  let pollTimer = null;

  // Per-project migration status (MigrationProjectStatus): 0 Pending, 1 Importing, 2 Imported, 3 Failed.
  function projStatusBadge(s) {
    const map = { 0: ['secondary', I18N.t('intg.ps.pending')], 1: ['info', I18N.t('intg.ps.importing')],
                  2: ['success', I18N.t('intg.ps.imported')], 3: ['danger', I18N.t('intg.ps.failed')] };
    const [c, t] = map[s] || ['secondary', '?'];
    return `<span class="badge bg-${c}">${t}</span>`;
  }

  async function loadCatalog() {
    let cat;
    try { cat = await API.get('/integrations/paymo/projects'); }
    catch { return; }
    catalogSummary.innerHTML =
      `<span class="text-success fw-semibold">${cat.imported}</span> / ${cat.total} ${I18N.t('intg.imported').toLowerCase()}` +
      (cat.failed ? ` · <span class="text-danger">${cat.failed} ${I18N.t('intg.ps.failed').toLowerCase()}</span>` : '') +
      (cat.pending ? ` · ${cat.pending} ${I18N.t('intg.ps.pending').toLowerCase()}` : '');
    if (!cat.items.length) {
      catalogBody.innerHTML = `<tr><td colspan="7" class="text-muted small py-3">${I18N.t('intg.noProjects')}</td></tr>`;
      return;
    }
    catalogBody.innerHTML = cat.items.map((p, i) => {
      let action = '';
      if (p.status === 0) action = `<button class="btn btn-primary btn-sm py-0 mig-one" data-id="${p.paymoProjectId}">${I18N.t('intg.migrate')}</button>`;
      else if (p.status === 3) action = `<button class="btn btn-outline-warning btn-sm py-0 mig-one" data-id="${p.paymoProjectId}">${I18N.t('intg.retry')}</button>`;
      return `
      <tr>
        <td class="text-muted small">${i + 1}</td>
        <td>${escapeHtml(p.name)}</td>
        <td>${projStatusBadge(p.status)}</td>
        <td>${p.status === 2 ? p.recordCount : ''}</td>
        <td class="small text-muted">${p.importedAtUtc ? new Date(p.importedAtUtc).toLocaleString(I18N.lang) : ''}</td>
        <td class="small text-danger">${p.errorMessage ? escapeHtml(p.errorMessage) : ''}</td>
        <td class="text-end">${action}</td>
      </tr>`; }).join('');
    // wire per-row migrate buttons; keep them disabled if a run is in progress (stop button enabled == running)
    const running = stopBtn && !stopBtn.disabled;
    catalogBody.querySelectorAll('.mig-one').forEach(b => { b.onclick = () => migrateProject(b.dataset.id); b.disabled = running; });
  }

  function escapeHtml(s) { return (s || '').replace(/[&<>"]/g, c => ({ '&': '&amp;', '<': '&lt;', '>': '&gt;', '"': '&quot;' }[c])); }

  async function loadStatus() {
    const s = await API.get('/integrations/paymo/status');
    if (s.connected) { statusBadge.className = 'badge bg-success'; statusBadge.textContent = I18N.t('intg.connected'); }
    else { statusBadge.className = 'badge bg-secondary'; statusBadge.textContent = I18N.t('intg.notConnected'); }
    lastSync.textContent = s.lastSyncUtc ? `${I18N.t('intg.lastSync')}: ${new Date(s.lastSyncUtc).toLocaleString(I18N.lang)}` : '';
  }

  function statusLabel(s) {
    const map = { 0: ['secondary', 'Pending'], 1: ['info', 'Running'], 2: ['success', 'Completed'],
                  3: ['warning', 'With errors'], 4: ['danger', 'Failed'] };
    const [color, text] = map[s] || ['secondary', '?'];
    return `<span class="badge bg-${color}">${text}</span>`;
  }

  async function loadJobs() {
    const jobs = await API.get('/integrations/paymo/jobs');
    document.getElementById('jobsBody').innerHTML = jobs.map(j => `
      <tr>
        <td>#${j.id} <span class="text-muted small">${j.type === 0 ? 'Full' : 'Inc'}</span></td>
        <td>${statusLabel(j.status)}</td>
        <td>${j.processedRecords}/${j.totalRecords || j.processedRecords}</td>
        <td>${j.errorCount > 0 ? `<span class="text-danger">${j.errorCount}</span>` : '0'}</td>
        <td class="small text-muted">${j.startedUtc ? new Date(j.startedUtc).toLocaleString(I18N.lang) : ''}</td>
      </tr>`).join('');
  }

  function setRunning(isRunning) {
    runFull.disabled = isRunning; runSync.disabled = isRunning; resetBtn.disabled = isRunning;
    if (migrateBatchBtn) migrateBatchBtn.disabled = isRunning;
    if (migrateUsersBtn) migrateUsersBtn.disabled = isRunning;
    if (stopBtn) stopBtn.disabled = !isRunning;   // Stop only enabled while a run is active
    document.querySelectorAll('.mig-one').forEach(b => b.disabled = isRunning);
  }

  function renderProgress(job) {
    if (!job) { wrap.classList.add('d-none'); return; }
    const running = RUNNING(job.status);
    wrap.classList.remove('d-none');
    const total = job.projectsTotal || 0;
    const done = job.projectsDone || 0;
    const percent = total > 0 ? Math.round(done * 100 / total) : (running ? 5 : 100);
    bar.style.width = percent + '%';
    pct.textContent = percent + '%';
    bar.classList.toggle('progress-bar-animated', running);
    bar.classList.remove('bg-success', 'bg-warning', 'bg-danger');
    if (job.status === 2) bar.classList.add('bg-success');
    else if (job.status === 3) bar.classList.add('bg-warning');
    else if (job.status === 4) bar.classList.add('bg-danger');
    label.textContent = job.message || (running ? I18N.t('intg.running') : '');
    setRunning(running);
  }

  function startPolling(jobId) {
    clearInterval(pollTimer);
    pollTimer = setInterval(async () => {
      try {
        const job = await API.get(`/integrations/paymo/jobs/${jobId}`);
        renderProgress(job);
        await loadJobs();
        await loadCatalog();
        if (!RUNNING(job.status)) { clearInterval(pollTimer); await loadStatus(); }
      } catch { clearInterval(pollTimer); }
    }, 1500);
  }

  document.getElementById('connectForm').onsubmit = async (e) => {
    e.preventDefault();
    const box = document.getElementById('connectAlert');
    try {
      await API.post('/integrations/paymo/connect', { apiKey: document.getElementById('apiKey').value });
      box.innerHTML = ''; document.getElementById('apiKey').value = '';
      await loadStatus();
    } catch (ex) { box.innerHTML = `<div class="alert alert-danger py-1">${ex.problem?.title || I18N.t('common.error')}</div>`; }
  };

  async function run(endpoint) {
    runMsg.innerHTML = `<div class="alert alert-info py-1">${I18N.t('intg.queued')}<br><small class="text-muted">${I18N.t('intg.resumeHint')}</small></div>`;
    try {
      const res = await API.post(`/integrations/paymo/${endpoint}`);
      setRunning(true);
      startPolling(res.jobId);
    } catch (ex) {
      runMsg.innerHTML = `<div class="alert alert-danger py-1">${ex.problem?.title || I18N.t('common.error')}</div>`;
    }
  }
  runFull.onclick = () => run('migrate');
  runSync.onclick = () => run('sync');

  async function startRun(endpoint) {
    runMsg.innerHTML = `<div class="alert alert-info py-1">${I18N.t('intg.queued')}</div>`;
    try {
      const res = await API.post(endpoint);
      setRunning(true);
      startPolling(res.jobId);
    } catch (ex) {
      runMsg.innerHTML = `<div class="alert alert-danger py-1">${ex.problem?.title || I18N.t('common.error')}</div>`;
    }
  }
  function migrateProject(paymoId) { return startRun(`/integrations/paymo/migrate-project/${paymoId}`); }

  migrateBatchBtn.onclick = () => {
    const n = Math.max(1, parseInt(batchSize.value, 10) || 10);
    return startRun(`/integrations/paymo/migrate-batch?count=${n}`);
  };
  migrateUsersBtn.onclick = () => startRun('/integrations/paymo/migrate-users');
  stopBtn.onclick = async () => {
    stopBtn.disabled = true;
    try { await API.post('/integrations/paymo/stop'); runMsg.innerHTML = `<div class="alert alert-warning py-1">${I18N.t('intg.stopping')}</div>`; }
    catch (ex) { runMsg.innerHTML = `<div class="alert alert-danger py-1">${ex.problem?.title || I18N.t('common.error')}</div>`; }
  };
  refreshCatalogBtn.onclick = () => loadCatalog();

  resetBtn.onclick = async () => {
    if (!confirm(I18N.t('intg.resetConfirm'))) return;
    try {
      await API.post('/integrations/paymo/reset');
      runMsg.innerHTML = `<div class="alert alert-success py-1">${I18N.t('intg.resetDone')}</div>`;
      wrap.classList.add('d-none');
      await loadStatus(); await loadJobs(); await loadCatalog();
    } catch (ex) { runMsg.innerHTML = `<div class="alert alert-danger py-1">${ex.problem?.title || I18N.t('common.error')}</div>`; }
  };

  document.addEventListener('lang-changed', () => location.reload());
  await loadStatus();
  await loadJobs();
  await loadCatalog();

  // If a job is already running (e.g. page reloaded mid-import), resume polling it.
  try {
    const latest = await API.get('/integrations/paymo/jobs/latest');
    if (latest && RUNNING(latest.status)) { renderProgress(latest); startPolling(latest.id); }
  } catch {}
})();
