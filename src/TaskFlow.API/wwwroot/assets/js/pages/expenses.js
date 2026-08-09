(async function () {
  await I18N.load();
  if (!Auth.requireAuth()) return;
  await UI.mountNavbar('expenses');

  let clients = [];
  try { clients = await API.get('/clients'); } catch {}
  const clientName = (id) => {
    const c = clients.find(x => String(x.id) === String(id));
    return c ? c.name : '';
  };
  document.getElementById('x_client').innerHTML =
    `<option value="">${I18N.t('common.none')}</option>` +
    clients.map(c => `<option value="${c.id}">${UI.esc(c.name)}</option>`).join('');

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

  function money(cur, amount) {
    return `${UI.esc(cur || '')} ${Number(amount || 0).toFixed(2)}`.trim();
  }

  function renderStats(list) {
    const cur = list.length ? list[0].currency : 'USD';
    const total = list.reduce((s, e) => s + Number(e.amount || 0), 0);
    const billable = list.filter(e => e.isBillable);
    const billableTotal = billable.reduce((s, e) => s + Number(e.amount || 0), 0);
    document.getElementById('expStats').innerHTML =
      statCard(I18N.t('exp.statTotal'), money(cur, total), 'bi-cash-stack') +
      statCard(I18N.t('exp.statBillable'), money(cur, billableTotal), 'bi-receipt', 'background:#e7f1fe;color:#1d6fd6') +
      statCard(I18N.t('exp.statBillableCount'), billable.length, 'bi-check2-circle', 'background:#e4f6f2;color:#0f9a80') +
      statCard(I18N.t('exp.statCount'), list.length, 'bi-list-ul', 'background:#fef3c7;color:#b45309');
  }

  function statusPill(e) {
    return e.isBillable
      ? `<span class="status-pill info">${I18N.t('exp.billable')}</span>`
      : `<span class="soft-badge gray">${I18N.t('exp.nonBillable')}</span>`;
  }

  function expRow(e) {
    const client = clientName(e.clientId);
    return `<tr>
      <td class="text-nowrap small">${e.date ? UI.esc(e.date.slice(0, 10)) : ''}</td>
      <td><span class="soft-badge">${UI.esc(e.category)}</span></td>
      <td class="min-w-0"><span class="text-truncate d-inline-block" style="max-width:280px">${UI.esc(e.description || '')}</span></td>
      <td class="small text-muted">${client ? UI.esc(client) : I18N.t('common.none')}</td>
      <td>${statusPill(e)}</td>
      <td class="text-end fw-semibold text-nowrap">${money(e.currency, e.amount)}</td>
      <td class="text-end"><button class="btn btn-sm btn-outline-danger" data-del="${e.id}" title="${I18N.t('common.delete')}"><i class="bi bi-trash"></i></button></td>
    </tr>`;
  }

  async function load() {
    const list = await API.get('/expenses');
    document.getElementById('expCount').textContent = list.length;
    renderStats(list);
    const body = document.getElementById('expBody');
    body.innerHTML = list.length
      ? list.map(expRow).join('')
      : `<tr><td colspan="7" class="p-0">
           <div class="empty-state"><i class="bi bi-cash-stack es-icon"></i><div class="es-text">${I18N.t('exp.empty')}</div></div>
         </td></tr>`;
    body.querySelectorAll('[data-del]').forEach(b => b.onclick = async () => { await API.del(`/expenses/${b.dataset.del}`); load(); });
  }

  const modal = new bootstrap.Modal(document.getElementById('expModal'));
  document.getElementById('expForm').onsubmit = async (ev) => {
    ev.preventDefault();
    const date = document.getElementById('x_date').value;
    const clientId = document.getElementById('x_client').value;
    const body = {
      projectId: null,
      clientId: clientId ? parseInt(clientId, 10) : null,
      category: document.getElementById('x_cat').value.trim(),
      description: document.getElementById('x_desc').value.trim() || null,
      amount: parseFloat(document.getElementById('x_amount').value) || 0,
      currency: 'USD',
      date: date ? `${date}T00:00:00Z` : new Date().toISOString(),
      isBillable: document.getElementById('x_billable').checked
    };
    try { await API.post('/expenses', body); document.getElementById('expForm').reset(); modal.hide(); load(); }
    catch (ex) { document.getElementById('expErr').innerHTML = `<div class="alert alert-danger py-1">${ex.problem?.title || I18N.t('common.error')}</div>`; }
  };

  document.addEventListener('lang-changed', () => location.reload());
  await load();
})();
