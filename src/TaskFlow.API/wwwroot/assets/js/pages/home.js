(async function () {
  await I18N.load();
  if (!Auth.requireAuth()) return;
  await UI.mountNavbar('home');
  document.getElementById('userName').textContent = API.currentUser()?.fullName ?? '';

  const PRIO = p => `<span class="priority priority-${p}"></span>`;
  const ST = {
    0: { k: 'board.st.todo', c: 'bg-secondary' }, 1: { k: 'board.st.inprogress', c: 'bg-primary' },
    2: { k: 'board.st.inreview', c: 'bg-info text-dark' }, 3: { k: 'board.st.done', c: 'bg-success' },
    4: { k: 'board.st.blocked', c: 'bg-danger' },
  };
  const ymd = d => `${d.getFullYear()}-${String(d.getMonth()+1).padStart(2,'0')}-${String(d.getDate()).padStart(2,'0')}`;
  const fmtDay = d => d ? new Date(d).toLocaleDateString(I18N.lang, { month: 'short', day: 'numeric' }) : '';
  const todayStr = ymd(new Date());

  // shared caches
  let projects = [], users = [];
  const ensureProjects = async () => { if (!projects.length) { try { projects = (await API.get('/projects?pageSize=200')).items || []; } catch {} } return projects; };
  const ensureUsers = async () => { if (!users.length) { try { users = await API.get('/users'); } catch {} } return users; };

  // ---- tab switching ----
  const loaders = {};
  const loaded = {};
  function show(tab) {
    document.querySelectorAll('#homeTabs .nav-link').forEach(b => b.classList.toggle('active', b.dataset.tab === tab));
    document.querySelectorAll('.tab-pane').forEach(p => p.classList.toggle('d-none', p.dataset.pane !== tab));
    if (!loaded[tab]) { loaded[tab] = true; loaders[tab]?.(); }
    history.replaceState(null, '', `?tab=${tab}`);
  }
  document.querySelectorAll('#homeTabs .nav-link').forEach(b => b.onclick = () => show(b.dataset.tab));

  // ---- a task row (with optional quick actions) ----
  function taskRow(t, actions) {
    const st = ST[t.status] || ST[0];
    const overdue = t.dueDate && t.dueDate.slice(0, 10) < todayStr;
    const due = t.dueDate ? `<span class="badge ${overdue ? 'bg-danger' : 'bg-light text-dark border'} ms-2">${fmtDay(t.dueDate)}</span>` : '';
    return `<div class="d-flex justify-content-between align-items-center border-bottom py-2" data-id="${t.id}" data-pid="${t.projectId}">
      <div class="me-2 text-truncate">
        ${PRIO(t.priority)}<a href="/board.html?projectId=${t.projectId}" class="text-decoration-none">${UI.esc(t.title)}</a>
        ${due}<span class="text-muted small ms-2">${UI.esc(t.projectName || '')}</span>
      </div>
      <div class="d-flex align-items-center gap-1 flex-shrink-0">
        <span class="badge ${st.c}">${I18N.t(st.k)}</span>
        ${actions ? `<button class="btn btn-sm btn-outline-success" data-complete title="${I18N.t('home.complete')}"><i class="bi bi-check2"></i></button>
        <button class="btn btn-sm btn-outline-primary" data-timer title="${I18N.t('home.startTimer')}"><i class="bi bi-play-fill"></i></button>` : ''}
      </div>
    </div>`;
  }

  function wireRowActions(container, reload) {
    container.querySelectorAll('[data-complete]').forEach(b => b.onclick = async () => {
      const id = b.closest('[data-id]').dataset.id;
      b.disabled = true;
      try { await API.patch(`/tasks/${id}/move`, { taskListId: null, position: 0, status: 3 }); reload(); }
      catch { b.disabled = false; }
    });
    container.querySelectorAll('[data-timer]').forEach(b => b.onclick = async () => {
      const row = b.closest('[data-id]');
      b.disabled = true;
      try {
        await API.post('/time/start', { projectId: parseInt(row.dataset.pid, 10), taskId: parseInt(row.dataset.id, 10), note: null, isBillable: true });
        b.innerHTML = '<i class="bi bi-check"></i>';
      } catch { b.disabled = false; }
    });
  }

  // ---- My Day ----
  loaders.myday = async () => {
    document.getElementById('mydayDate').textContent = new Date().toLocaleDateString(I18N.lang, { weekday: 'long', month: 'long', day: 'numeric' });
    const el = document.getElementById('mydayList');
    el.innerHTML = `<div class="text-muted">${I18N.t('common.loading')}</div>`;
    let mine = []; try { mine = await API.get('/me/tasks'); } catch {}
    const today = mine.filter(t => t.dueDate && t.dueDate.slice(0, 10) <= todayStr);
    el.innerHTML = today.length ? today.map(t => taskRow(t, true)).join('') : `<div class="text-muted">${I18N.t('home.noToday')}</div>`;
    wireRowActions(el, () => { loaded.myday = false; show('myday'); });
  };

  // ---- My Tasks (grouped by project) ----
  loaders.mytasks = async () => {
    const el = document.getElementById('mytasksGroups');
    el.innerHTML = `<div class="text-muted">${I18N.t('common.loading')}</div>`;
    let mine = []; try { mine = await API.get('/me/tasks'); } catch {}
    if (!mine.length) { el.innerHTML = `<div class="text-muted">${I18N.t('home.noMine')}</div>`; return; }
    const byProj = new Map();
    mine.forEach(t => { if (!byProj.has(t.projectId)) byProj.set(t.projectId, { name: t.projectName, tasks: [] }); byProj.get(t.projectId).tasks.push(t); });
    el.innerHTML = [...byProj.values()].map(g => `<div class="card shadow-sm mb-3"><div class="card-body">
        <h6 class="mb-2"><i class="bi bi-folder"></i> ${UI.esc(g.name)} <span class="badge bg-light text-dark border">${g.tasks.length}</span></h6>
        ${g.tasks.map(t => taskRow(t, true)).join('')}</div></div>`).join('');
    wireRowActions(el, () => { loaded.mytasks = false; show('mytasks'); });
  };

  // ---- Team's Tasks (calendar of assignments, by month) ----
  let teamMonth = new Date(); teamMonth.setDate(1);
  loaders.team = async () => renderTeam();
  async function renderTeam() {
    await ensureUsers(); await ensureProjects();
    const first = new Date(teamMonth.getFullYear(), teamMonth.getMonth(), 1);
    const next = new Date(teamMonth.getFullYear(), teamMonth.getMonth() + 1, 1);
    document.getElementById('teamLabel').textContent = first.toLocaleDateString(I18N.lang, { month: 'long', year: 'numeric' });
    const el = document.getElementById('teamList');
    el.innerHTML = `<div class="text-muted">${I18N.t('common.loading')}</div>`;
    let items = []; try { items = await API.get(`/reports/calendar?from=${ymd(first)}&to=${ymd(next)}`); } catch {}
    const withDue = items.filter(t => t.dueDate).sort((a, b) => a.dueDate.localeCompare(b.dueDate));
    if (!withDue.length) { el.innerHTML = `<div class="text-muted">${I18N.t('home.noTeam')}</div>`; return; }
    const uname = id => users.find(u => u.id === id)?.fullName || '—';
    const pname = id => projects.find(p => p.id === id)?.name || '';
    const byDate = new Map();
    withDue.forEach(t => { const k = t.dueDate.slice(0, 10); if (!byDate.has(k)) byDate.set(k, []); byDate.get(k).push(t); });
    el.innerHTML = [...byDate.entries()].map(([date, ts]) => `<div class="mb-2">
       <div class="small fw-semibold text-muted border-bottom pb-1 mb-1">${new Date(date).toLocaleDateString(I18N.lang, { weekday: 'short', month: 'short', day: 'numeric' })}</div>
       ${ts.map(t => `<div class="d-flex justify-content-between align-items-center py-1">
          <div class="text-truncate me-2">${PRIO(t.priority)}<a href="/board.html?projectId=${t.projectId}" class="text-decoration-none">${UI.esc(t.title)}</a>
            <span class="text-muted small ms-1">${UI.esc(pname(t.projectId))}</span></div>
          <span class="badge rounded-pill text-bg-light border">${UI.esc(uname(t.assigneeId))}</span></div>`).join('')}
     </div>`).join('');
  }
  document.getElementById('teamPrev').onclick = () => { teamMonth.setMonth(teamMonth.getMonth() - 1); renderTeam(); };
  document.getElementById('teamNext').onclick = () => { teamMonth.setMonth(teamMonth.getMonth() + 1); renderTeam(); };

  // ---- Dashboard ----
  loaders.dashboard = async () => {
    try {
      const s = await API.get('/reports/dashboard');
      const cards = [
        ['rep.activeProjects', s.activeProjects], ['rep.openTasks', s.openTasks], ['rep.overdue', s.overdueTasks],
        ['rep.hoursWeek', (s.hoursThisWeek ?? 0).toFixed(1)], ['rep.billableWeek', (s.billableHoursThisWeek ?? 0).toFixed(1)],
      ];
      document.getElementById('statCards').innerHTML = cards.map(([k, v]) => `<div class="col-6 col-md">
        <div class="card shadow-sm text-center"><div class="card-body py-3">
          <div class="h4 mb-0">${v ?? 0}</div><div class="text-muted small">${I18N.t(k)}</div></div></div></div>`).join('');
    } catch {}

    await ensureProjects();
    const sel = document.getElementById('timerProject');
    sel.innerHTML = projects.map(p => `<option value="${p.id}">${UI.esc(p.name)}</option>`).join('');
    const status = document.getElementById('timerStatus'), startBtn = document.getElementById('startBtn'), stopBtn = document.getElementById('stopBtn');
    async function refreshTimer() {
      const running = await API.get('/time/running');
      if (running) { status.innerHTML = `<span class="timer-running">● ${I18N.t('dash.running')}</span>`; startBtn.classList.add('d-none'); stopBtn.classList.remove('d-none'); }
      else { status.textContent = I18N.t('dash.noTimer'); startBtn.classList.remove('d-none'); stopBtn.classList.add('d-none'); }
    }
    startBtn.onclick = async () => { const pid = parseInt(sel.value, 10); if (!pid) return; await API.post('/time/start', { projectId: pid, taskId: null, note: null, isBillable: true }); refreshTimer(); };
    stopBtn.onclick = async () => { await API.post('/time/stop'); refreshTimer(); };
    await refreshTimer();

    try {
      const feed = await API.get('/activity?take=20');
      document.getElementById('activityList').innerHTML = feed.length
        ? feed.map(a => `<li class="list-group-item d-flex justify-content-between px-0">
            <span>${UI.esc(a.action)} <span class="text-muted">${a.entityType || ''} #${a.entityId || ''}</span></span>
            <small class="text-muted">${new Date(a.createdAtUtc).toLocaleString(I18N.lang)}</small></li>`).join('')
        : `<li class="list-group-item text-muted px-0">${I18N.t('notif.empty')}</li>`;
    } catch {}
  };

  document.addEventListener('lang-changed', () => location.reload());
  // open the requested tab (default My Day)
  const initial = new URLSearchParams(location.search).get('tab');
  show(['myday', 'mytasks', 'team', 'dashboard'].includes(initial) ? initial : 'myday');
})();
