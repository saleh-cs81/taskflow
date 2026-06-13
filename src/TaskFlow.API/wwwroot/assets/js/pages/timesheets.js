(async function () {
  await I18N.load();
  if (!Auth.requireAuth()) return;
  await UI.mountNavbar('timesheets');

  const STATUS = ['Draft', 'Submitted', 'Approved', 'Rejected'];
  const STATUS_COLOR = ['secondary', 'info', 'success', 'danger'];
  const canApprove = (API.currentUser()?.permissions || []).includes('time.approve');

  async function load() {
    const list = await API.get('/timesheets');
    const body = document.getElementById('tsBody');
    body.innerHTML = list.length ? list.map(t => `
      <tr>
        <td class="small">${t.periodStart.slice(0,10)} → ${t.periodEnd.slice(0,10)}</td>
        <td><span class="badge bg-${STATUS_COLOR[t.status]}">${STATUS[t.status]}</span></td>
        <td class="text-end">${t.totalHours.toFixed(2)}</td>
        <td class="text-end text-nowrap">
          ${t.status === 0 || t.status === 3 ? `<button class="btn btn-sm btn-outline-info" data-submit="${t.id}">${I18N.t('ts.submit')}</button>` : ''}
          ${canApprove && t.status === 1 ? `<button class="btn btn-sm btn-outline-success" data-approve="${t.id}">${I18N.t('ts.approve')}</button>
            <button class="btn btn-sm btn-outline-danger" data-reject="${t.id}">${I18N.t('ts.reject')}</button>` : ''}
        </td>
      </tr>`).join('') : `<tr><td colspan="4" class="text-muted text-center py-3">${I18N.t('ts.empty')}</td></tr>`;

    body.querySelectorAll('[data-submit]').forEach(b => b.onclick = async () => { await API.post(`/timesheets/${b.dataset.submit}/submit`); load(); });
    body.querySelectorAll('[data-approve]').forEach(b => b.onclick = async () => { await API.post(`/timesheets/${b.dataset.approve}/approve`, { reviewNote: null }); load(); });
    body.querySelectorAll('[data-reject]').forEach(b => b.onclick = async () => { await API.post(`/timesheets/${b.dataset.reject}/reject`, { reviewNote: null }); load(); });
  }

  document.getElementById('tsForm').onsubmit = async (e) => {
    e.preventDefault();
    const from = document.getElementById('ts_from').value;
    const to = document.getElementById('ts_to').value;
    try {
      await API.post('/timesheets', { periodStart: `${from}T00:00:00Z`, periodEnd: `${to}T00:00:00Z` });
      document.getElementById('tsForm').reset(); load();
    } catch (ex) { document.getElementById('tsErr').innerHTML = `<div class="alert alert-danger py-1">${ex.problem?.title || I18N.t('common.error')}</div>`; }
  };

  document.addEventListener('lang-changed', () => location.reload());
  await load();
})();
