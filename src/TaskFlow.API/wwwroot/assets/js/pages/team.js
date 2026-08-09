(async function () {
  await I18N.load();
  if (!Auth.requireAuth()) return;
  await UI.mountNavbar('users');

  let roles = [];
  let users = [];
  let pending = [];

  const inviteModal = () => bootstrap.Modal.getOrCreateInstance(document.getElementById('inviteModal'));
  const editModal = () => bootstrap.Modal.getOrCreateInstance(document.getElementById('editUserModal'));

  async function loadRoles() {
    roles = await API.get('/roles');
    document.getElementById('inviteRole').innerHTML =
      roles.map(r => `<option value="${r.id}">${UI.esc(r.name)}</option>`).join('');
  }

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
    const active = users.filter(u => u.isActive).length;
    const admins = users.filter(u => (u.roles || []).some(r => /admin/i.test(r))).length;
    document.getElementById('teamStats').innerHTML =
      statCard(I18N.t('team.statMembers'), users.length, 'bi-people') +
      statCard(I18N.t('team.statActive'), active, 'bi-person-check', 'background:#e4f6f2;color:#0f9a80') +
      statCard(I18N.t('team.statAdmins'), admins, 'bi-shield-lock', 'background:#e7f1fe;color:#1d6fd6') +
      statCard(I18N.t('team.statPending'), pending.length, 'bi-envelope', 'background:#fef3c7;color:#b45309');
  }

  function userRow(u) {
    const rolesHtml = (u.roles || []).length
      ? u.roles.map(r => `<span class="soft-badge">${UI.esc(r)}</span>`).join(' ')
      : `<span class="soft-badge gray">${I18N.t('team.noRole')}</span>`;
    const last = u.lastLoginUtc ? new Date(u.lastLoginUtc).toLocaleDateString(I18N.lang) : I18N.t('team.never');
    return `<div class="list-row" data-id="${u.id}">
      ${UI.avatar(u.fullName || u.email, u.id, 'lg')}
      <div class="flex-grow-1 min-w-0">
        <div class="fw-semibold text-truncate">${UI.esc(u.fullName || '—')}</div>
        <div class="text-muted small text-truncate">${UI.esc(u.email)}</div>
        <div class="mt-1 d-flex flex-wrap gap-1">${rolesHtml}</div>
      </div>
      <div class="text-end d-none d-md-block me-1" style="min-width:96px">
        <span class="status-pill ${u.isActive ? 'on' : 'off'}">${I18N.t(u.isActive ? 'team.active' : 'team.inactive')}</span>
        <div class="text-muted small mt-1"><i class="bi bi-clock-history"></i> ${last}</div>
      </div>
      <div class="d-flex gap-1 flex-none">
        <button class="btn btn-sm btn-outline-secondary" data-projects data-name="${UI.esc(u.fullName || u.email)}" title="${I18N.t('team.projects')}"><i class="bi bi-folder2"></i></button>
        <button class="btn btn-sm btn-outline-primary" data-edit title="${I18N.t('team.editMember')}"><i class="bi bi-pencil"></i></button>
      </div>
    </div>`;
  }

  function renderUsers() {
    const q = document.getElementById('userSearch').value.trim().toLowerCase();
    const list = q
      ? users.filter(u => (u.fullName || '').toLowerCase().includes(q) || (u.email || '').toLowerCase().includes(q))
      : users;
    document.getElementById('memberCount').textContent = users.length;
    const el = document.getElementById('usersList');
    el.innerHTML = list.length
      ? list.map(userRow).join('')
      : `<div class="empty-state"><i class="bi bi-people es-icon"></i><div class="es-text">${I18N.t('team.noMembers')}</div></div>`;

    el.querySelectorAll('[data-projects]').forEach(btn =>
      btn.onclick = () => showUserProjects(btn.closest('.list-row').dataset.id, btn.dataset.name));
    el.querySelectorAll('[data-edit]').forEach(btn =>
      btn.onclick = () => openEdit(btn.closest('.list-row').dataset.id));
  }

  async function loadUsers() {
    users = await API.get('/users');
    renderUsers();
    renderStats();
  }

  // ----- Edit member modal -----
  function openEdit(id) {
    const u = users.find(x => String(x.id) === String(id));
    if (!u) return;
    document.getElementById('eu_id').value = u.id;
    document.getElementById('editUserError').innerHTML = '';
    document.getElementById('euHeader').innerHTML =
      `${UI.avatar(u.fullName || u.email, u.id, 'lg')}
       <div><div class="fw-semibold">${UI.esc(u.fullName || '—')}</div>
       <div class="text-muted small">${UI.esc(u.email)}</div></div>`;
    document.getElementById('eu_roles').innerHTML = roles.map(r => `
      <div class="form-check">
        <input class="form-check-input" type="checkbox" value="${r.id}" id="eur_${r.id}" ${(u.roles || []).includes(r.name) ? 'checked' : ''}>
        <label class="form-check-label" for="eur_${r.id}">${UI.esc(r.name)}</label>
      </div>`).join('');
    document.getElementById('eu_active').checked = !!u.isActive;
    document.getElementById('eu_password').value = '';
    document.getElementById('eu_pwMsg').innerHTML = '';
    editModal().show();
  }

  document.getElementById('editUserForm').onsubmit = async (e) => {
    e.preventDefault();
    const id = document.getElementById('eu_id').value;
    const u = users.find(x => String(x.id) === String(id));
    const isActive = document.getElementById('eu_active').checked;
    const roleIds = [...document.querySelectorAll('#eu_roles input:checked')].map(i => parseInt(i.value, 10));
    const btn = e.submitter; if (btn) btn.disabled = true;
    try {
      await API.put(`/users/${id}`, { fullName: u.fullName, isActive, roleIds });
      editModal().hide();
      await loadUsers();
    } catch (ex) {
      document.getElementById('editUserError').innerHTML =
        `<div class="alert alert-danger py-2">${ex.problem?.title || I18N.t('common.error')}</div>`;
    } finally { if (btn) btn.disabled = false; }
  };

  // ----- Admin sets a user's password -----
  // Strong random password (guarantees 1 upper/lower/digit/symbol; avoids ambiguous chars).
  function generatePassword(len = 14) {
    const U = 'ABCDEFGHJKLMNPQRSTUVWXYZ', L = 'abcdefghijkmnpqrstuvwxyz', D = '23456789', S = '!@#$%*?';
    const all = U + L + D + S;
    const rnd = (n) => crypto.getRandomValues(new Uint32Array(1))[0] % n;
    const pick = (s) => s[rnd(s.length)];
    const out = [pick(U), pick(L), pick(D), pick(S)];
    while (out.length < len) out.push(pick(all));
    for (let i = out.length - 1; i > 0; i--) { const j = rnd(i + 1); [out[i], out[j]] = [out[j], out[i]]; }
    return out.join('');
  }

  document.getElementById('eu_genPwBtn').onclick = async () => {
    const pw = generatePassword();
    const input = document.getElementById('eu_password');
    input.type = 'text';
    input.value = pw;
    let copied = false;
    try { await navigator.clipboard.writeText(pw); copied = true; } catch {}
    document.getElementById('eu_pwMsg').innerHTML =
      `<div class="text-muted small"><i class="bi bi-${copied ? 'clipboard-check' : 'info-circle'}"></i> ${I18N.t(copied ? 'team.pwGeneratedCopied' : 'team.pwGenerated')}</div>`;
  };

  document.getElementById('eu_setPwBtn').onclick = async () => {
    const id = document.getElementById('eu_id').value;
    const pw = document.getElementById('eu_password').value;
    const msg = document.getElementById('eu_pwMsg');
    if (pw.length < 8) { msg.innerHTML = `<div class="text-danger small">${I18N.t('team.passwordHint')}</div>`; return; }
    const btn = document.getElementById('eu_setPwBtn'); btn.disabled = true;
    try {
      await API.post(`/users/${id}/password`, { newPassword: pw });
      document.getElementById('eu_password').value = '';
      msg.innerHTML = `<div class="text-success small"><i class="bi bi-check-circle"></i> ${I18N.t('team.pwSet')}</div>`;
    } catch (ex) {
      msg.innerHTML = `<div class="text-danger small">${ex.problem?.title || I18N.t('common.error')}</div>`;
    } finally { btn.disabled = false; }
  };

  // ----- Pending invitations -----
  async function loadPending() {
    try { pending = await API.get('/invitations'); }
    catch { pending = []; }
    document.getElementById('pendingCount').textContent = pending.length;
    const el = document.getElementById('pendingList');
    el.innerHTML = pending.length ? pending.map(i => `
      <div class="list-row" data-inv="${i.id}">
        <span class="stat-icon" style="inline-size:34px;block-size:34px;font-size:1rem"><i class="bi bi-envelope"></i></span>
        <div class="flex-grow-1 min-w-0">
          <div class="fw-semibold text-truncate">${UI.esc(i.email)}</div>
          <div><span class="soft-badge gray">${UI.esc(i.roleName)}</span></div>
        </div>
        <button class="btn btn-sm btn-outline-danger flex-none" data-revoke="${i.id}" title="${I18N.t('team.revoke')}"><i class="bi bi-x-lg"></i></button>
      </div>`).join('')
      : `<div class="empty-state"><i class="bi bi-envelope-open es-icon"></i><div class="es-text">${I18N.t('team.noPending')}</div></div>`;

    el.querySelectorAll('[data-revoke]').forEach(b => b.onclick = async () => {
      await API.del(`/invitations/${b.dataset.revoke}`);
      await loadPending();
      renderStats();
    });
  }

  // ----- Invite -----
  document.getElementById('inviteBtn').onclick = () => {
    document.getElementById('inviteAlert').innerHTML = '';
    document.getElementById('inviteEmail').value = '';
    inviteModal().show();
  };

  document.getElementById('inviteForm').onsubmit = async (e) => {
    e.preventDefault();
    const email = document.getElementById('inviteEmail').value;
    const roleId = parseInt(document.getElementById('inviteRole').value, 10);
    const alertBox = document.getElementById('inviteAlert');
    try {
      const inv = await API.post('/invitations', { email, roleId });
      const link = location.origin + inv.acceptUrl;
      alertBox.innerHTML = `<div class="alert alert-success">
        <div class="small mb-1">${UI.esc(email)}</div>
        <div class="input-group input-group-sm">
          <input class="form-control" id="inviteLink" value="${link}" readonly>
          <button type="button" class="btn btn-outline-secondary" id="copyLink">${I18N.t('team.copyLink')}</button>
        </div></div>`;
      document.getElementById('copyLink').onclick = async () => {
        await navigator.clipboard.writeText(link).catch(() => {});
        document.getElementById('copyLink').textContent = I18N.t('team.linkCopied');
      };
      document.getElementById('inviteEmail').value = '';
      await loadPending();
      renderStats();
    } catch (ex) {
      alertBox.innerHTML = `<div class="alert alert-danger">${ex.problem?.title || I18N.t('common.error')}</div>`;
    }
  };

  // ----- A user's projects -----
  const PROJ_STATUS = { 0:'Planned',1:'Active',2:'On hold',3:'Completed',4:'Cancelled',5:'Archived' };
  async function showUserProjects(id, name) {
    const modal = bootstrap.Modal.getOrCreateInstance(document.getElementById('userProjectsModal'));
    document.getElementById('upTitle').textContent = `${I18N.t('team.projectsOf')} ${name}`;
    const body = document.getElementById('upBody');
    body.innerHTML = `<div class="text-muted">${I18N.t('common.loading') || '…'}</div>`;
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

  document.getElementById('userSearch').oninput = renderUsers;
  document.addEventListener('lang-changed', () => location.reload());

  await loadRoles();
  await loadUsers();
  await loadPending();
  renderStats();
})();
