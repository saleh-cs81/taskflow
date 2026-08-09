(async function () {
  await I18N.load();
  if (!Auth.requireAuth()) return;
  await UI.mountNavbar('projects');

  const STATUS = ['Planned', 'Active', 'On hold', 'Completed', 'Archived', 'Cancelled'];
  // i18n label key per STATUS index (keys already exist in locales)
  const STATUS_KEY = ['proj.planned', 'proj.active', 'proj.onhold', 'proj.completed', 'proj.archived', 'proj.cancelled'];
  // status-pill tone per STATUS index
  const STATUS_PILL = ['off', 'on', 'warn', 'info', 'off', 'danger'];

  let clients = [];
  const clientMap = {};
  let departments = [];
  let allProjects = [];
  let view = localStorage.getItem('tf_proj_view') || 'cards';

  async function loadClients() {
    try { clients = await API.get('/clients'); } catch { clients = []; }
    try { departments = await API.get('/departments'); } catch { departments = []; }
    clients.forEach(c => { clientMap[c.id] = c.name; });
    populateDepts();
    document.getElementById('p_client').innerHTML =
      `<option value="">${I18N.t('common.none')}</option>` +
      clients.map(c => `<option value="${c.id}">${UI.esc(c.name)}</option>`).join('');
  }

  const clientNameOf = (p) => p.clientName || (p.clientId != null ? (clientMap[p.clientId] || '') : '');

  // ---------- stats ----------
  function statCard(label, value, icon, color) {
    return `<div class="col-6 col-md-3">
      <div class="stat-card d-flex align-items-center gap-3">
        <span class="stat-icon" style="${color || ''}"><i class="bi ${icon}"></i></span>
        <div><div class="stat-value">${value}</div><div class="stat-label">${label}</div></div>
      </div></div>`;
  }
  function renderStats(items) {
    const active = items.filter(p => p.status === 1).length;
    const completed = items.filter(p => p.status === 3).length;
    document.getElementById('projStats').innerHTML =
      statCard(I18N.t('proj.statTotal'), items.length, 'bi-folder') +
      statCard(I18N.t('proj.statActive'), active, 'bi-play-circle', 'background:#e4f6f2;color:#0f9a80') +
      statCard(I18N.t('proj.statCompleted'), completed, 'bi-check2-circle', 'background:#e7f1fe;color:#1d6fd6');
  }

  // ---------- editable status control (dropdown) ----------
  function statusLabel(i) { return STATUS_KEY[i] ? I18N.t(STATUS_KEY[i]) : (STATUS[i] || ''); }
  function statusControl(p) {
    const tone = STATUS_PILL[p.status] || 'off';
    const menu = STATUS.map((_, i) => `<li>
        <button type="button" class="dropdown-item d-flex align-items-center gap-2 ${i === p.status ? 'active' : ''}" data-setstatus="${p.id}" data-status="${i}">
          <span class="status-pill ${STATUS_PILL[i] || 'off'}">${statusLabel(i)}</span>
        </button></li>`).join('');
    return `<div class="dropdown d-inline-block">
      <button type="button" class="status-pill ${tone} dropdown-toggle" data-bs-toggle="dropdown" aria-expanded="false" title="${I18N.t('proj.changeStatus')}">${statusLabel(p.status)}</button>
      <ul class="dropdown-menu dropdown-menu-end shadow-sm">${menu}</ul>
    </div>`;
  }

  // ---------- views ----------
  function cardCol(p) {
    const code = p.code ? `<span class="soft-badge gray">${UI.esc(p.code)}</span>` : '';
    const cn = clientNameOf(p);
    const client = cn ? `<div class="text-muted small text-truncate mt-1"><i class="bi bi-building"></i> ${UI.esc(cn)}</div>` : '';
    return `<div class="col-md-4">
      <div class="card shadow-sm h-100"><div class="card-body d-flex flex-column">
        <div class="d-flex justify-content-between align-items-start gap-2 mb-2">
          <a class="fw-semibold text-truncate" href="/board.html?projectId=${p.id}" title="${UI.esc(p.name)}">${UI.esc(p.name)}</a>
          ${statusControl(p)}
        </div>
        <div class="d-flex flex-wrap align-items-center gap-2">${code}</div>
        ${client}
        <div class="text-muted small mt-1 mb-3"><i class="bi bi-people"></i> ${p.memberCount ?? 0} <span>${I18N.t('proj.membersLabel')}</span></div>
        <a class="btn btn-sm btn-outline-primary mt-auto align-self-start" href="/board.html?projectId=${p.id}">
          <i class="bi bi-kanban"></i> ${I18N.t('dash.openBoard')}
        </a>
      </div></div>
    </div>`;
  }

  function tileCol(p) {
    return `<div class="col-6 col-md-3">
      <div class="card shadow-sm h-100"><div class="card-body p-3 d-flex flex-column">
        <a class="fw-semibold text-truncate small mb-2" href="/board.html?projectId=${p.id}" title="${UI.esc(p.name)}">${UI.esc(p.name)}</a>
        <div>${statusControl(p)}</div>
        <div class="text-muted small mt-2 mb-2"><i class="bi bi-people"></i> ${p.memberCount ?? 0}</div>
        <a class="btn btn-sm btn-outline-primary mt-auto align-self-start" href="/board.html?projectId=${p.id}" title="${I18N.t('dash.openBoard')}"><i class="bi bi-kanban"></i></a>
      </div></div>
    </div>`;
  }

  function listView(list) {
    const rows = list.map(p => {
      const cn = clientNameOf(p);
      return `<tr>
        <td><a class="fw-semibold" href="/board.html?projectId=${p.id}">${UI.esc(p.name)}</a></td>
        <td>${p.code ? `<span class="soft-badge gray">${UI.esc(p.code)}</span>` : ''}</td>
        <td class="text-muted small">${cn ? UI.esc(cn) : I18N.t('common.none')}</td>
        <td>${statusControl(p)}</td>
        <td class="text-center text-muted small">${p.memberCount ?? 0}</td>
        <td class="text-end"><a class="btn btn-sm btn-outline-primary" href="/board.html?projectId=${p.id}"><i class="bi bi-kanban"></i> ${I18N.t('dash.openBoard')}</a></td>
      </tr>`;
    }).join('');
    return `<div class="card shadow-sm"><div class="table-responsive"><table class="table table-hover align-middle mb-0">
      <thead><tr>
        <th>${I18N.t('proj.name')}</th><th>${I18N.t('proj.code')}</th><th>${I18N.t('proj.client')}</th>
        <th>${I18N.t('common.status')}</th><th class="text-center">${I18N.t('team.members')}</th><th></th>
      </tr></thead><tbody>${rows}</tbody></table></div></div>`;
  }

  function emptyState(key) {
    return `<div class="empty-state"><i class="bi bi-folder2-open es-icon"></i><div class="es-text">${I18N.t(key)}</div></div>`;
  }

  // distinct, de-duplicated, sorted list of codes -> the #fCode dropdown
  function populateCodes() {
    const sel = document.getElementById('fCode');
    const current = sel.value;
    const codes = [...new Set(allProjects.map(p => p.code).filter(c => c && c.trim()))]
      .sort((a, b) => a.localeCompare(b, undefined, { numeric: true, sensitivity: 'base' }));
    sel.innerHTML = `<option value="">${I18N.t('proj.allCodes')}</option>` +
      codes.map(c => `<option value="${UI.esc(c)}">${UI.esc(c)}</option>`).join('');
    if (current && codes.includes(current)) sel.value = current;   // keep selection across reloads
  }

  function populateDepts() {
    const sel = document.getElementById('fDept');
    const current = sel.value || new URLSearchParams(location.search).get('departmentId') || '';
    sel.innerHTML = `<option value="">${I18N.t('proj.allDepts')}</option>` +
      departments.map(d => `<option value="${d.id}">${UI.esc(d.name)}</option>`).join('');
    if (current && departments.some(d => String(d.id) === String(current))) sel.value = current;
  }

  function filtered() {
    const q = (document.getElementById('projSearch').value || '').trim().toLowerCase();
    const code = document.getElementById('fCode').value;
    const dept = document.getElementById('fDept').value;
    return allProjects.filter(p => {
      if (code && p.code !== code) return false;
      if (dept && String(p.departmentId) !== dept) return false;
      if (!q) return true;
      return (p.name || '').toLowerCase().includes(q) ||
        (p.code || '').toLowerCase().includes(q) ||
        clientNameOf(p).toLowerCase().includes(q);
    });
  }

  function render() {
    const host = document.getElementById('projView');
    if (!allProjects.length) { host.innerHTML = emptyState('proj.empty'); return; }
    const list = filtered();
    if (!list.length) { host.innerHTML = emptyState('proj.noMatch'); return; }
    if (view === 'list') host.innerHTML = listView(list);
    else host.innerHTML = `<div class="row g-3">` + list.map(view === 'grid' ? tileCol : cardCol).join('') + `</div>`;
  }

  function setView(v) {
    view = v;
    localStorage.setItem('tf_proj_view', v);
    document.querySelectorAll('#viewSwitch [data-view]').forEach(b => b.classList.toggle('active', b.dataset.view === v));
    render();
  }

  async function load() {
    const data = await API.get('/projects?pageSize=100');
    allProjects = data.items || [];
    renderStats(allProjects);
    populateCodes();
    render();
  }

  // ---------- change status (delegated; PUT preserves all other fields) ----------
  document.getElementById('projView').addEventListener('click', async (e) => {
    const btn = e.target.closest('[data-setstatus]');
    if (!btn) return;
    const id = parseInt(btn.dataset.setstatus, 10);
    const status = parseInt(btn.dataset.status, 10);
    const p = allProjects.find(x => x.id === id);
    if (!p || p.status === status) return;
    const body = {
      name: p.name, code: p.code, description: p.description, categoryId: p.categoryId,
      clientId: p.clientId, status, startDate: p.startDate, dueDate: p.dueDate,
      isBillable: p.isBillable, budgetAmount: p.budgetAmount, budgetHours: p.budgetHours, color: p.color
    };
    try {
      const updated = await API.put(`/projects/${id}`, body);
      if (updated && typeof updated === 'object') Object.assign(p, updated); else p.status = status;
      renderStats(allProjects);
      render();
    } catch (ex) { alert(ex.problem?.title || I18N.t('common.error')); }
  });

  // search + code filter + view switcher wiring
  document.getElementById('projSearch').oninput = render;
  document.getElementById('fCode').onchange = render;
  document.getElementById('fDept').onchange = render;
  document.querySelectorAll('#viewSwitch [data-view]').forEach(b => b.onclick = () => setView(b.dataset.view));

  // ---------- create project (unchanged contract) ----------
  const modal = new bootstrap.Modal(document.getElementById('projModal'));
  document.getElementById('projForm').onsubmit = async (e) => {
    e.preventDefault();
    const due = document.getElementById('p_due').value;
    const clientId = document.getElementById('p_client').value;
    const body = {
      name: document.getElementById('p_name').value.trim(),
      code: document.getElementById('p_code').value.trim() || null,
      description: null, categoryId: null,
      clientId: clientId ? parseInt(clientId, 10) : null,
      startDate: null, dueDate: due ? `${due}T00:00:00Z` : null,
      isBillable: document.getElementById('p_billable').checked,
      budgetAmount: null, budgetHours: null, color: null
    };
    try { await API.post('/projects', body); document.getElementById('projForm').reset(); modal.hide(); await load(); }
    catch (ex) { document.getElementById('projErr').innerHTML = `<div class="alert alert-danger py-1">${ex.problem?.title || I18N.t('common.error')}</div>`; }
  };

  document.addEventListener('lang-changed', () => location.reload());
  await loadClients();
  setView(view);
  await load();
})();
