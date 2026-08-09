(async function () {
  await I18N.load();
  if (!Auth.requireAuth()) return;
  await UI.mountNavbar('clients');

  const id = parseInt(new URLSearchParams(location.search).get('id'), 10);
  if (!id) { location.href = '/clients.html'; return; }

  const PROJ_ST = { 0: 'proj.planned', 1: 'proj.active', 2: 'proj.onhold', 3: 'proj.completed', 4: 'proj.archived', 5: 'proj.cancelled' };
  const INV_ST = { 0: 'inv.draft', 1: 'inv.sent', 2: 'inv.paid', 3: 'inv.overdue', 4: 'inv.cancelled' };
  const INV_CLS = { 0: 'bg-secondary', 1: 'bg-primary', 2: 'bg-success', 3: 'bg-danger', 4: 'bg-dark' };
  const fmtDate = d => d ? new Date(d).toLocaleDateString(I18N.lang, { dateStyle: 'medium' }) : '—';
  const money = (n, cur) => `${(n ?? 0).toLocaleString(I18N.lang, { minimumFractionDigits: 2, maximumFractionDigits: 2 })} ${cur || ''}`.trim();

  // ---- tabs ----
  const loaders = {}, loaded = {};
  function show(tab) {
    document.querySelectorAll('#clientTabs .nav-link').forEach(b => b.classList.toggle('active', b.dataset.tab === tab));
    document.querySelectorAll('.tab-pane').forEach(p => p.classList.toggle('d-none', p.dataset.pane !== tab));
    if (!loaded[tab]) { loaded[tab] = true; loaders[tab]?.(); }
    history.replaceState(null, '', `?id=${id}&tab=${tab}`);
  }
  document.querySelectorAll('#clientTabs .nav-link').forEach(b => b.onclick = () => show(b.dataset.tab));

  // ---- Overview (also sets the header) ----
  loaders.overview = async () => {
    let c; try { c = await API.get(`/clients/${id}`); } catch { document.getElementById('cdOverview').innerHTML = `<div class="text-danger">${I18N.t('common.error')}</div>`; return; }
    document.getElementById('clientName').textContent = c.name;
    document.getElementById('clientCompany').textContent = c.companyName || '';
    const stat = (label, val) => `<div class="col-6 col-md-3"><div class="card shadow-sm text-center"><div class="card-body py-3">
      <div class="h4 mb-0">${val}</div><div class="text-muted small">${label}</div></div></div></div>`;
    document.getElementById('cdStats').innerHTML =
      stat(I18N.t('cd.stProjects'), c.projectCount) + stat(I18N.t('cd.stOpen'), c.openProjectCount) +
      stat(I18N.t('cd.stInvoiced'), money(c.invoicedTotal)) + stat(I18N.t('cd.stHours'), c.hoursLogged);
    const row = (label, val) => val ? `<div class="col-md-6 mb-2"><div class="text-muted small">${label}</div><div>${UI.esc(String(val))}</div></div>` : '';
    document.getElementById('cdOverview').innerHTML = `<div class="row">
      ${row(I18N.t('cd.fEmail'), c.contactEmail)}${row(I18N.t('cd.fPhone'), c.phone)}
      ${row(I18N.t('cd.fWebsite'), c.website)}${row(I18N.t('cd.fAddress'), [c.address, c.city, c.country].filter(Boolean).join(', '))}
      ${c.notes ? `<div class="col-12 mt-2"><div class="text-muted small">${I18N.t('cd.fNotes')}</div><div style="white-space:pre-wrap">${UI.esc(c.notes)}</div></div>` : ''}
    </div>` || `<div class="text-muted">—</div>`;
  };

  // ---- Contacts ----
  loaders.contacts = async () => renderContacts();
  async function renderContacts() {
    const el = document.getElementById('cdContacts');
    el.innerHTML = `<div class="text-muted">${I18N.t('common.loading')}</div>`;
    let list = []; try { list = await API.get(`/clients/${id}/contacts`); } catch {}
    el.innerHTML = list.length ? list.map(x => `<div class="d-flex justify-content-between align-items-center border-bottom py-2">
        <div><span class="fw-semibold">${UI.esc(x.name)}</span>${x.isMain ? ` <span class="badge bg-primary">${I18N.t('cd.cMain')}</span>` : ''}
          ${x.position ? `<div class="text-muted small">${UI.esc(x.position)}</div>` : ''}</div>
        <div class="text-end small">
          <div>${UI.esc(x.email || '')}</div><div class="text-muted">${UI.esc(x.phone || '')}</div>
        </div>
        <button class="btn btn-sm btn-outline-danger ms-2" data-del="${x.id}">×</button></div>`).join('')
      : `<div class="text-muted">${I18N.t('cd.noContacts')}</div>`;
    el.querySelectorAll('[data-del]').forEach(b => b.onclick = async () => {
      if (!confirm(I18N.t('cli.confirmDelete'))) return;
      await API.del(`/clients/${id}/contacts/${b.dataset.del}`); renderContacts();
    });
  }
  const contactModal = new bootstrap.Modal(document.getElementById('contactModal'));
  document.getElementById('addContactBtn').onclick = () => { document.getElementById('contactForm').reset(); document.getElementById('contactError').innerHTML = ''; contactModal.show(); };
  document.getElementById('contactForm').onsubmit = async (e) => {
    e.preventDefault();
    const body = { name: document.getElementById('ct_name').value.trim(), email: document.getElementById('ct_email').value.trim() || null,
      phone: document.getElementById('ct_phone').value.trim() || null, position: document.getElementById('ct_position').value.trim() || null, isMain: document.getElementById('ct_main').checked };
    try { await API.post(`/clients/${id}/contacts`, body); contactModal.hide(); renderContacts(); }
    catch (ex) { document.getElementById('contactError').innerHTML = `<div class="alert alert-danger py-1">${ex.problem?.title || I18N.t('common.error')}</div>`; }
  };

  // ---- Projects ----
  loaders.projects = async () => {
    const el = document.getElementById('cdProjects');
    el.innerHTML = `<div class="text-muted">${I18N.t('common.loading')}</div>`;
    let list = []; try { list = await API.get(`/clients/${id}/projects`); } catch {}
    el.innerHTML = list.length ? `<div class="card shadow-sm"><div class="table-responsive"><table class="table table-sm align-middle mb-0">
      <thead><tr><th data-i18n="proj.name"></th><th data-i18n="proj.status"></th><th data-i18n="proj.dueDate"></th><th data-i18n="proj.tasks"></th></tr></thead>
      <tbody>${list.map(p => `<tr>
        <td><a href="/board.html?projectId=${p.id}" class="text-decoration-none">${UI.esc(p.name)}</a></td>
        <td>${I18N.t(PROJ_ST[p.status] || '')}</td><td>${fmtDate(p.dueDate)}</td><td>${p.taskCount}</td></tr>`).join('')}</tbody>
      </table></div></div>` : `<div class="text-muted">${I18N.t('proj.empty')}</div>`;
    I18N.translate(el);
  };

  // ---- Timesheets ----
  loaders.timesheets = async () => {
    const el = document.getElementById('cdTimesheets');
    el.innerHTML = `<div class="text-muted">${I18N.t('common.loading')}</div>`;
    let rows = []; try { rows = await API.get(`/clients/${id}/timesheet`); } catch {}
    if (!rows.length) { el.innerHTML = `<div class="text-muted">${I18N.t('cd.noTime')}</div>`; return; }
    const th = rows.reduce((s, r) => s + r.hours, 0), tb = rows.reduce((s, r) => s + r.billableHours, 0);
    el.innerHTML = `<table class="table table-sm align-middle mb-0">
      <thead><tr><th data-i18n="proj.name"></th><th class="text-end" data-i18n="cd.hours"></th><th class="text-end" data-i18n="cd.billable"></th></tr></thead>
      <tbody>${rows.map(r => `<tr><td>${UI.esc(r.projectName)}</td><td class="text-end">${r.hours.toFixed(1)}</td><td class="text-end">${r.billableHours.toFixed(1)}</td></tr>`).join('')}</tbody>
      <tfoot><tr class="fw-semibold border-top"><td data-i18n="common.total"></td><td class="text-end">${th.toFixed(1)}</td><td class="text-end">${tb.toFixed(1)}</td></tr></tfoot></table>`;
    I18N.translate(el);
  };

  // ---- Invoices ----
  loaders.invoices = async () => {
    const el = document.getElementById('cdInvoices');
    el.innerHTML = `<div class="text-muted">${I18N.t('common.loading')}</div>`;
    let list = []; try { list = await API.get(`/clients/${id}/invoices`); } catch {}
    el.innerHTML = list.length ? `<table class="table table-sm align-middle mb-0">
      <thead><tr><th data-i18n="inv.number"></th><th data-i18n="common.status"></th><th data-i18n="inv.issueDate"></th><th class="text-end" data-i18n="common.total"></th></tr></thead>
      <tbody>${list.map(i => `<tr>
        <td>${UI.esc(i.number)}</td><td><span class="badge ${INV_CLS[i.status] || 'bg-secondary'}">${I18N.t(INV_ST[i.status] || '')}</span></td>
        <td>${fmtDate(i.issueDate)}</td><td class="text-end">${money(i.total, i.currency)}</td></tr>`).join('')}</tbody></table>`
      : `<div class="text-muted">${I18N.t('inv.empty')}</div>`;
    I18N.translate(el);
  };

  document.addEventListener('lang-changed', () => location.reload());
  const initial = new URLSearchParams(location.search).get('tab');
  // overview must run first (sets the header) even if another tab is requested
  await loaders.overview(); loaded.overview = true;
  show(['contacts', 'projects', 'timesheets', 'invoices'].includes(initial) ? initial : 'overview');
})();
