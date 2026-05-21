// Shared chrome for authenticated pages: navbar, language toggle, notifications.
const UI = {
  async mountNavbar(active) {
    const user = API.currentUser();
    const el = document.getElementById('navbar');
    el.innerHTML = `
      <nav class="navbar navbar-expand navbar-dark bg-primary px-3">
        <a class="navbar-brand" href="/dashboard.html" data-i18n="app.name"></a>
        <div class="navbar-nav me-auto">
          <a class="nav-link ${active==='dashboard'?'active':''}" href="/dashboard.html" data-i18n="nav.dashboard"></a>
          <a class="nav-link ${active==='board'?'active':''}" href="/board.html" data-i18n="nav.board"></a>
          <a class="nav-link ${active==='reports'?'active':''}" href="/reports.html" data-i18n="nav.reports"></a>
          <a class="nav-link ${active==='team'?'active':''}" href="/team.html" data-i18n="nav.team"></a>
          <a class="nav-link ${active==='integration'?'active':''}" href="/integration.html" data-i18n="nav.integration"></a>
        </div>
        <div class="d-flex align-items-center gap-2">
          <div class="dropdown">
            <button class="btn btn-outline-light position-relative" id="notifBtn" data-bs-toggle="dropdown" aria-expanded="false">
              🔔<span class="badge bg-danger notif-badge d-none" id="notifBadge">0</span>
            </button>
            <div class="dropdown-menu dropdown-menu-end p-2" style="min-width:300px" id="notifMenu">
              <div class="d-flex justify-content-between align-items-center mb-1">
                <strong data-i18n="notif.title"></strong>
                <button class="btn btn-sm btn-link" id="notifMarkAll" data-i18n="notif.markAll"></button>
              </div>
              <div id="notifList" class="small"></div>
            </div>
          </div>
          <button class="btn btn-outline-light btn-sm" id="langToggle" data-i18n="lang.toggle"></button>
          <span class="navbar-text text-white-50 small">${user?.fullName ?? ''}</span>
          <button class="btn btn-outline-light btn-sm" id="logoutBtn" data-i18n="nav.logout"></button>
        </div>
      </nav>`;

    I18N.translate(el);
    document.getElementById('logoutBtn').onclick = () => Auth.logout();
    document.getElementById('langToggle').onclick = () => I18N.toggle();
    document.getElementById('notifMarkAll').onclick = async () => { await API.post('/notifications/read-all'); this.refreshNotifications(); };

    document.addEventListener('lang-changed', () => I18N.translate(el));

    await this.refreshNotifications();
    const conn = await Realtime.connect('notifications');
    if (conn) conn.on('notification', () => this.refreshNotifications());
  },

  async refreshNotifications() {
    try {
      const [list, unread] = await Promise.all([
        API.get('/notifications'),
        API.get('/notifications/unread-count')
      ]);
      const badge = document.getElementById('notifBadge');
      if (unread.count > 0) { badge.textContent = unread.count; badge.classList.remove('d-none'); }
      else badge.classList.add('d-none');

      const listEl = document.getElementById('notifList');
      listEl.innerHTML = list.length
        ? list.map(n => `<a href="${n.linkUrl||'#'}" class="dropdown-item notif-item ${n.isRead?'':'unread'}" data-id="${n.id}">
             <div class="fw-semibold">${this.esc(n.title)}</div>
             <div class="text-muted small">${this.esc(n.body||'')}</div></a>`).join('')
        : `<div class="text-muted text-center py-2" data-i18n="notif.empty">${I18N.t('notif.empty')}</div>`;

      listEl.querySelectorAll('.notif-item').forEach(a =>
        a.addEventListener('click', () => API.post(`/notifications/${a.dataset.id}/read`).catch(()=>{})));
    } catch {}
  },

  esc(s) { const d = document.createElement('div'); d.textContent = s; return d.innerHTML; }
};

window.UI = UI;
