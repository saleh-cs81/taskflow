// Shared app shell (Paymo-style): left sidebar + topbar, notifications, language.
const UI = {
  async mountNavbar(active) {
    const user = API.currentUser();

    // Inject Bootstrap Icons once (so pages don't each need the <link>).
    if (!document.getElementById('bi-css')) {
      const l = document.createElement('link');
      l.id = 'bi-css'; l.rel = 'stylesheet';
      l.href = 'https://cdn.jsdelivr.net/npm/bootstrap-icons@1.11.3/font/bootstrap-icons.min.css';
      document.head.appendChild(l);
    }

    document.body.classList.add('app-shell');
    const el = document.getElementById('navbar');

    const link = (key, href, icon, i18n) =>
      `<a class="side-link ${active === key ? 'active' : ''}" href="${href}">
         <i class="bi ${icon}"></i><span data-i18n="${i18n}">${I18N.t(i18n)}</span></a>`;

    const initials = (user?.fullName || '?').split(' ').map(s => s[0]).slice(0, 2).join('').toUpperCase();

    el.innerHTML = `
      <aside class="app-sidebar" id="appSidebar">
        <a class="brand" href="/home.html">
          <span class="logo">T</span><span data-i18n="app.name">${I18N.t('app.name')}</span>
        </a>

        <div class="nav-group" data-group="home">
          <button class="nav-group-toggle"><i class="bi bi-house"></i>
            <span data-i18n="nav.home">${I18N.t('nav.home')}</span><i class="bi bi-chevron-down caret"></i></button>
          <div class="nav-group-items">
            ${link('home', '/home.html?tab=myday', 'bi-sun', 'home.myday')}
            ${link('mytasks', '/home.html?tab=mytasks', 'bi-check2-square', 'home.mytasks')}
            ${link('team', '/home.html?tab=team', 'bi-people', 'home.team')}
            ${link('dashboard', '/home.html?tab=dashboard', 'bi-speedometer2', 'home.dashboard')}
          </div>
        </div>

        ${link('clients', '/clients.html', 'bi-people', 'nav.clients')}
        ${link('projects', '/projects.html', 'bi-folder', 'nav.projects')}
        ${link('departments', '/departments.html', 'bi-diagram-3', 'nav.departments')}
        ${link('board', '/board.html', 'bi-kanban', 'nav.board')}
        ${link('calendar', '/calendar.html', 'bi-calendar3', 'nav.calendar')}

        <div class="nav-group" data-group="people">
          <button class="nav-group-toggle"><i class="bi bi-person-badge"></i>
            <span data-i18n="nav.people">${I18N.t('nav.people')}</span><i class="bi bi-chevron-down caret"></i></button>
          <div class="nav-group-items">
            ${link('users', '/team.html', 'bi-person', 'nav.users')}
            ${link('guests', '/guests.html', 'bi-person-vcard', 'nav.guests')}
            ${link('scheduling', '/scheduling.html', 'bi-calendar-week', 'nav.scheduling')}
          </div>
        </div>

        ${link('recurring', '/recurring.html', 'bi-arrow-repeat', 'nav.recurring')}
        ${link('reports', '/reports.html', 'bi-graph-up', 'nav.reports')}
        ${link('timesheets', '/timesheets.html', 'bi-clock-history', 'nav.timesheets')}
        ${link('audit', '/audit.html', 'bi-card-list', 'nav.audit')}
        ${link('integration', '/integration.html', 'bi-plug', 'nav.integration')}
        ${link('billing', '/billing.html', 'bi-credit-card', 'nav.billing')}

        <div class="side-timer">
          <i class="bi bi-stopwatch"></i>
          <span id="sideTimer">00:00:00</span>
        </div>
      </aside>

      <header class="app-topbar">
        <button class="icon-btn topbar-toggle" id="sidebarToggle"><i class="bi bi-list"></i></button>
        <div class="spacer"></div>
        <div class="dropdown">
          <button class="icon-btn" id="notifBtn" data-bs-toggle="dropdown" aria-expanded="false">
            <i class="bi bi-bell"></i><span class="badge bg-danger notif-badge d-none" id="notifBadge">0</span>
          </button>
          <div class="dropdown-menu dropdown-menu-end p-2" style="min-width:320px" id="notifMenu">
            <div class="d-flex justify-content-between align-items-center mb-1">
              <strong data-i18n="notif.title">${I18N.t('notif.title')}</strong>
              <button class="btn btn-sm btn-link p-0" id="notifMarkAll" data-i18n="notif.markAll">${I18N.t('notif.markAll')}</button>
            </div>
            <div id="notifList" class="small"></div>
          </div>
        </div>
        <button class="icon-btn" id="langToggle" title="Language"><i class="bi bi-translate"></i></button>
        <a class="avatar" href="/profile.html" title="${this.esc(user?.fullName || '')}">${initials}</a>
        <button class="icon-btn" id="logoutBtn" title="${I18N.t('nav.logout')}"><i class="bi bi-box-arrow-right flip-rtl"></i></button>
      </header>`;

    I18N.translate(el);

    // Collapsible groups (persist open state). Auto-open the group of the active link.
    el.querySelectorAll('.nav-group').forEach(g => {
      const key = g.dataset.group;
      const saved = localStorage.getItem('tf_group_' + key);
      const hasActive = g.querySelector('.side-link.active');
      if (saved === 'open' || hasActive) g.classList.add('open');
      g.querySelector('.nav-group-toggle').onclick = () => {
        g.classList.toggle('open');
        localStorage.setItem('tf_group_' + key, g.classList.contains('open') ? 'open' : 'closed');
      };
    });

    document.getElementById('logoutBtn').onclick = () => Auth.logout();
    document.getElementById('langToggle').onclick = () => I18N.toggle();
    document.getElementById('sidebarToggle').onclick = () =>
      document.getElementById('appSidebar').classList.toggle('open');
    document.getElementById('notifMarkAll').onclick = async () => { await API.post('/notifications/read-all'); this.refreshNotifications(); };

    document.addEventListener('lang-changed', () => I18N.translate(el));

    await this.refreshNotifications();
    this.refreshSideTimer();
    const conn = await Realtime.connect('notifications');
    if (conn) conn.on('notification', () => this.refreshNotifications());
  },

  async refreshSideTimer() {
    try {
      const running = await API.get('/time/running');
      const el = document.getElementById('sideTimer');
      if (!el) return;
      if (running && running.startUtc) {
        const tick = () => {
          const secs = Math.max(0, Math.floor((Date.now() - new Date(running.startUtc).getTime()) / 1000));
          const h = String(Math.floor(secs / 3600)).padStart(2, '0');
          const m = String(Math.floor((secs % 3600) / 60)).padStart(2, '0');
          const s = String(secs % 60).padStart(2, '0');
          el.textContent = `${h}:${m}:${s}`;
          el.classList.add('timer-running');
        };
        tick();
        clearInterval(this._timerInt);
        this._timerInt = setInterval(tick, 1000);
      } else {
        el.textContent = '00:00:00';
        el.classList.remove('timer-running');
        clearInterval(this._timerInt);
      }
    } catch {}
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
        : `<div class="text-muted text-center py-2">${I18N.t('notif.empty')}</div>`;

      listEl.querySelectorAll('.notif-item').forEach(a =>
        a.addEventListener('click', () => API.post(`/notifications/${a.dataset.id}/read`).catch(()=>{})));
    } catch {}
  },

  esc(s) { const d = document.createElement('div'); d.textContent = s ?? ''; return d.innerHTML; }
,
  // Initials from a display name (max 2 letters), e.g. "Saleh Odeh" -> "SO".
  initials(name) {
    const parts = (name || '').trim().split(/\s+/).filter(Boolean);
    if (!parts.length) return '?';
    return parts.map(s => s[0]).slice(0, 2).join('').toUpperCase();
  },
  // Deterministic avatar colour bucket (c0..c7) from a stable seed (id/email/name).
  avColor(seed) {
    const s = String(seed ?? '');
    let h = 0;
    for (let i = 0; i < s.length; i++) h = (h * 31 + s.charCodeAt(i)) >>> 0;
    return 'c' + (h % 8);
  },
  // Ready-to-inject coloured initials avatar. size: '' | 'lg' | 'sm'.
  avatar(name, seed, size) {
    return `<span class="av ${size ? 'av-' + size : ''} ${this.avColor(seed ?? name)}">${this.esc(this.initials(name))}</span>`;
  }
,
  // Render rich HTML (e.g. Paymo task descriptions) safely: strip scripts, event handlers and js: urls.
  safeHtml(html) {
    const t = document.createElement('template');
    t.innerHTML = html || '';
    t.content.querySelectorAll('script,style,iframe,object,embed,link,meta,form').forEach(e => e.remove());
    t.content.querySelectorAll('*').forEach(el => {
      [...el.attributes].forEach(a => {
        const n = a.name.toLowerCase();
        if (n.startsWith('on')) el.removeAttribute(a.name);
        if ((n === 'href' || n === 'src') && /^\s*javascript:/i.test(a.value)) el.removeAttribute(a.name);
      });
      if (el.tagName === 'A') { el.setAttribute('target', '_blank'); el.setAttribute('rel', 'noopener noreferrer'); }
    });
    return t.innerHTML;
  }
};

window.UI = UI;
