(async function () {
  await I18N.load();
  if (!Auth.requireAuth()) return;
  await UI.mountNavbar('invoices');

  const STATUS = ['Draft', 'Sent', 'Paid', 'Overdue', 'Cancelled'];
  const STATUS_COLOR = ['secondary', 'info', 'success', 'danger', 'dark'];
  let clients = [];

  try { clients = await API.get('/clients'); } catch {}
  document.getElementById('i_client').innerHTML =
    `<option value="">${I18N.t('common.none')}</option>` +
    clients.map(c => `<option value="${c.id}">${UI.esc(c.name)}</option>`).join('');

  async function load() {
    const list = await API.get('/invoices');
    const body = document.getElementById('invBody');
    body.innerHTML = list.length ? list.map(i => `
      <tr>
        <td>${UI.esc(i.number)}</td>
        <td>${UI.esc(i.clientName || '—')}</td>
        <td><span class="badge bg-${STATUS_COLOR[i.status]}">${STATUS[i.status]}</span></td>
        <td class="small">${i.issueDate ? i.issueDate.slice(0,10) : ''}</td>
        <td class="text-end">${i.currency} ${i.total.toFixed(2)}</td>
        <td class="text-end text-nowrap">
          ${i.status === 0 ? `<button class="btn btn-sm btn-outline-info" data-sent="${i.id}">${I18N.t('inv.markSent')}</button>` : ''}
          ${i.status !== 2 ? `<button class="btn btn-sm btn-outline-success" data-paid="${i.id}">${I18N.t('inv.markPaid')}</button>` : ''}
          <button class="btn btn-sm btn-outline-danger" data-del="${i.id}">×</button>
        </td>
      </tr>`).join('') : `<tr><td colspan="6" class="text-muted text-center py-3">${I18N.t('inv.empty')}</td></tr>`;

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
      <td><button type="button" class="btn btn-sm btn-link text-danger l-del">×</button></td>`;
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
