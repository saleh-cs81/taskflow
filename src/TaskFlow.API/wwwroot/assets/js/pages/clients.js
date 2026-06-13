(async function () {
  await I18N.load();
  if (!Auth.requireAuth()) return;
  await UI.mountNavbar('clients');

  const tbody = document.getElementById('clientsBody');

  async function load() {
    const list = await API.get('/clients');
    tbody.innerHTML = list.length
      ? list.map(c => `<tr>
          <td>${UI.esc(c.name)}</td>
          <td>${UI.esc(c.companyName || '')}</td>
          <td>${UI.esc(c.contactEmail || '')}</td>
          <td>${UI.esc(c.phone || '')}</td>
          <td class="text-end"><button class="btn btn-sm btn-outline-danger" data-del="${c.id}">×</button></td>
        </tr>`).join('')
      : `<tr><td colspan="5" class="text-muted text-center py-3">${I18N.t('cli.empty')}</td></tr>`;

    tbody.querySelectorAll('[data-del]').forEach(b => b.onclick = async () => {
      if (!confirm(I18N.t('cli.confirmDelete'))) return;
      await API.del(`/clients/${b.dataset.del}`); await load();
    });
  }

  const modalEl = document.getElementById('clientModal');
  const modal = new bootstrap.Modal(modalEl);
  document.getElementById('clientForm').onsubmit = async (e) => {
    e.preventDefault();
    const body = {
      name: document.getElementById('cl_name').value.trim(),
      companyName: document.getElementById('cl_company').value.trim() || null,
      contactEmail: document.getElementById('cl_email').value.trim() || null,
      phone: document.getElementById('cl_phone').value.trim() || null,
      notes: document.getElementById('cl_notes').value.trim() || null
    };
    try {
      await API.post('/clients', body);
      document.getElementById('clientForm').reset();
      modal.hide();
      await load();
    } catch (ex) {
      document.getElementById('clientError').innerHTML = `<div class="alert alert-danger py-1">${ex.problem?.title || I18N.t('common.error')}</div>`;
    }
  };

  document.addEventListener('lang-changed', () => location.reload());
  await load();
})();
