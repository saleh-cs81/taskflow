(async function () {
  await I18N.load();
  if (!Auth.requireAuth()) return;
  await UI.mountNavbar('invoices');

  const STATUS = ['Draft', 'Sent', 'Paid', 'Overdue', 'Cancelled'];
  const STATUS_KEY = ['inv.draft', 'inv.sent', 'inv.paid', 'inv.overdue', 'inv.cancelled'];
  const STATUS_PILL = ['off', 'info', 'on', 'danger', 'off'];
  let clients = [];

  try { clients = await API.get('/clients'); } catch {}
  document.getElementById('i_client').innerHTML =
    `<option value="">${I18N.t('common.none')}</option>` +
    clients.map(c => `<option value="${c.id}">${UI.esc(c.name)}</option>`).join('');

  function statCard(label, value, icon, color) {
    return `<div class="col-6 col-md-4">
      <div class="stat-card d-flex align-items-center gap-3">
        <span class="stat-icon" style="${color || ''}"><i class="bi ${icon}"></i></span>
        <div>
          <div class="stat-value">${value}</div>
          <div class="stat-label">${label}</div>
        </div>
      </div></div>`;
  }

  function fmtMoney(currency, amount) {
    return `${UI.esc(currency || '')} ${Number(amount || 0).toFixed(2)}`.trim();
  }

  function renderStats(list) {
    const count = list.length;
    // outstanding = sum of total where status is Sent (1) or Overdue (3)
    const outstandingCur = (list.find(i => i.status === 1 || i.status === 3) || {}).currency || '';
    const outstanding = list
      .filter(i => i.status === 1 || i.status === 3)
      .reduce((s, i) => s + Number(i.total || 0), 0);
    const paid = list.filter(i => i.status === 2).length;
    document.getElementById('invStats').innerHTML =
      statCard(I18N.t('inv.statCount'), count, 'bi-receipt') +
      statCard(I18N.t('inv.statOutstanding'), fmtMoney(outstandingCur, outstanding), 'bi-hourglass-split', 'background:#fee2e2;color:#b91c1c') +
      statCard(I18N.t('inv.statPaid'), paid, 'bi-check2-circle', 'background:#e4f6f2;color:#0f9a80');
  }

  function invRow(i) {
    const pill = STATUS_PILL[i.status] || 'off';
    const statusLabel = I18N.t(STATUS_KEY[i.status]) || STATUS[i.status] || '';
    return `<tr>
      <td class="fw-semibold">${UI.esc(i.number)}</td>
      <td class="text-truncate" style="max-width:220px">${UI.esc(i.clientName) || I18N.t('common.none')}</td>
      <td><span class="status-pill ${pill}">${UI.esc(statusLabel)}</span></td>
      <td class="text-muted small">${i.issueDate ? UI.esc(i.issueDate.slice(0, 10)) : ''}</td>
      <td class="text-end fw-semibold text-nowrap">${fmtMoney(i.currency, i.total)}</td>
      <td class="text-end text-nowrap">
        <div class="d-inline-flex gap-1">
          ${i.status === 0 ? `<button class="btn btn-sm btn-outline-info" data-sent="${i.id}" title="${I18N.t('inv.markSent')}"><i class="bi bi-send"></i></button>` : ''}
          ${i.status !== 2 ? `<button class="btn btn-sm btn-outline-success" data-paid="${i.id}" title="${I18N.t('inv.markPaid')}"><i class="bi bi-check2"></i></button>` : ''}
          <button class="btn btn-sm btn-outline-danger" data-del="${i.id}" title="${I18N.t('common.delete')}"><i class="bi bi-trash"></i></button>
        </div>
      </td>
    </tr>`;
  }

  async function load() {
    const list = await API.get('/invoices');
    renderStats(list);
    const body = document.getElementById('invBody');
    body.innerHTML = list.length
      ? list.map(invRow).join('')
      : `<tr><td colspan="6"><div class="empty-state"><i class="bi bi-receipt es-icon"></i><div class="es-text">${I18N.t('inv.empty')}</div></div></td></tr>`;

    body.querySelectorAll('[data-sent]').forEach(b => b.onclick = async () => { await API.post(`/invoices/${b.dataset.sent}/status?status=1`); load(); });
    body.querySelectorAll('[data-paid]').forEach(b => b.onclick = async () => { await API.post(`/invoices/${b.dataset.paid}/status?status=2`); load(); });
    body.querySelectorAll('[data-del]').forEach(b => b.onclick = async () => { await API.del(`/invoices/${b.dataset.del}`); load(); });
  }

  // line items
  const lineBody = document.getElementById('lineBody');
  function addLine(desc = '', qty = 1, price = 0) {
    const tr = document.createElement('tr');
    tr.innerHTML = `
      <td><input class="form-control form-control-sm l-desc" value="${UI.esc(desc)}"></td>
      <td><input type="number" step="0.01" class="form-control form-control-sm l-qty" value="${qty}" style="width:80px"></td>
      <td><input type="number" step="0.01" class="form-control form-control-sm l-price" value="${price}" style="width:110px"></td>
      <td><button type="button" class="btn btn-sm btn-link text-danger l-del">&times;</button></td>`;
    lineBody.appendChild(tr);
    tr.querySelector('.l-del').onclick = () => { tr.remove(); recalc(); };
    tr.querySelectorAll('input').forEach(i => i.oninput = recalc);
    recalc();
  }
  function recalc() {
    let sub = 0;
    lineBody.querySelectorAll('tr').forEach(tr => {
      const q = parseFloat(tr.querySelector('.l-qty').value) || 0;
      const p = parseFloat(tr.querySelector('.l-price').value) || 0;
      sub += q * p;
    });
    const tax = sub * (parseFloat(document.getElementById('i_tax').value) || 0) / 100;
    document.getElementById('invTotal').textContent = (sub + tax).toFixed(2);
  }
  document.getElementById('addLine').onclick = () => addLine();
  document.getElementById('i_tax').oninput = recalc;

  const modal = new bootstrap.Modal(document.getElementById('invModal'));
  document.getElementById('invModal').addEventListener('show.bs.modal', () => {
    if (!lineBody.children.length) addLine();
  });

  document.getElementById('invForm').onsubmit = async (e) => {
    e.preventDefault();
    const items = [...lineBody.querySelectorAll('tr')].map(tr => ({
      description: tr.querySelector('.l-desc').value.trim(),
      quantity: parseFloat(tr.querySelector('.l-qty').value) || 0,
      unitPrice: parseFloat(tr.querySelector('.l-price').value) || 0
    })).filter(i => i.description);
    if (!items.length) { document.getElementById('invErr').innerHTML = `<div class="alert alert-danger py-1">${I18N.t('inv.addLine')}</div>`; return; }
    const issue = document.getElementById('i_issue').value;
    const due = document.getElementById('i_due').value;
    const clientId = document.getElementById('i_client').value;
    const body = {
      clientId: clientId ? parseInt(clientId, 10) : null,
      issueDate: issue ? `${issue}T00:00:00Z` : new Date().toISOString(),
      dueDate: due ? `${due}T00:00:00Z` : null,
      currency: 'USD', notes: null,
      taxRate: parseFloat(document.getElementById('i_tax').value) || 0,
      items
    };
    try {
      await API.post('/invoices', body);
      document.getElementById('invForm').reset(); lineBody.innerHTML = ''; modal.hide(); load();
    } catch (ex) { document.getElementById('invErr').innerHTML = `<div class="alert alert-danger py-1">${ex.problem?.title || I18N.t('common.error')}</div>`; }
  };

  document.addEventListener('lang-changed', () => location.reload());
  await load();
})();
