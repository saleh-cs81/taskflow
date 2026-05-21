(async function () {
  await I18N.load();
  if (!Auth.requireAuth()) return;
  await UI.mountNavbar('team');

  let roles = [];

  async function loadRoles() {
    roles = await API.get('/roles');
    document.getElementById('inviteRole').innerHTML =
      roles.map(r => `<option value="${r.id}">${UI.esc(r.name)}</option>`).join('');
  }

  function roleOptions(selectedNames) {
    return roles.map(r => `<option value="${r.id}" ${selectedNames.includes(r.name) ? 'selected' : ''}>${UI.esc(r.name)}</option>`).join('');
  }

  async function loadUsers() {
    const users = await API.get('/users');
    document.getElementById('usersBody').innerHTML = users.map(u => `
      <tr data-id="${u.id}">
        <td>
          <div class="fw-semibold">${UI.esc(u.fullName)}</div>
          <div class="text-muted small">${UI.esc(u.email)}</div>
        </td>
        <td><select class="form-select form-select-sm" multiple size="2" data-roles>${roleOptions(u.roles)}</select></td>
        <td class="small text-muted">${u.lastLoginUtc ? new Date(u.lastLoginUtc).toLocaleString(I18N.lang) : I18N.t('team.never')}</td>
        <td class="text-nowrap">
          <div class="form-check form-switch d-inline-block align-middle me-2">
            <input class="form-check-input" type="checkbox" data-active ${u.isActive ? 'checked' : ''}>
          </div>
          <button class="btn btn-sm btn-outline-primary" data-save data-name="${UI.esc(u.fullName)}">${I18N.t('team.save')}</button>
        </td>
      </tr>`).join('');

    document.querySelectorAll('#usersBody [data-save]').forEach(btn => btn.onclick = async () => {
      const tr = btn.closest('tr');
      const id = tr.dataset.id;
      const isActive = tr.querySelector('[data-active]').checked;
      const roleIds = [...tr.querySelector('[data-roles]').selectedOptions].map(o => parseInt(o.value, 10));
      btn.disabled = true;
      try { await API.put(`/users/${id}`, { fullName: btn.dataset.name, isActive, roleIds }); await loadUsers(); }
      catch (e) { alert(e.problem?.title || I18N.t('common.error')); btn.disabled = false; }
    });
  }

  async function loadPending() {
    const list = await API.get('/invitations');
    const el = document.getElementById('pendingList');
    el.innerHTML = list.length ? list.map(i => `
      <div class="d-flex justify-content-between align-items-center py-2 border-bottom">
        <div><span class="fw-semibold">${UI.esc(i.email)}</span>
          <span class="badge bg-light text-dark ms-1">${UI.esc(i.roleName)}</span></div>
        <button class="btn btn-sm btn-outline-danger" data-revoke="${i.id}">${I18N.t('team.revoke')}</button>
      </div>`).join('') : `<div class="text-muted" data-i18n="team.noPending">${I18N.t('team.noPending')}</div>`;

    el.querySelectorAll('[data-revoke]').forEach(b => b.onclick = async () => {
      await API.del(`/invitations/${b.dataset.revoke}`); loadPending();
    });
  }

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
          <button class="btn btn-outline-secondary" id="copyLink">${I18N.t('team.copyLink')}</button>
        </div></div>`;
      document.getElementById('copyLink').onclick = async () => {
        await navigator.clipboard.writeText(link).catch(()=>{});
        document.getElementById('copyLink').textContent = I18N.t('team.linkCopied');
      };
      document.getElementById('inviteEmail').value = '';
      loadPending();
    } catch (ex) { alertBox.innerHTML = `<div class="alert alert-danger">${ex.problem?.title || I18N.t('common.error')}</div>`; }
  };

  document.addEventListener('lang-changed', () => location.reload());
  await loadRoles();
  await loadUsers();
  await loadPending();
})();
