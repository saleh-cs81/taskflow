(async function () {
  await I18N.load();
  if (!Auth.requireAuth()) return;
  await UI.mountNavbar('expenses');

  let clients = [];
  try { clients = await API.get('/clients'); } catch {}
  document.getElementById('x_client').innerHTML =
    `<option value="">${I18N.t('common.none')}</option>` +
    clients.map(c => `<option value="${c.id}">${UI.esc(c.name)}</option>`).join('');

  async function load() {
    const list = await API.get('/expenses');
    const body = document.getElementById('expBody');
    body.innerHTML = list.length ? list.map(e => `
      <tr>
        <td>${UI.esc(e.category)}</td>
        <td>${UI.esc(e.description || '')}</td>
        <td class="small">${e.date ? e.date.slice(0,10) : ''}</td>
        <td class="text-end">${e.currency} ${e.amount.toFixed(2)}</td>
        <td>${e.isBillable ? '✓' : ''}</td>
        <td class="text-end"><button class="btn btn-sm btn-outline-danger" data-del="${e.id}">×</button></td>
      </tr>`).join('') : `<tr><td colspan="6" class="text-muted text-center py-3">${I18N.t('exp.empty')}</td></tr>`;
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
