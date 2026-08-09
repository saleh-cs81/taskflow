(async function () {
  await I18N.load();
  if (!Auth.requireAuth()) return;
  await UI.mountNavbar('board');

  const gridEl = document.getElementById('taskGrid');
  const picker = document.getElementById('projectPicker');

  let projects = [];
  try { projects = (await API.get('/projects?pageSize=100')).items; } catch {}
  if (!projects.length) { gridEl.innerHTML = `<div class="text-muted p-3">${I18N.t('dash.noProjects')}</div>`; return; }

  picker.innerHTML = projects.map(p => `<option value="${p.id}">${UI.esc(p.name)}</option>`).join('');
  const params = new URLSearchParams(location.search);
  let projectId = parseInt(params.get('projectId') || projects[0].id, 10);
  picker.value = projectId;

  let users = [];
  try { users = await API.get('/users'); } catch {}
  const userName = uid => users.find(u => u.id === uid)?.fullName || '—';
  document.getElementById('tf_assignee').innerHTML =
    `<option value="">—</option>` + users.map(u => `<option value="${u.id}">${UI.esc(u.fullName)} (${UI.esc(u.email)})</option>`).join('');

  const PRIO = p => `<span class="priority priority-${p}"></span>`;
  const ST = { 0: { k: 'board.st.todo', c: 'bg-secondary' }, 1: { k: 'board.st.inprogress', c: 'bg-primary' }, 2: { k: 'board.st.inreview', c: 'bg-info text-dark' }, 3: { k: 'board.st.done', c: 'bg-success' }, 4: { k: 'board.st.blocked', c: 'bg-danger' } };
  const fmtDate = d => d ? new Date(d).toLocaleDateString(I18N.lang, { dateStyle: 'medium' }) : I18N.t('common.none');
  const fmtDateTime = d => d ? new Date(d).toLocaleString(I18N.lang, { dateStyle: 'medium', timeStyle: 'short' }) : I18N.t('common.none');
  const fmtSize = b => b == null ? '' : b < 1024 ? b + ' B' : b < 1048576 ? (b / 1024).toFixed(0) + ' KB' : (b / 1048576).toFixed(1) + ' MB';
  const money = n => (n == null ? '—' : Number(n).toLocaleString(I18N.lang, { minimumFractionDigits: 2, maximumFractionDigits: 2 }));

  let defaultListId = null, taskCache = [], filesCache = [];

  // ---- sub-tab switching ----
  const loaders = {}, loaded = {};
  let activeTab = 'tasks';
  function show(tab) {
    activeTab = tab;
    document.querySelectorAll('#wsTabs .nav-link').forEach(b => b.classList.toggle('active', b.dataset.tab === tab));
    document.querySelectorAll('.tab-pane').forEach(p => p.classList.toggle('d-none', p.dataset.pane !== tab));
    if (!loaded[tab]) { loaded[tab] = true; loaders[tab]?.(); }
  }
  document.querySelectorAll('#wsTabs .nav-link').forEach(b => b.onclick = () => show(b.dataset.tab));

  picker.onchange = () => {
    projectId = parseInt(picker.value, 10);
    Object.keys(loaded).forEach(k => delete loaded[k]);
    loadMembers(); loaded[activeTab] = true; loaders[activeTab]?.(); subscribe();
    history.replaceState(null, '', `?projectId=${projectId}`);
  };

  // ===== Tasks tab (list / grid / cards views) =====
  let taskView = localStorage.getItem('tf_board_view') || 'cards';
  let filesByTaskCache = new Map();
  loaders.tasks = async () => {
    document.getElementById('projectName').textContent = projects.find(p => p.id === projectId)?.name ?? '';
    loadMembers();
    try { taskCache = (await API.get(`/tasks?projectId=${projectId}&pageSize=500`)).items || []; } catch { taskCache = []; }
    try { filesCache = await API.get(`/projects/${projectId}/files`); } catch { filesCache = []; }
    defaultListId = taskCache.find(t => t.taskListId)?.taskListId ?? null;
    if (defaultListId == null) { try { defaultListId = (await API.get(`/projects/${projectId}/board`)).columns?.[0]?.id ?? null; } catch {} }

    filesByTaskCache = new Map(); const projectFiles = [];
    filesCache.forEach(f => {
      if (f.taskId == null) { projectFiles.push(f); return; }
      if (!filesByTaskCache.has(f.taskId)) filesByTaskCache.set(f.taskId, []);
      filesByTaskCache.get(f.taskId).push(f);
    });
    // Project-level files go in their own section (task files stay on each task card).
    renderProjectFiles(projectFiles);
    renderTasks();
  };

  // Renders the task list in the selected view (list / grid / cards) into #taskGrid (a .row).
  function renderTasks() {
    const top = taskCache.filter(t => !t.parentTaskId);
    if (!top.length) { gridEl.innerHTML = `<div class="text-muted p-3">${I18N.t('board.noTasks')}</div>`; return; }
    if (taskView === 'list') gridEl.innerHTML = `<div class="col-12">${taskListHtml(top)}</div>`;
    else gridEl.innerHTML = top.map(t => taskView === 'grid' ? taskTile(t) : taskCard(t, filesByTaskCache.get(t.id) || [])).join('');
    gridEl.querySelectorAll('.dl-file').forEach(a => a.onclick = (e) => { e.preventDefault(); e.stopPropagation(); downloadFile(a.dataset.id, a.dataset.name); });
    gridEl.querySelectorAll('[data-opentask]').forEach(c => c.onclick = () => openTaskDetail(parseInt(c.dataset.opentask, 10)));
  }

  // Compact tile (Grid view).
  function taskTile(t) {
    const st = ST[t.status] || ST[0];
    return `<div class="col-6 col-md-4 col-xl-3"><div class="card h-100 shadow-sm task-open" data-opentask="${t.id}" style="cursor:pointer">
      <div class="card-body p-3 d-flex flex-column">
        <div class="small fw-semibold text-truncate mb-2">${PRIO(t.priority)}${UI.esc(t.title)}</div>
        <span class="badge ${st.c} align-self-start mt-auto">${I18N.t(st.k)}</span>
      </div></div></div>`;
  }

  // Dense table (List view).
  function taskListHtml(tasks) {
    const rows = tasks.map(t => {
      const st = ST[t.status] || ST[0];
      return `<tr class="task-open" data-opentask="${t.id}" style="cursor:pointer">
        <td class="fw-semibold">${PRIO(t.priority)}${UI.esc(t.title)}</td>
        <td><span class="badge ${st.c}">${I18N.t(st.k)}</span></td>
        <td class="text-muted small text-nowrap">${fmtDate(t.createdAtUtc)}</td>
        <td class="text-muted small text-nowrap">${fmtDate(t.updatedAtUtc)}</td>
      </tr>`;
    }).join('');
    return `<div class="card shadow-sm"><div class="table-responsive"><table class="table table-hover align-middle mb-0">
      <thead><tr><th>${I18N.t('task.title')}</th><th>${I18N.t('common.status')}</th><th>${I18N.t('board.created')}</th><th>${I18N.t('board.updated')}</th></tr></thead>
      <tbody>${rows}</tbody></table></div></div>`;
  }

  // View switcher (in the header, visible on all tabs) — reshapes the task list.
  function setTaskViewActive() {
    document.querySelectorAll('#taskViewSwitch [data-view]').forEach(b => b.classList.toggle('active', b.dataset.view === taskView));
  }
  document.querySelectorAll('#taskViewSwitch [data-view]').forEach(b => b.onclick = () => {
    taskView = b.dataset.view;
    localStorage.setItem('tf_board_view', taskView);
    setTaskViewActive();
    // Re-render the task list in place; stay on whatever tab the user is on (don't jump to Tasks).
    if (loaded['tasks']) renderTasks();
  });
  setTaskViewActive();

  function fileGridHtml(files) {
    if (!files.length) return `<span class="text-muted small">${I18N.t('board.noFiles')}</span>`;
    return `<div class="tf-file-grid">${files.map(f => `<div class="tf-file-item d-flex justify-content-between align-items-center border rounded px-2 py-1">
        <a href="#" class="dl-file text-truncate me-2" data-id="${f.id}" data-name="${UI.esc(f.fileName)}" title="${UI.esc(f.fileName)}"><i class="bi bi-file-earmark me-1"></i>${UI.esc(f.fileName)}</a>
        <span class="text-muted small" style="white-space:nowrap">${fmtSize(f.sizeBytes)}</span></div>`).join('')}</div>`;
  }
  function wireDownloads(el) { el.querySelectorAll('.dl-file').forEach(a => a.onclick = (e) => { e.preventDefault(); downloadFile(a.dataset.id, a.dataset.name); }); }
  function renderProjectFiles(files) {
    document.getElementById('projFilesCount').textContent = files.length;
    const el = document.getElementById('projFilesList');
    el.innerHTML = fileGridHtml(files);
    wireDownloads(el);
  }

  function fileRows(files) {
    if (!files.length) return `<span class="text-muted small">${I18N.t('board.noFiles')}</span>`;
    return files.map(f => `<div class="d-flex justify-content-between align-items-center py-1">
        <a href="#" class="dl-file text-truncate me-2" data-id="${f.id}" data-name="${UI.esc(f.fileName)}" title="${UI.esc(f.fileName)}">${UI.esc(f.fileName)}</a>
        <span class="text-muted small" style="white-space:nowrap">${fmtSize(f.sizeBytes)}</span></div>`).join('');
  }
  function taskCard(t, files) {
    const st = ST[t.status] || ST[0];
    const desc = t.description ? `<div class="small mb-2 tf-rich tf-rich-clip">${UI.safeHtml(t.description)}</div>` : '';
    return `<div class="col-12 col-md-6 col-xl-4"><div class="card h-100 shadow-sm task-open" data-opentask="${t.id}" style="cursor:pointer">
      <div class="card-body d-flex flex-column">
        <div class="d-flex justify-content-between align-items-start mb-2">
          <h6 class="mb-0">${PRIO(t.priority)}${UI.esc(t.title)}</h6>
          <span class="badge ${st.c} ms-2 flex-shrink-0">${I18N.t(st.k)}</span>
        </div>
        <div class="text-muted small mb-2"><div>🗓️ ${I18N.t('board.created')}: ${fmtDate(t.createdAtUtc)}</div><div>✏️ ${I18N.t('board.updated')}: ${fmtDate(t.updatedAtUtc)}</div></div>
        ${desc}
        <div class="border-top pt-2 mt-auto"><div class="small fw-semibold mb-1"><span class="text-muted">📎</span> ${I18N.t('board.files')} <span class="badge bg-light text-dark border">${files.length}</span></div>${fileRows(files)}</div>
      </div></div></div>`;
  }
  // Multipart upload of a file to a target (0=Task, 1=Project). Needs the JWT via a raw fetch.
  async function uploadTo(targetType, targetId, file) {
    const fd = new FormData();
    fd.append('targetType', targetType); fd.append('targetId', targetId); fd.append('file', file);
    const res = await fetch('/api/v1/files', { method: 'POST', headers: { 'Authorization': `Bearer ${API.accessToken}` }, body: fd });
    if (!res.ok) throw new Error('upload failed');
  }

  // ---- project-level file upload ----
  const projFileInput = document.getElementById('projFileInput');
  document.getElementById('uploadProjFileBtn').onclick = () => projFileInput.click();
  projFileInput.onchange = async () => {
    const file = projFileInput.files[0]; if (!file) return;
    try { await uploadTo(1, projectId, file); projFileInput.value = ''; loaded.tasks = true; loaders.tasks(); }
    catch { alert(I18N.t('common.error')); }
  };

  let currentMembers = [];
  async function loadMembers() {
    try {
      currentMembers = await API.get(`/projects/${projectId}/members`);
      document.getElementById('memberCount').textContent = currentMembers.length;
      document.getElementById('projectMembers').innerHTML = currentMembers.length
        ? currentMembers.map(m => `<span class="badge rounded-pill text-bg-light border" title="${UI.esc(m.email)}">${UI.esc(m.fullName)}${m.roleInProject ? ` · ${UI.esc(m.roleInProject)}` : ''} <a href="#" class="text-danger text-decoration-none" data-rmmember="${m.userId}">&times;</a></span>`).join('')
        : `<span class="text-muted">${I18N.t('board.noMembers')}</span>`;
      document.getElementById('projectMembers').querySelectorAll('[data-rmmember]').forEach(a => a.onclick = async (e) => {
        e.preventDefault();
        try { await API.del(`/projects/${projectId}/members/${a.dataset.rmmember}`); await loadMembers(); }
        catch (ex) { alert(ex.problem?.title || I18N.t('common.error')); }
      });
      renderMemberPicker();
    } catch {}
  }

  function renderMemberPicker() {
    const box = document.getElementById('memberPicker');
    if (!box) return;
    const q = (document.getElementById('memberSearch').value || '').trim().toLowerCase();
    const memberIds = new Set(currentMembers.map(m => m.userId));
    const list = users.filter(u => !memberIds.has(u.id) &&
      (!q || (u.fullName || '').toLowerCase().includes(q) || (u.email || '').toLowerCase().includes(q)));
    box.innerHTML = list.length
      ? list.map(u => `<button type="button" class="dropdown-item d-flex align-items-center gap-2" data-adduser="${u.id}">
          ${UI.avatar(u.fullName, u.id, 'sm')}<span class="text-truncate">${UI.esc(u.fullName)} <span class="text-muted small">${UI.esc(u.email)}</span></span></button>`).join('')
      : `<div class="text-muted small px-2 py-1">${I18N.t('board.noUsersToAdd')}</div>`;
    box.querySelectorAll('[data-adduser]').forEach(b => b.onclick = async () => {
      try { await API.post(`/projects/${projectId}/members`, { userId: parseInt(b.dataset.adduser, 10), roleInProject: null }); await loadMembers(); }
      catch (ex) { alert(ex.problem?.title || I18N.t('common.error')); }
    });
  }
  document.getElementById('memberSearch').oninput = renderMemberPicker;
  async function downloadFile(id, name) {
    try {
      const res = await fetch(`/api/v1/files/${id}/download`, { headers: { 'Authorization': `Bearer ${API.accessToken}` } });
      if (!res.ok) { alert(I18N.t('common.error')); return; }
      const blob = await res.blob(); const url = URL.createObjectURL(blob);
      const a = document.createElement('a'); a.href = url; a.download = name || 'file'; document.body.appendChild(a); a.click(); a.remove(); URL.revokeObjectURL(url);
    } catch { alert(I18N.t('common.error')); }
  }

  // ===== Milestones tab =====
  document.getElementById('ms_name').placeholder = I18N.t('ws.msName');
  loaders.milestones = async () => renderMilestones();
  async function renderMilestones() {
    const el = document.getElementById('msList');
    el.innerHTML = `<div class="text-muted">${I18N.t('common.loading')}</div>`;
    let list = []; try { list = await API.get(`/projects/${projectId}/milestones`); } catch {}
    el.innerHTML = list.length ? list.map(m => {
      const done = m.status === 3;
      return `<div class="d-flex justify-content-between align-items-center border-bottom py-2" data-ms="${m.id}">
        <div class="form-check mb-0"><input class="form-check-input" type="checkbox" ${done ? 'checked' : ''} data-toggle-ms data-name="${UI.esc(m.name)}">
          <label class="form-check-label ${done ? 'text-decoration-line-through text-muted' : ''}">${UI.esc(m.name)}</label></div>
        <div class="text-muted small">${m.dueDate ? fmtDate(m.dueDate) : ''}
          <button class="btn btn-sm btn-outline-danger ms-2" data-del-ms>×</button></div></div>`;
    }).join('') : `<div class="text-muted">${I18N.t('ws.noMilestones')}</div>`;
    el.querySelectorAll('[data-toggle-ms]').forEach(cb => cb.onclick = async () => {
      const row = cb.closest('[data-ms]');
      await API.put(`/projects/${projectId}/milestones/${row.dataset.ms}`, { name: cb.dataset.name, dueDate: null, status: cb.checked ? 3 : 0 });
      renderMilestones();
    });
    el.querySelectorAll('[data-del-ms]').forEach(b => b.onclick = async () => { await API.del(`/projects/${projectId}/milestones/${b.closest('[data-ms]').dataset.ms}`); renderMilestones(); });
  }
  document.getElementById('msForm').onsubmit = async (e) => {
    e.preventDefault();
    const name = document.getElementById('ms_name').value.trim(); const due = document.getElementById('ms_due').value;
    if (!name) return;
    try { await API.post(`/projects/${projectId}/milestones`, { name, dueDate: due ? `${due}T00:00:00Z` : null }); document.getElementById('msForm').reset(); renderMilestones(); }
    catch { alert(I18N.t('common.error')); }
  };

  // ===== Finance tab =====
  loaders.finance = async () => {
    const el = document.getElementById('financeBody');
    el.innerHTML = `<div class="text-muted">${I18N.t('common.loading')}</div>`;
    let f; try { f = await API.get(`/projects/${projectId}/finance`); } catch { el.innerHTML = `<div class="text-danger">${I18N.t('common.error')}</div>`; return; }
    const stat = (label, val, sub) => `<div class="col-6 col-md-3"><div class="card shadow-sm text-center"><div class="card-body py-3">
      <div class="h4 mb-0">${val}</div><div class="text-muted small">${label}</div>${sub ? `<div class="text-muted small">${sub}</div>` : ''}</div></div></div>`;
    const pct = f.budgetHours ? Math.min(100, Math.round(f.actualHours / f.budgetHours * 100)) : null;
    el.innerHTML = `<div class="row g-3 mb-3">
      ${stat(I18N.t('ws.actualHours'), f.actualHours.toFixed(1), `${I18N.t('ws.billableH')}: ${f.billableHours.toFixed(1)}`)}
      ${stat(I18N.t('ws.budgetHours'), f.budgetHours != null ? Number(f.budgetHours).toFixed(1) : '—', pct != null ? `${pct}%` : '')}
      ${stat(I18N.t('ws.budgetAmount'), f.budgetAmount != null ? money(f.budgetAmount) : '—')}
      ${stat(I18N.t('ws.expenses'), money(f.expensesTotal))}
    </div>
    ${pct != null ? `<div class="card shadow-sm"><div class="card-body">
      <div class="d-flex justify-content-between small mb-1"><span>${I18N.t('ws.budgetUsed')}</span><span>${f.actualHours.toFixed(1)} / ${Number(f.budgetHours).toFixed(1)} h</span></div>
      <div class="progress" style="height:14px"><div class="progress-bar ${pct >= 100 ? 'bg-danger' : pct >= 80 ? 'bg-warning' : 'bg-success'}" style="width:${pct}%"></div></div>
    </div></div>` : ''}`;
  };

  // ===== Files tab (grouped by task) =====
  loaders.files = async () => {
    const el = document.getElementById('filesAll');
    el.innerHTML = `<div class="text-muted">${I18N.t('common.loading')}</div>`;
    let files = filesCache; if (!files.length) { try { files = await API.get(`/projects/${projectId}/files`); } catch { files = []; } }
    if (!files.length) { el.innerHTML = `<div class="text-muted">${I18N.t('board.noFiles')}</div>`; return; }
    const groups = new Map();
    files.forEach(f => { const key = f.taskId != null ? `t${f.taskId}` : 'project'; if (!groups.has(key)) groups.set(key, { title: f.taskTitle || null, files: [] }); groups.get(key).files.push(f); });
    const ordered = [...groups.values()].sort((a, b) => (a.title ? 0 : 1) - (b.title ? 0 : 1));
    el.innerHTML = ordered.map(g => `<div class="mb-3"><div class="small fw-semibold border-bottom pb-1 mb-1"><span class="text-muted">${g.title ? '📎' : '📁'}</span> ${UI.esc(g.title || I18N.t('board.projectLevel'))}</div>${fileRows(g.files)}</div>`).join('');
    el.querySelectorAll('.dl-file').forEach(a => a.onclick = (e) => { e.preventDefault(); downloadFile(a.dataset.id, a.dataset.name); });
  };

  // ===== Discussions tab =====
  loaders.discussions = async () => renderDiscussions();
  async function renderDiscussions() {
    const el = document.getElementById('discList');
    el.innerHTML = `<div class="text-muted">${I18N.t('common.loading')}</div>`;
    let list = []; try { list = await API.get(`/discussions?projectId=${projectId}`); } catch {}
    el.innerHTML = list.length ? list.map(d => `<div class="border-bottom py-2">
        <a href="#" class="fw-semibold text-decoration-none disc-open" data-id="${d.id}" data-title="${UI.esc(d.title)}">${UI.esc(d.title)}</a>
        <span class="badge bg-light text-dark border ms-1">${d.postCount}</span>
        <div class="text-muted small">${userName(d.createdById)} · ${fmtDate(d.createdAtUtc)}</div>
        <div class="disc-posts mt-2 d-none" data-posts="${d.id}"></div></div>`).join('')
      : `<div class="text-muted">${I18N.t('ws.noDisc')}</div>`;
    el.querySelectorAll('.disc-open').forEach(a => a.onclick = (e) => { e.preventDefault(); openDiscussion(a.dataset.id); });
  }
  async function openDiscussion(id) {
    const box = document.querySelector(`[data-posts="${id}"]`);
    if (!box.classList.contains('d-none')) { box.classList.add('d-none'); return; }
    box.classList.remove('d-none');
    await loadPosts(id, box);
  }
  async function loadPosts(id, box) {
    box.innerHTML = `<div class="text-muted small">${I18N.t('common.loading')}</div>`;
    let posts = []; try { posts = await API.get(`/discussions/${id}/posts`); } catch {}
    box.innerHTML = `${posts.map(p => `<div class="bg-light rounded p-2 mb-1"><div class="small fw-semibold">${userName(p.authorId)} <span class="text-muted fw-normal">${fmtDateTime(p.createdAtUtc)}</span></div><div class="tf-rich">${UI.safeHtml(p.body)}</div></div>`).join('')}
      <div class="input-group input-group-sm mt-1"><input class="form-control" placeholder="${I18N.t('ws.reply')}" data-reply><button class="btn btn-outline-primary" data-send>${I18N.t('common.add')}</button></div>`;
    box.querySelector('[data-send]').onclick = async () => {
      const inp = box.querySelector('[data-reply]'); const body = inp.value.trim(); if (!body) return;
      await API.post(`/discussions/${id}/posts`, { body }); await loadPosts(id, box);
    };
  }
  document.getElementById('newDiscBtn').onclick = () => { document.getElementById('discForm').reset(); document.getElementById('discError').innerHTML = ''; discModal.show(); };
  const discModal = new bootstrap.Modal(document.getElementById('discModal'));
  document.getElementById('discForm').onsubmit = async (e) => {
    e.preventDefault();
    const title = document.getElementById('d_title').value.trim(); const first = document.getElementById('d_first').value.trim() || null;
    try { await API.post('/discussions', { projectId, title, firstPost: first }); discModal.hide(); renderDiscussions(); }
    catch (ex) { document.getElementById('discError').innerHTML = `<div class="alert alert-danger py-1">${ex.problem?.title || I18N.t('common.error')}</div>`; }
  };

  // ===== Activity tab =====
  loaders.activity = async () => {
    const el = document.getElementById('activityFeed');
    el.innerHTML = `<li class="list-group-item text-muted px-0">${I18N.t('common.loading')}</li>`;
    let feed = []; try { feed = await API.get(`/activity?take=50&projectId=${projectId}`); } catch {}
    el.innerHTML = feed.length ? feed.map(a => `<li class="list-group-item d-flex justify-content-between px-0">
        <span>${UI.esc(a.action)} <span class="text-muted">${a.entityType || ''} #${a.entityId || ''}</span></span>
        <small class="text-muted">${fmtDateTime(a.createdAtUtc)}</small></li>`).join('')
      : `<li class="list-group-item text-muted px-0">${I18N.t('notif.empty')}</li>`;
  };

  // ===== Overview tab (default): project summary + members strip + project files =====
  const PROJ_ST = { 0: 'proj.planned', 1: 'proj.active', 2: 'proj.onhold', 3: 'proj.completed', 4: 'proj.archived', 5: 'proj.cancelled' };
  let clientsCache = null;
  const ensureClients = async () => { if (!clientsCache) { try { clientsCache = await API.get('/clients'); } catch { clientsCache = []; } } return clientsCache; };
  loaders.overview = async () => {
    document.getElementById('projectName').textContent = projects.find(p => p.id === projectId)?.name ?? '';
    loadMembers();
    const [proj, fin, files, tasksRes, milestones, clients] = await Promise.all([
      API.get(`/projects/${projectId}`).catch(() => null),
      API.get(`/projects/${projectId}/finance`).catch(() => null),
      API.get(`/projects/${projectId}/files`).catch(() => []),
      API.get(`/tasks?projectId=${projectId}&pageSize=500`).catch(() => ({ items: [] })),
      API.get(`/projects/${projectId}/milestones`).catch(() => []),
      ensureClients()
    ]);
    const tasks = tasksRes.items || [];
    const done = tasks.filter(t => t.status === 3).length;
    const projFiles = files.filter(f => f.taskId == null);
    const stat = (label, val) => `<div class="col-6 col-md"><div class="card shadow-sm text-center"><div class="card-body py-3">
      <div class="h4 mb-0">${val}</div><div class="text-muted small">${label}</div></div></div></div>`;
    document.getElementById('ovStats').innerHTML =
      stat(I18N.t('rep.openTasks'), tasks.length - done) + stat(I18N.t('rep.completedTasks'), done) +
      stat(I18N.t('ws.actualHours'), fin ? fin.actualHours.toFixed(1) : '0') +
      stat(I18N.t('board.projectFiles'), projFiles.length) + stat(I18N.t('ws.milestones'), milestones.length);

    const row = (label, val) => val ? `<div class="col-sm-6 mb-2"><div class="text-muted small">${label}</div><div>${val}</div></div>` : '';
    const clientName = proj && proj.clientId ? UI.esc(clients.find(c => c.id === proj.clientId)?.name || '') : '';
    const dates = proj && (proj.startDate || proj.dueDate) ? `${fmtDate(proj.startDate)} → ${fmtDate(proj.dueDate)}` : '';
    const budget = proj && (proj.budgetHours || proj.budgetAmount)
      ? [proj.budgetHours ? `${Number(proj.budgetHours).toFixed(0)} h` : '', proj.budgetAmount ? money(proj.budgetAmount) : ''].filter(Boolean).join(' · ') : '';
    document.getElementById('ovDetails').innerHTML = proj ? `<div class="row">
        ${row(I18N.t('proj.status'), `<span class="badge bg-light text-dark border">${I18N.t(PROJ_ST[proj.status] || '')}</span>`)}
        ${row(I18N.t('ws.client'), clientName)}${row(I18N.t('proj.code'), proj.code ? UI.esc(proj.code) : '')}
        ${row(I18N.t('ws.dates'), dates)}${row(I18N.t('ws.budget'), budget)}
        ${proj.description ? `<div class="col-12 mt-2"><div class="text-muted small">${I18N.t('proj.description')}</div><div class="tf-rich">${UI.safeHtml(proj.description)}</div></div>` : ''}
      </div>` : `<div class="text-muted">—</div>`;

    document.getElementById('ovFilesCount').textContent = projFiles.length;
    const fel = document.getElementById('ovFilesList');
    fel.innerHTML = fileGridHtml(projFiles); wireDownloads(fel);
  };
  // upload a project file from the Overview tab
  const ovFileInput = document.getElementById('ovFileInput');
  document.getElementById('ovUploadBtn').onclick = () => ovFileInput.click();
  ovFileInput.onchange = async () => { const f = ovFileInput.files[0]; if (!f) return; try { await uploadTo(1, projectId, f); ovFileInput.value = ''; loaded.overview = true; loaders.overview(); loaded.tasks = false; } catch { alert(I18N.t('common.error')); } };

  // ===== Timesheets tab: this project's time entries, grouped by user =====
  loaders.timesheets = async () => {
    const el = document.getElementById('timesheetBody');
    el.innerHTML = `<div class="text-muted">${I18N.t('common.loading')}</div>`;
    let entries = []; try { entries = await API.get(`/time/entries?from=2015-01-01&to=2035-12-31&projectId=${projectId}`); } catch {}
    if (!entries.length) { el.innerHTML = `<div class="text-muted">${I18N.t('ws.noTime')}</div>`; return; }
    const taskMap = new Map(taskCache.map(t => [t.id, t.title]));
    if (!taskCache.length) { try { ((await API.get(`/tasks?projectId=${projectId}&pageSize=500`)).items || []).forEach(t => taskMap.set(t.id, t.title)); } catch {} }
    const byUser = new Map();
    entries.forEach(e => { if (!byUser.has(e.userId)) byUser.set(e.userId, { hours: 0, rows: [] }); const g = byUser.get(e.userId); g.hours += e.durationHours; g.rows.push(e); });
    const total = entries.reduce((s, e) => s + e.durationHours, 0);
    const groups = [...byUser.entries()].sort((a, b) => b[1].hours - a[1].hours);
    el.innerHTML = `<div class="mb-3 fw-semibold">${I18N.t('ws.tsTotal')}: ${total.toFixed(1)} h</div>` +
      groups.map(([uid, g]) => `<div class="mb-3">
        <div class="fw-semibold small border-bottom pb-1 mb-1">${UI.esc(userName(uid))} <span class="badge bg-light text-dark border">${g.hours.toFixed(1)} h</span></div>
        <table class="table table-sm mb-0"><tbody>${g.rows.sort((a, b) => (a.startUtc || '').localeCompare(b.startUtc || '')).map(e => `<tr>
            <td class="small text-muted" style="width:130px">${fmtDate(e.startUtc)}</td>
            <td class="small">${e.taskId ? UI.esc(taskMap.get(e.taskId) || ('#' + e.taskId)) : '<span class="text-muted">—</span>'}${e.note ? ` <span class="text-muted">· ${UI.esc(e.note)}</span>` : ''}</td>
            <td class="small text-end" style="width:80px">${e.durationHours.toFixed(2)} h</td>
            <td class="text-end" style="width:50px">${e.isBillable ? '<span class="badge bg-success">$</span>' : ''}</td></tr>`).join('')}</tbody></table></div>`).join('');
  };

  // ===== Task Detail offcanvas =====
  const taskDetail = new bootstrap.Offcanvas(document.getElementById('taskDetail'));
  async function openTaskDetail(id) {
    document.getElementById('td_title').textContent = '…';
    document.getElementById('td_body').innerHTML = `<div class="text-muted">${I18N.t('common.loading')}</div>`;
    taskDetail.show();
    let t; try { t = await API.get(`/tasks/${id}`); } catch { document.getElementById('td_body').innerHTML = `<div class="text-danger">${I18N.t('common.error')}</div>`; return; }
    document.getElementById('td_title').textContent = t.title;
    const st = ST[t.status] || ST[0];
    const subs = taskCache.filter(x => x.parentTaskId === id);
    const field = (label, val) => `<div class="col-6 mb-2"><div class="text-muted small">${label}</div><div>${val}</div></div>`;
    document.getElementById('td_body').innerHTML = `
      <div class="row mb-2">
        ${field(I18N.t('common.status'), `<span class="badge ${st.c}">${I18N.t(st.k)}</span>`)}
        ${field(I18N.t('task.priority'), I18N.t(['task.prio.low','task.prio.normal','task.prio.high','task.prio.urgent'][t.priority] || 'task.prio.normal'))}
        ${field(I18N.t('task.assignee'), UI.esc(t.assigneeId ? userName(t.assigneeId) : '—'))}
        ${field(I18N.t('task.dueDate'), fmtDate(t.dueDate))}
      </div>
      ${t.description ? `<div class="mb-3"><div class="text-muted small">${I18N.t('task.description')}</div><div class="tf-rich">${UI.safeHtml(t.description)}</div></div>` : ''}
      <div class="mb-3"><div class="d-flex justify-content-between align-items-center mb-1">
          <span class="fw-semibold"><span class="text-muted">📎</span> ${I18N.t('ws.files')}</span>
          <button class="btn btn-sm btn-outline-primary py-0" id="td_upBtn"><i class="bi bi-upload"></i> ${I18N.t('board.upload')}</button></div>
        <div id="td_files"></div><input type="file" id="td_fileInput" class="d-none"></div>
      <div class="mb-3"><div class="fw-semibold mb-1">${I18N.t('ws.checklist')}</div><div id="td_checklist"></div>
        <div class="input-group input-group-sm mt-1"><input class="form-control" id="td_clText" placeholder="${I18N.t('ws.addItem')}"><button class="btn btn-outline-primary" id="td_clAdd">+</button></div></div>
      ${subs.length ? `<div class="mb-3"><div class="fw-semibold mb-1">${I18N.t('ws.subtasks')} <span class="badge bg-light text-dark border">${subs.length}</span></div>
        ${subs.map(s => `<div class="d-flex justify-content-between border-bottom py-1"><span>${PRIO(s.priority)}${UI.esc(s.title)}</span><span class="badge ${(ST[s.status]||ST[0]).c}">${I18N.t((ST[s.status]||ST[0]).k)}</span></div>`).join('')}</div>` : ''}
      <div><div class="fw-semibold mb-1">${I18N.t('ws.comments')}</div><div id="td_comments"></div>
        <div class="input-group input-group-sm mt-1"><input class="form-control" id="td_cmText" placeholder="${I18N.t('ws.addComment')}"><button class="btn btn-outline-primary" id="td_cmAdd">${I18N.t('common.add')}</button></div></div>`;
    renderChecklist(id); renderComments(id); renderTaskFiles(id);
    document.getElementById('td_clAdd').onclick = async () => { const v = document.getElementById('td_clText').value.trim(); if (!v) return; await API.post(`/tasks/${id}/checklist`, { text: v }); renderChecklist(id); };
    document.getElementById('td_cmAdd').onclick = async () => { const v = document.getElementById('td_cmText').value.trim(); if (!v) return; await API.post(`/tasks/${id}/comments`, { body: v, parentCommentId: null, mentionedUserIds: [] }); renderComments(id); };
    const fi = document.getElementById('td_fileInput');
    document.getElementById('td_upBtn').onclick = () => fi.click();
    fi.onchange = async () => { const f = fi.files[0]; if (!f) return; try { await uploadTo(0, id, f); fi.value = ''; renderTaskFiles(id); loaded.tasks = true; } catch { alert(I18N.t('common.error')); } };
  }
  async function renderTaskFiles(id) {
    const el = document.getElementById('td_files'); if (!el) return;
    let files = []; try { files = await API.get(`/files?targetType=0&targetId=${id}`); } catch {}
    el.innerHTML = files.length ? files.map(f => `<div class="d-flex justify-content-between align-items-center py-1">
        <a href="#" class="dl-file text-truncate me-2" data-id="${f.id}" data-name="${UI.esc(f.fileName)}" title="${UI.esc(f.fileName)}">${UI.esc(f.fileName)}</a>
        <span class="text-muted small">${fmtSize(f.sizeBytes)}</span></div>`).join('') : `<div class="text-muted small">—</div>`;
    el.querySelectorAll('.dl-file').forEach(a => a.onclick = (e) => { e.preventDefault(); downloadFile(a.dataset.id, a.dataset.name); });
  }
  async function renderChecklist(id) {
    const el = document.getElementById('td_checklist'); if (!el) return;
    let items = []; try { items = await API.get(`/tasks/${id}/checklist`); } catch {}
    el.innerHTML = items.length ? items.map(i => `<div class="form-check"><input class="form-check-input" type="checkbox" ${i.isDone ? 'checked' : ''} data-cl="${i.id}">
      <label class="form-check-label ${i.isDone ? 'text-decoration-line-through text-muted' : ''}">${UI.esc(i.text)}</label></div>`).join('') : `<div class="text-muted small">—</div>`;
    el.querySelectorAll('[data-cl]').forEach(cb => cb.onclick = async () => { await API.patch(`/tasks/${id}/checklist/${cb.dataset.cl}/toggle`, {}); renderChecklist(id); });
  }
  async function renderComments(id) {
    const el = document.getElementById('td_comments'); if (!el) return;
    let list = []; try { list = await API.get(`/tasks/${id}/comments`); } catch {}
    el.innerHTML = list.length ? list.map(c => `<div class="bg-light rounded p-2 mb-1"><div class="small fw-semibold">${userName(c.authorId)} <span class="text-muted fw-normal">${fmtDateTime(c.createdAtUtc)}</span></div><div class="tf-rich">${UI.safeHtml(c.body)}</div></div>`).join('') : `<div class="text-muted small">—</div>`;
    const box = document.getElementById('td_cmText'); if (box) box.value = '';
  }

  // ===== Task create modal =====
  const taskModal = new bootstrap.Modal(document.getElementById('taskModal'));
  const descEd = document.getElementById('tf_desc');
  // Rich-text toolbar (mousedown+preventDefault keeps the editor selection when the button is clicked).
  document.querySelectorAll('#taskModal .tf-editor-toolbar [data-cmd]').forEach(btn => {
    btn.addEventListener('mousedown', e => {
      e.preventDefault();
      const cmd = btn.dataset.cmd;
      if (cmd === 'createLink') { const url = prompt('Link URL:'); if (url) document.execCommand('createLink', false, url); }
      else document.execCommand(cmd, false, null);
      descEd.focus();
    });
  });
  document.getElementById('addTaskBtn').onclick = () => { document.getElementById('taskForm').reset(); descEd.innerHTML = ''; document.getElementById('tf_listId').value = defaultListId ?? ''; document.getElementById('taskError').innerHTML = ''; taskModal.show(); };
  document.getElementById('taskForm').onsubmit = async (e) => {
    e.preventDefault();
    const listId = document.getElementById('tf_listId').value; const due = document.getElementById('tf_due').value; const assigneeId = document.getElementById('tf_assignee').value;
    const rawDesc = descEd.innerHTML.trim();
    const descHtml = (rawDesc && rawDesc !== '<br>' && descEd.textContent.trim()) ? UI.safeHtml(rawDesc) : null;
    const body = { projectId, taskListId: listId ? parseInt(listId, 10) : null, parentTaskId: null, milestoneId: null,
      title: document.getElementById('tf_title').value.trim(), description: descHtml,
      priority: parseInt(document.getElementById('tf_priority').value, 10), assigneeId: assigneeId ? parseInt(assigneeId, 10) : null,
      startDate: null, dueDate: due ? `${due}T00:00:00Z` : null, estimateHours: null, isBillable: false };
    try { await API.post('/tasks', body); taskModal.hide(); loaded.tasks = true; loaders.tasks(); }
    catch (ex) { document.getElementById('taskError').innerHTML = `<div class="alert alert-danger py-1">${ex.problem?.title || I18N.t('common.error')}</div>`; }
  };

  // ===== delete project (all data) =====
  document.getElementById('deleteProjBtn').onclick = async () => {
    const name = projects.find(p => p.id === projectId)?.name || '';
    if (!confirm(`${I18N.t('board.deleteConfirm')}\n\n${name}`)) return;
    try {
      await API.del(`/projects/${projectId}/data`);
      projects = projects.filter(p => p.id !== projectId);
      if (!projects.length) { location.href = '/projects.html'; return; }
      picker.innerHTML = projects.map(p => `<option value="${p.id}">${UI.esc(p.name)}</option>`).join('');
      projectId = projects[0].id; picker.value = projectId;
      Object.keys(loaded).forEach(k => delete loaded[k]);
      show('overview'); subscribe();
    } catch { alert(I18N.t('common.error')); }
  };

  // ===== realtime =====
  let conn;
  async function subscribe() {
    conn = await Realtime.connect('board'); if (!conn) return;
    conn.off('task.moved'); conn.off('task.created');
    await conn.invoke('JoinProject', projectId).catch(() => {});
    conn.on('task.moved', () => { if (activeTab === 'tasks') loaders.tasks(); });
    conn.on('task.created', () => { if (activeTab === 'tasks') loaders.tasks(); });
  }

  document.addEventListener('lang-changed', () => location.reload());
  show('overview');
  await subscribe();
})();
