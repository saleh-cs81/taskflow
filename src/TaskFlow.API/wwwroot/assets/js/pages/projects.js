(async function () {
  await I18N.load();
  if (!Auth.requireAuth()) return;
  await UI.mountNavbar('projects');

  const STATUS = ['Planned', 'Active', 'On hold', 'Completed', 'Archived', 'Cancelled'];
  const STATUS_COLOR = ['secondary', 'success', 'warning', 'primary', 'dark', 'danger'];
  let clients = [];

  async function loadClients() {
    try { clients = await API.get('/clients'); } catch { clients = []; }
    document.getElementById('p_client').innerHTML =
      `<option value="">${I18N.t('common.none')}</option>` +
      clients.map(c => `<option value="${c.id}">${UI.esc(c.name)}</option>`).join('');
  }

  async function load() {
    const data = await API.get('/projects?pageSize=100');
    const grid = document.getElementById('projGrid');
    if (!data.items.length) { grid.innerHTML = `<div class="text-muted">${I18N.t('proj.empty')}</div>`; return; }
    grid.innerHTML = data.items.map(p => `
      <div class="col-md-4">
        <div class="card shadow-sm h-100">
          <div class="card-body">
            <div class="d-flex justify-content-between align-items-start">
              <h6 class="mb-1">${UI.esc(p.name)}</h6>
              <span class="badge bg-${STATUS_COLOR[p.status] || 'secondary'}">${STATUS[p.status] || ''}</span>
            </div>
            ${p.code ? `<div class="text-muted small mb-2">${UI.esc(p.code)}</div>` : ''}
            <div class="text-muted small mb-3">${I18N.t('team.members')}: ${p.memberCount ?? 0}</div>
            <a class="btn btn-sm btn-outline-primary" href="/board.html?projectId=${p.id}">${I18N.t('dash.openBoard')}</a>
          </div>
        </div>
      </div>`).join('');
  }

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
  await load();
})();
