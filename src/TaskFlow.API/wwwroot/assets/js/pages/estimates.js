(async function () {
  await I18N.load();
  if (!Auth.requireAuth()) return;
  await UI.mountNavbar('estimates');

  const STATUS = ['Draft', 'Sent', 'Accepted', 'Rejected', 'Expired'];
  const STATUS_COLOR = ['secondary', 'info', 'success', 'danger', 'dark'];
  let clients = [];

  try { clients = await API.get('/clients'); } catch {}
  document.getElementById('e_client').innerHTML =
    `<option value="">${I18N.t('common.none')}</option>` +
    clients.map(c => `<option value="${c.id}">${UI.esc(c.name)}</option>`).join('');

  async function load() {
    const list = await API.get('/estimates');
    const body = document.getElementById('estBody');
    body.innerHTML = list.length ? list.map(e => `
      <tr>
        <td>${UI.esc(e.number)}</td>
        <td>${UI.esc(e.clientName || '—')}</td>
        <td><span class="badge bg-${STATUS_COLOR[e.status]}">${STATUS[e.status]}</span></td>
        <td class="small">${e.issueDate ? e.issueDate.slice(0,10) : ''}</td>
        <td class="text-end">${e.currency} ${e.total.toFixed(2)}</td>
        <td class="text-end text-nowrap">
          ${e.status === 0 ? `<button class="btn btn-sm btn-outline-info" data-sent="${e.id}">${I18N.t('inv.markSent')}</button>` : ''}
          ${e.status !== 2 ? `<button class="btn btn-sm btn-outline-success" data-accept="${e.id}">${I18N.t('est.accept')}</button>` : ''}
          <button class="btn btn-sm btn-outline-primary" data-conv="${e.id}">${I18N.t('est.convert')}</button>
          <button class="btn btn-sm btn-outline-danger" data-del="${e.id}">×</button>
        </td>
      </tr>`).join('') : `<tr><td colspan="6" class="text-muted text-center py-3">${I18N.t('est.empty')}</td></tr>`;

    body.querySelectorAll('[data-sent]').forEach(b => b.onclick = async () => { await API.post(`/estimates/${b.dataset.sent}/status?status=1`); load(); });
    body.querySelectorAll('[data-accept]').forEach(b => b.onclick = async () => { await API.post(`/estimates/${b.dataset.accept}/status?status=2`); load(); });
    body.querySelectorAll('[data-conv]').forEach(b => b.onclick = async () => { await API.post(`/estimates/${b.dataset.conv}/convert`); alert('→ ' + I18N.t('inv.title')); load(); });
    body.querySelectorAll('[data-del]').forEach(b => b.onclick = async () => { await API.del(`/estimates/${b.dataset.del}`); load(); });
  }

  const lineBody = document.getElementById('eLineBody');
  function addLine() {
    const tr = document.createElement('tr');
    tr.innerHTML = `
      <td><input class="form-control form-control-sm l-desc"></td>
      <td><input type="number" step="0.01" class="form-control form-control-sm l-qty" value="1" style="width:80px"></td>
      <td><input type="number" step="0.01" class="form-control form-control-sm l-price" value="0" style="width:110px"></td>
      <td><button type="button" class="btn btn-sm btn-link text-danger l-del">×</button></td>`;
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

  document.addEventListener('lang-changed', () => location.reload());
  await load();
})();
