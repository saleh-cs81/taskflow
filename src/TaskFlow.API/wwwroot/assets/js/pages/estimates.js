(async function () {
  await I18N.load();
  if (!Auth.requireAuth()) return;
  await UI.mountNavbar('estimates');

  // Real estimate statuses (index = status code from the API). Preserved from the data contract.
  const STATUS = ['Draft', 'Sent', 'Accepted', 'Rejected', 'Expired'];
  const STATUS_KEY = ['est.stDraft', 'est.stSent', 'est.stAccepted', 'est.stRejected', 'est.stExpired'];
  const STATUS_PILL = ['off', 'info', 'on', 'danger', 'warn'];

  let clients = [];
  let estimates = [];

  try { clients = await API.get('/clients'); } catch {}
  document.getElementById('e_client').innerHTML =
    `<option value="">${I18N.t('common.none')}</option>` +
    clients.map(c => `<option value="${c.id}">${UI.esc(c.name)}</option>`).join('');

  function statusLabel(s) {
    return I18N.t(STATUS_KEY[s]) || STATUS[s] || '';
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
    const accepted = estimates.filter(e => e.status === 2).length;
    const sent = estimates.filter(e => e.status === 1).length;
    const currency = estimates.length ? (estimates[0].currency || '') : '';
    const value = estimates.reduce((sum, e) => sum + (e.total || 0), 0);
    document.getElementById('estStats').innerHTML =
      statCard(I18N.t('est.statTotal'), estimates.length, 'bi-file-earmark-text') +
      statCard(I18N.t('est.statAccepted'), accepted, 'bi-check-circle', 'background:#e4f6f2;color:#0f9a80') +
      statCard(I18N.t('est.statSent'), sent, 'bi-send', 'background:#e7f1fe;color:#1d6fd6') +
      statCard(I18N.t('est.statValue'), `${UI.esc(currency)} ${value.toFixed(2)}`, 'bi-cash-coin', 'background:#fef3c7;color:#b45309');
  }

  function estRow(e) {
    const pill = STATUS_PILL[e.status] || 'off';
    return `<tr data-id="${e.id}">
      <td class="fw-semibold">${UI.esc(e.number)}</td>
      <td class="text-truncate" style="max-width:200px">${UI.esc(e.clientName || I18N.t('common.none'))}</td>
      <td><span class="status-pill ${pill}">${UI.esc(statusLabel(e.status))}</span></td>
      <td class="small text-muted">${e.issueDate ? UI.esc(e.issueDate.slice(0, 10)) : ''}</td>
      <td class="small text-muted">${e.expiryDate ? UI.esc(e.expiryDate.slice(0, 10)) : ''}</td>
      <td class="text-end fw-semibold">${UI.esc(e.currency)} ${e.total.toFixed(2)}</td>
      <td class="text-end text-nowrap">
        ${e.status === 0 ? `<button class="btn btn-sm btn-outline-info" data-sent="${e.id}" title="${I18N.t('inv.markSent')}"><i class="bi bi-send"></i></button>` : ''}
        ${e.status !== 2 ? `<button class="btn btn-sm btn-outline-success" data-accept="${e.id}" title="${I18N.t('est.accept')}"><i class="bi bi-check-lg"></i></button>` : ''}
        <button class="btn btn-sm btn-outline-primary" data-conv="${e.id}" title="${I18N.t('est.convert')}"><i class="bi bi-arrow-left-right flip-rtl"></i></button>
        <button class="btn btn-sm btn-outline-danger" data-del="${e.id}" title="${I18N.t('common.delete')}"><i class="bi bi-trash"></i></button>
      </td>
    </tr>`;
  }

  function render() {
    const q = document.getElementById('estSearch').value.trim().toLowerCase();
    const list = q
      ? estimates.filter(e =>
          (e.number || '').toLowerCase().includes(q) ||
          (e.clientName || '').toLowerCase().includes(q))
      : estimates;

    document.getElementById('estCount').textContent = estimates.length;
    const body = document.getElementById('estBody');
    const empty = document.getElementById('estEmpty');

    if (!list.length) {
      body.innerHTML = '';
      const msg = estimates.length ? I18N.t('est.noMatch') : I18N.t('est.empty');
      empty.innerHTML = `<div class="empty-state"><i class="bi bi-file-earmark-text es-icon"></i><div class="es-text">${UI.esc(msg)}</div></div>`;
    } else {
      empty.innerHTML = '';
      body.innerHTML = list.map(estRow).join('');
    }

    body.querySelectorAll('[data-sent]').forEach(b => b.onclick = async () => { await API.post(`/estimates/${b.dataset.sent}/status?status=1`); load(); });
    body.querySelectorAll('[data-accept]').forEach(b => b.onclick = async () => { await API.post(`/estimates/${b.dataset.accept}/status?status=2`); load(); });
    body.querySelectorAll('[data-conv]').forEach(b => b.onclick = async () => { await API.post(`/estimates/${b.dataset.conv}/convert`); alert(I18N.t('est.converted')); load(); });
    body.querySelectorAll('[data-del]').forEach(b => b.onclick = async () => { await API.del(`/estimates/${b.dataset.del}`); load(); });
  }

  async function load() {
    estimates = await API.get('/estimates');
    render();
    renderStats();
  }

  // ----- Create estimate: line items + math (preserved exactly) -----
  const lineBody = document.getElementById('eLineBody');
  function addLine() {
    const tr = document.createElement('tr');
    tr.innerHTML = `
      <td><input class="form-control form-control-sm l-desc"></td>
      <td><input type="number" step="0.01" class="form-control form-control-sm l-qty" value="1" style="width:80px"></td>
      <td><input type="number" step="0.01" class="form-control form-control-sm l-price" value="0" style="width:110px"></td>
      <td><button type="button" class="btn btn-sm btn-link text-danger l-del">&times;</button></td>`;
    lineBody.appendChild(tr);
    tr.querySelector('.l-del').onclick = () => { tr.remove(); recalc(); };
    tr.querySelectorAll('input').forEach(i => i.oninput = recalc);
    recalc();
  }
  function recalc() {
    let sub = 0;
    lineBody.querySelectorAll('tr').forEach(tr => {
      sub += (parseFloat(tr.querySelector('.l-qty').value) || 0) * (parseFloat(tr.querySelector('.l-price').value) || 0);
    });
    const tax = sub * (parseFloat(document.getElementById('e_tax').value) || 0) / 100;
    document.getElementById('estTotal').textContent = (sub + tax).toFixed(2);
  }
  document.getElementById('eAddLine').onclick = addLine;
  document.getElementById('e_tax').oninput = recalc;
  document.getElementById('estModal').addEventListener('show.bs.modal', () => { if (!lineBody.children.length) addLine(); });

  const modal = new bootstrap.Modal(document.getElementById('estModal'));
  document.getElementById('estForm').onsubmit = async (ev) => {
    ev.preventDefault();
    const items = [...lineBody.querySelectorAll('tr')].map(tr => ({
      description: tr.querySelector('.l-desc').value.trim(),
      quantity: parseFloat(tr.querySelector('.l-qty').value) || 0,
      unitPrice: parseFloat(tr.querySelector('.l-price').value) || 0
    })).filter(i => i.description);
    if (!items.length) { document.getElementById('estErr').innerHTML = `<div class="alert alert-danger py-1">${I18N.t('inv.addLine')}</div>`; return; }
    const issue = document.getElementById('e_issue').value;
    const expiry = document.getElementById('e_expiry').value;
    const clientId = document.getElementById('e_client').value;
    const body = {
      clientId: clientId ? parseInt(clientId, 10) : null,
      issueDate: issue ? `${issue}T00:00:00Z` : new Date().toISOString(),
      expiryDate: expiry ? `${expiry}T00:00:00Z` : null,
      currency: 'USD', notes: null,
      taxRate: parseFloat(document.getElementById('e_tax').value) || 0,
      items
    };
    try { await API.post('/estimates', body); document.getElementById('estForm').reset(); lineBody.innerHTML = ''; modal.hide(); load(); }
    catch (ex) { document.getElementById('estErr').innerHTML = `<div class="alert alert-danger py-1">${ex.problem?.title || I18N.t('common.error')}</div>`; }
  };

  document.getElementById('estSearch').oninput = render;
  document.addEventListener('lang-changed', () => location.reload());
  await load();
})();
