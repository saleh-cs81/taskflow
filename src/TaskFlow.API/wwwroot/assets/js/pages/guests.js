(async function () {
  await I18N.load();
  if (!Auth.requireAuth()) return;
  await UI.mountNavbar('guests');

  const GUEST_ROLE = 'Client'; // the system role for external, limited-access collaborators
  let roles = [];
  try { roles = await API.get('/roles'); } catch {}
  const roleIdByName = Object.fromEntries(roles.map(r => [r.name, r.id]));
  const guestRoleId = roleIdByName[GUEST_ROLE];

  let guests = [];

  const inviteModal = () => bootstrap.Modal.getOrCreateInstance(document.getElementById('inviteModal'));

  function statCard(label, value, icon, color) {
    return `<div class="col-6 col-md-3">
      <div class="stat-card d-flex align-items-center gap-3">
        <span class="stat-icon" style="${color || ''}"><i class="bi ${icon}"></i></span>
        <div>
          <div class="stat-value">${value}</div>
          <div class="stat-label">${label}</div>
        </div>
      </div></div>`;
  }

  function renderStats() {
    const active = guests.filter(u => u.isActive).length;
    const inactive = guests.length - active;
    document.getElementById('guestStats').innerHTML =
      statCard(I18N.t('guest.statTotal'), guests.length, 'bi-person-vcard') +
      statCard(I18N.t('guest.statActive'), active, 'bi-person-check', 'background:#e4f6f2;color:#0f9a80') +
      statCard(I18N.t('guest.statInactive'), inactive, 'bi-person-dash', 'background:#f1f3f6;color:#5a6675');
  }

  function guestRow(u) {
    const last = u.lastLoginUtc ? new Date(u.lastLoginUtc).toLocaleString(I18N.lang) : I18N.t('team.never');
    const roleIds = (u.roles || []).map(r => roleIdByName[r]).filter(Boolean).join(',');
    return `<div class="list-row" data-id="${u.id}" data-roles="${roleIds}">
      ${UI.avatar(u.fullName || u.email, u.id, 'lg')}
      <div class="flex-grow-1 min-w-0">
        <div class="fw-semibold text-truncate">${UI.esc(u.fullName || '—')}</div>
        <div class="text-muted small text-truncate">${UI.esc(u.email)}</div>
        <div class="text-muted small mt-1"><i class="bi bi-clock-history"></i> ${last}</div>
      </div>
      <div class="flex-none d-none d-md-flex align-items-center me-1">
        <div class="form-check form-switch mb-0">
          <input class="form-check-input" type="checkbox" data-active ${u.isActive ? 'checked' : ''} title="${I18N.t('team.active')}">
        </div>
        <span class="status-pill ${u.isActive ? 'on' : 'off'}">${I18N.t(u.isActive ? 'team.active' : 'team.inactive')}</span>
      </div>
      <div class="d-flex gap-1 flex-none">
        <button class="btn btn-sm btn-outline-secondary" data-projects data-name="${UI.esc(u.fullName || u.email)}" title="${I18N.t('team.projects')}"><i class="bi bi-folder2"></i></button>
        <button class="btn btn-sm btn-outline-primary" data-save data-name="${UI.esc(u.fullName || '')}" title="${I18N.t('team.save')}"><i class="bi bi-check-lg"></i></button>
      </div>
    </div>`;
  }

  function renderGuests() {
    const q = document.getElementById('guestSearch').value.trim().toLowerCase();
    const list = q
      ? guests.filter(u => (u.fullName || '').toLowerCase().includes(q) || (u.email || '').toLowerCase().includes(q))
      : guests;
    document.getElementById('guestCount').textContent = guests.length;
    const el = document.getElementById('guestsList');
    el.innerHTML = list.length
      ? list.map(guestRow).join('')
      : `<div class="empty-state"><i class="bi bi-person-x es-icon"></i><div class="es-text">${I18N.t('guest.empty')}</div></div>`;

    el.querySelectorAll('[data-projects]').forEach(btn =>
      btn.onclick = () => showUserProjects(btn.closest('.list-row').dataset.id, btn.dataset.name));
    el.querySelectorAll('[data-save]').forEach(btn => btn.onclick = async () => {
      const row = btn.closest('.list-row');
      const roleIds = row.dataset.roles.split(',').filter(Boolean).map(x => parseInt(x, 10));
      btn.disabled = true;
      try {
        await API.put(`/users/${row.dataset.id}`, { fullName: btn.dataset.name, isActive: row.querySelector('[data-active]').checked, roleIds });
        await loadGuests();
      } catch (e) {
        alert(e.problem?.title || I18N.t('common.error'));
        btn.disabled = false;
      }
    });
  }

  async function loadGuests() {
    let users = [];
    try { users = await API.get('/users'); } catch {}
    guests = users.filter(u => (u.roles || []).includes(GUEST_ROLE));
    renderGuests();
    renderStats();
  }

  // ----- Invite guest -----
  document.getElementById('inviteBtn').onclick = () => {
    document.getElementById('inviteAlert').innerHTML = '';
    document.getElementById('inviteEmail').value = '';
    inviteModal().show();
  };

  document.getElementById('inviteForm').onsubmit = async (e) => {
    e.preventDefault();
    const email = document.getElementById('inviteEmail').value;
    const alertBox = document.getElementById('inviteAlert');
    if (!guestRoleId) { alertBox.innerHTML = `<div class="alert alert-danger">${I18N.t('common.error')}</div>`; return; }
    try {
      const inv = await API.post('/invitations', { email, roleId: guestRoleId });
      const link = location.origin + inv.acceptUrl;
      alertBox.innerHTML = `<div class="alert alert-success"><div class="small mb-1">${UI.esc(email)}</div>
        <div class="input-group input-group-sm"><input class="form-control" value="${link}" readonly><button type="button" class="btn btn-outline-secondary" id="copyLink">${I18N.t('team.copyLink')}</button></div></div>`;
      document.getElementById('copyLink').onclick = async () => { await navigator.clipboard.writeText(link).catch(()=>{}); document.getElementById('copyLink').textContent = I18N.t('team.linkCopied'); };
      document.getElementById('inviteEmail').value = '';
    } catch (ex) { alertBox.innerHTML = `<div class="alert alert-danger">${ex.problem?.title || I18N.t('common.error')}</div>`; }
  };

  // ----- A guest's projects -----
  const PROJ_STATUS = { 0: 'Planned', 1: 'Active', 2: 'On hold', 3: 'Completed', 4: 'Archived', 5: 'Cancelled' };
  async function showUserProjects(id, name) {
    const modal = bootstrap.Modal.getOrCreateInstance(document.getElementById('userProjectsModal'));
    document.getElementById('upTitle').textContent = `${I18N.t('team.projectsOf')} ${name}`;
    const body = document.getElementById('upBody');
    body.innerHTML = `<div class="text-muted">${I18N.t('common.loading')}</div>`;
    modal.show();
    try {
      const list = await API.get(`/users/${id}/projects`);
      body.innerHTML = list.length
        ? `<ul class="list-group list-group-flush">${list.map(p => `<li class="list-group-item d-flex justify-content-between align-items-center px-0">
            <a href="/board.html?projectId=${p.projectId}">${UI.esc(p.name)}</a>
            <span class="badge bg-light text-dark">${PROJ_STATUS[p.status] ?? ''}${p.roleInProject ? ' · ' + UI.esc(p.roleInProject) : ''}</span></li>`).join('')}</ul>`
        : `<div class="empty-state"><i class="bi bi-folder2-open es-icon"></i><div class="es-text">${I18N.t('team.noUserProjects')}</div></div>`;
    } catch { body.innerHTML = `<div class="text-danger">${I18N.t('common.error')}</div>`; }
  }

  document.getElementById('guestSearch').oninput = renderGuests;
  document.addEventListener('lang-changed', () => location.reload());

  await loadGuests();
})();
