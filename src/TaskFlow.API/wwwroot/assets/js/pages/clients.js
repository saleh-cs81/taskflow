(async function () {
  await I18N.load();
  if (!Auth.requireAuth()) return;
  await UI.mountNavbar('clients');

  let clients = [];

  const modalEl = document.getElementById('clientModal');
  const modal = new bootstrap.Modal(modalEl);

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
    document.getElementById('clientStats').innerHTML =
      statCard(I18N.t('cli.statTotal'), clients.length, 'bi-people');
  }

  function clientRow(c) {
    const company = c.companyName
      ? `<span class="soft-badge gray">${UI.esc(c.companyName)}</span>`
      : '';
    const email = c.contactEmail
      ? `<span class="text-truncate"><i class="bi bi-envelope"></i> ${UI.esc(c.contactEmail)}</span>`
      : '';
    const phone = c.phone
      ? `<span class="text-truncate"><i class="bi bi-telephone"></i> ${UI.esc(c.phone)}</span>`
      : '';
    const contact = (email || phone)
      ? `<div class="text-muted small text-truncate mt-1 d-flex flex-wrap gap-3">${email}${phone}</div>`
      : '';
    return `<div class="list-row" data-id="${c.id}">
      ${UI.avatar(c.name, c.id, 'lg')}
      <div class="flex-grow-1 min-w-0">
        <div class="text-truncate">
          <a href="/client-detail.html?id=${c.id}" class="fw-semibold text-decoration-none">${UI.esc(c.name)}</a>
          ${company ? ' ' + company : ''}
        </div>
        ${contact}
      </div>
      <div class="d-flex gap-1 flex-none">
        <button class="btn btn-sm btn-outline-danger" data-del="${c.id}" title="${I18N.t('common.delete')}"><i class="bi bi-trash"></i></button>
      </div>
    </div>`;
  }

  function render() {
    document.getElementById('clientCount').textContent = clients.length;
    const el = document.getElementById('clientsList');
    el.innerHTML = clients.length
      ? clients.map(clientRow).join('')
      : `<div class="empty-state"><i class="bi bi-people es-icon"></i><div class="es-text">${I18N.t('cli.empty')}</div></div>`;

    el.querySelectorAll('[data-del]').forEach(b => b.onclick = async () => {
      if (!confirm(I18N.t('cli.confirmDelete'))) return;
      await API.del(`/clients/${b.dataset.del}`);
      await load();
    });
    renderStats();
  }

  async function load() {
    clients = await API.get('/clients');
    render();
  }

  document.getElementById('newClientBtn').onclick = () => {
    document.getElementById('clientForm').reset();
    document.getElementById('clientError').innerHTML = '';
    modal.show();
  };

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
