(async function () {
  await I18N.load();
  if (!Auth.requireAuth()) return;
  await UI.mountNavbar('home');

  document.getElementById('userName').textContent = API.currentUser()?.fullName ?? '';

  const projectSelect = document.getElementById('timerProject');
  const startBtn = document.getElementById('startBtn');
  const stopBtn = document.getElementById('stopBtn');
  const status = document.getElementById('timerStatus');

  let projects = [];

  function renderProjects() {
    const list = document.getElementById('projectList');
    list.innerHTML = projects.length
      ? projects.map(p => `<div class="d-flex justify-content-between align-items-center py-1 border-bottom">
          <span>${UI.esc(p.name)}${p.code ? ` <span class="text-muted small">(${UI.esc(p.code)})</span>` : ''}</span>
          <a class="btn btn-sm btn-outline-primary" href="/board.html?projectId=${p.id}">${I18N.t('dash.openBoard')}</a></div>`).join('')
      : `<div class="text-muted">${I18N.t('dash.noProjects')}</div>`;
    projectSelect.innerHTML = projects.map(p => `<option value="${p.id}">${UI.esc(p.name)}</option>`).join('');
  }

  async function loadProjects() {
    try { projects = (await API.get('/projects?pageSize=100')).items; } catch { projects = []; }
    renderProjects();
  }
  await loadProjects();

  // --- New project form ---
  const form = document.getElementById('newProjectForm');
  const errBox = document.getElementById('newProjectError');
  form.onsubmit = async (e) => {
    e.preventDefault();
    errBox.innerHTML = '';
    const due = document.getElementById('np_due').value;
    const body = {
      name: document.getElementById('np_name').value.trim(),
      code: document.getElementById('np_code').value.trim() || null,
      description: document.getElementById('np_desc').value.trim() || null,
      categoryId: null,
      clientId: null,
      startDate: null,
      dueDate: due ? `${due}T00:00:00Z` : null,
      isBillable: document.getElementById('np_billable').checked,
      budgetAmount: null,
      budgetHours: null,
      color: null
    };
    try {
      await API.post('/projects', body);
      form.reset();
      bootstrap.Modal.getInstance(document.getElementById('newProjectModal')).hide();
      await loadProjects();
    } catch (ex) {
      errBox.innerHTML = `<div class="alert alert-danger py-1 mb-2">${ex.problem?.title || I18N.t('common.error')}</div>`;
    }
  };

  // --- Timer ---
  async function refreshTimer() {
    const running = await API.get('/time/running');
    if (running) {
      status.innerHTML = `<span class="timer-running">● ${I18N.t('dash.running')}</span>`;
      startBtn.classList.add('d-none'); stopBtn.classList.remove('d-none');
    } else {
      status.textContent = I18N.t('dash.noTimer');
      startBtn.classList.remove('d-none'); stopBtn.classList.add('d-none');
    }
  }
  startBtn.onclick = async () => {
    const projectId = parseInt(projectSelect.value, 10);
    if (!projectId) return;
    await API.post('/time/start', { projectId, taskId: null, note: null, isBillable: true });
    refreshTimer();
  };
  stopBtn.onclick = async () => { await API.post('/time/stop'); refreshTimer(); };
  await refreshTimer();

  // --- Activity feed ---
  try {
    const feed = await API.get('/activity?take=20');
    const al = document.getElementById('activityList');
    al.innerHTML = feed.length
      ? feed.map(a => `<li class="list-group-item d-flex justify-content-between">
          <span>${UI.esc(a.action)} <span class="text-muted">${a.entityType||''} #${a.entityId||''}</span></span>
          <small class="text-muted">${new Date(a.createdAtUtc).toLocaleString(I18N.lang)}</small></li>`).join('')
      : `<li class="list-group-item text-muted">—</li>`;
  } catch {}

  document.addEventListener('lang-changed', () => location.reload());
})();
