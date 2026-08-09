(async function () {
  await I18N.load();
  if (!Auth.requireAuth()) return;
  await UI.mountNavbar('departments');

  const me = API.currentUser() || {};
  const isAdmin = (me.roles || []).some(r => r === 'CompanyAdmin' || r === 'SuperAdmin');
  if (isAdmin) document.getElementById('adminActions').hidden = false;

  let depts = [];
  let users = [];
  let catalog = { items: [] };

  const $ = (id) => document.getElementById(id);
  const modal = () => bootstrap.Modal.getOrCreateInstance($('deptModal'));

  // ---------- departments ----------
  function statCard(label, value, icon, color) {
    return `<div class="col-6 col-md-3"><div class="stat-card d-flex align-items-center gap-3">
      <span class="stat-icon" style="${color || ''}"><i class="bi ${icon}"></i></span>
      <div><div class="stat-value">${value}</div><div class="stat-label">${label}</div></div>
    </div></div>`;
  }
  function renderStats() {
    const totalProjects = depts.reduce((s, d) => s + (d.projectCount || 0), 0);
    $('deptStats').innerHTML =
      statCard(I18N.t('dept.statCount'), depts.length, 'bi-diagram-3') +
      statCard(I18N.t('dept.statProjects'), totalProjects, 'bi-folder', 'background:#e4f6f2;color:#0f9a80');
  }

  function deptCard(d) {
    const admins = (d.admins || []).length
      ? d.admins.map(a => `<span class="d-inline-flex align-items-center gap-1 me-2 mb-1">${UI.avatar(a.fullName, a.userId, 'sm')}<span class="small">${UI.esc(a.fullName)}</span></span>`).join('')
      : `<span class="text-muted small">${I18N.t('dept.noAdmins')}</span>`;
    const prefix = d.codePrefix
      ? `<span class="soft-badge">${UI.esc(d.codePrefix)}</span>`
      : `<span class="soft-badge gray">${I18N.t('dept.noCode')}</span>`;
    return `<div class="col-md-6 col-lg-4">
      <div class="card shadow-sm h-100"><div class="card-body d-flex flex-column">
        <div class="d-flex justify-content-between align-items-start gap-2 mb-2">
          <h6 class="mb-0 text-truncate">${UI.esc(d.name)}</h6>
          ${isAdmin ? `<button class="btn btn-sm btn-outline-secondary flex-none" data-edit="${d.id}" title="${I18N.t('dept.edit')}"><i class="bi bi-pencil"></i></button>` : ''}
        </div>
        <div class="mb-2">${prefix}</div>
        <div class="mb-2 flex-grow-1"><div class="tf-eyebrow mb-1">${I18N.t('dept.admins')}</div>${admins}</div>
        <a class="small" href="/projects.html?departmentId=${d.id}"><i class="bi bi-folder2"></i> ${d.projectCount || 0} ${I18N.t('dept.projects')}</a>
      </div></div>
    </div>`;
  }

  function renderDepts() {
    renderStats();
    $('deptList').innerHTML = depts.length
      ? depts.map(deptCard).join('')
      : `<div class="col-12"><div class="empty-state"><i class="bi bi-diagram-3 es-icon"></i><div class="es-text">${I18N.t('dept.empty')}</div></div></div>`;
    if (isAdmin) $('deptList').querySelectorAll('[data-edit]').forEach(b => b.onclick = () => openEdit(b.dataset.edit));
  }

  // ---------- create / edit ----------
  function openEdit(id) {
    const d = id ? depts.find(x => String(x.id) === String(id)) : null;
    $('deptError').innerHTML = '';
    $('d_id').value = d ? d.id : '';
    $('d_name').value = d ? d.name : '';
    $('d_prefix').value = d ? (d.codePrefix || '') : '';
    $('deptModalTitle').textContent = I18N.t(d ? 'dept.edit' : 'dept.new');
    $('deptDeleteBtn').hidden = !d;
    const adminIds = new Set((d?.admins || []).map(a => a.userId));
    $('d_admins').innerHTML = users.map(u => `
      <div class="form-check">
        <input class="form-check-input" type="checkbox" value="${u.id}" id="da_${u.id}" ${adminIds.has(u.id) ? 'checked' : ''}>
        <label class="form-check-label" for="da_${u.id}">${UI.esc(u.fullName)} <span class="text-muted small">${UI.esc(u.email)}</span></label>
      </div>`).join('') || `<div class="text-muted small">${I18N.t('dept.noUsers')}</div>`;
    modal().show();
  }

  $('deptForm').onsubmit = async (e) => {
    e.preventDefault();
    const id = $('d_id').value;
    const body = {
      name: $('d_name').value.trim(),
      codePrefix: $('d_prefix').value.trim() || null,
      managerUserId: null,
      adminUserIds: [...document.querySelectorAll('#d_admins input:checked')].map(i => parseInt(i.value, 10))
    };
    try {
      if (id) await API.put(`/departments/${id}`, body);
      else await API.post('/departments', body);
      modal().hide();
      await loadDepts();
    } catch (ex) {
      $('deptError').innerHTML = `<div class="alert alert-danger py-2">${ex.problem?.title || I18N.t('common.error')}</div>`;
    }
  };

  $('deptDeleteBtn').onclick = async () => {
    const id = $('d_id').value;
    if (!id || !confirm(I18N.t('dept.confirmDelete'))) return;
    try { await API.del(`/departments/${id}`); modal().hide(); await loadDepts(); }
    catch (ex) { $('deptError').innerHTML = `<div class="alert alert-danger py-2">${ex.problem?.title || I18N.t('common.error')}</div>`; }
  };

  if (isAdmin) {
    $('newDeptBtn').onclick = () => openEdit(null);
    $('assignBtn').onclick = async () => {
      $('assignBtn').disabled = true;
      try { const r = await API.post('/departments/assign-projects'); await loadDepts(); alert(I18N.t('dept.assigned').replace('{n}', r.changed)); }
      catch { alert(I18N.t('common.error')); }
      finally { $('assignBtn').disabled = false; }
    };
  }

  // ---------- Paymo migration catalog (department-scoped by backend) ----------
  const CAT_ST = { 0: { k: 'intg.pending', t: 'off' }, 1: { k: 'intg.importing', t: 'info' }, 2: { k: 'intg.imported', t: 'on' }, 3: { k: 'intg.failed', t: 'danger' } };
  function catRow(p) {
    const st = CAT_ST[p.status] || CAT_ST[0];
    const canMigrate = p.status === 0 || p.status === 3;
    return `<div class="list-row" data-pid="${p.paymoProjectId}">
      <div class="flex-grow-1 min-w-0">
        <div class="fw-semibold text-truncate">${UI.esc(p.name)}</div>
        <div>${p.code ? `<span class="soft-badge gray">${UI.esc(p.code)}</span>` : `<span class="text-muted small">${I18N.t('dept.noCode')}</span>`}</div>
      </div>
      <span class="status-pill ${st.t}">${I18N.t(st.k)}</span>
      ${canMigrate ? `<button class="btn btn-sm btn-outline-primary flex-none" data-mig="${p.paymoProjectId}"><i class="bi bi-download"></i> ${I18N.t('intg.migrate')}</button>`
        : `<span class="flex-none" style="width:1px"></span>`}
    </div>`;
  }
  function renderCatalog() {
    const q = ($('catSearch').value || '').trim().toLowerCase();
    const items = catalog.items.filter(p => !q || (p.name || '').toLowerCase().includes(q) || (p.code || '').toLowerCase().includes(q));
    $('catCount').textContent = catalog.items.length;
    $('catList').innerHTML = items.length
      ? items.map(catRow).join('')
      : `<div class="empty-state"><i class="bi bi-inbox es-icon"></i><div class="es-text">${I18N.t('dept.noCatalog')}</div></div>`;
    $('catList').querySelectorAll('[data-mig]').forEach(b => b.onclick = async () => {
      b.disabled = true; b.innerHTML = `<span class="spinner-border spinner-border-sm"></span>`;
      try { await API.post(`/integrations/paymo/migrate-project/${b.dataset.mig}`); setTimeout(loadCatalog, 1500); }
      catch (ex) { alert(ex.problem?.title || I18N.t('common.error')); b.disabled = false; }
    });
  }

  // ---------- loaders ----------
  async function loadDepts() { depts = await API.get('/departments'); renderDepts(); }
  async function loadUsers() { try { users = await API.get('/users'); } catch { users = []; } }
  async function loadCatalog() { try { catalog = await API.get('/integrations/paymo/projects'); } catch { catalog = { items: [] }; } renderCatalog(); }

  $('catSearch').oninput = renderCatalog;
  document.addEventListener('lang-changed', () => location.reload());

  if (isAdmin) await loadUsers();
  await loadDepts();
  await loadCatalog();
})();
