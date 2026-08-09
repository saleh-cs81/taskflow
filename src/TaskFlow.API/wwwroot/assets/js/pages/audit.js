(async function () {
  await I18N.load();
  if (!Auth.requireAuth()) return;
  await UI.mountNavbar('audit');

  // change-type int -> key/tone (matches AuditChangeType enum on the server)
  const ACTIONS = [
    { v: 0, key: 'Created', tone: 'on' },
    { v: 1, key: 'Updated', tone: 'info' },
    { v: 2, key: 'Deleted', tone: 'danger' },
    { v: 3, key: 'Login', tone: 'purple' },
    { v: 4, key: 'Logout', tone: 'off' },
    { v: 5, key: 'LoginFailed', tone: 'warn' },
    { v: 6, key: 'PasswordChanged', tone: 'warn' }
  ];
  const toneOf = (name) => (ACTIONS.find(a => a.key === name) || {}).tone || 'off';
  const actLabel = (name) => I18N.t('audit.act.' + name);

  const pageSize = 30;
  let page = 1;
  let last = null;

  const $ = (id) => document.getElementById(id);

  function fillActionFilter() {
    $('fAction').innerHTML = `<option value="">${I18N.t('audit.allActions')}</option>` +
      ACTIONS.map(a => `<option value="${a.v}">${actLabel(a.key)}</option>`).join('');
  }
  async function fillEntityFilter() {
    let tables = [];
    try { tables = await API.get('/audit/tables'); } catch {}
    $('fEntity').innerHTML = `<option value="">${I18N.t('audit.allEntities')}</option>` +
      tables.map(t => `<option value="${UI.esc(t)}">${UI.esc(t)}</option>`).join('');
  }

  function parse(json) { try { return json ? JSON.parse(json) : null; } catch { return null; } }
  function fmt(v) {
    if (v === null || v === undefined) return '<span class="text-muted">—</span>';
    if (typeof v === 'boolean') return v ? '✓' : '✗';
    let s = String(v);
    if (/^\d{4}-\d{2}-\d{2}T/.test(s)) { const d = new Date(s); if (!isNaN(d)) s = d.toLocaleString(I18N.lang); }
    if (s.length > 60) s = s.slice(0, 60) + '…';
    return UI.esc(s);
  }

  // short one-line summary of what changed
  function summary(row) {
    if (row.tableName === 'Auth') {
      const nv = parse(row.newValuesJson) || {};
      return nv.reason ? UI.esc(nv.reason) : (nv.email ? UI.esc(nv.email) : '');
    }
    const nv = parse(row.newValuesJson);
    const ov = parse(row.oldValuesJson);
    if (row.changeType === 'Updated' && nv) {
      const parts = Object.keys(nv).slice(0, 3).map(k =>
        `<span class="text-muted">${UI.esc(k)}:</span> ${fmt(ov ? ov[k] : null)} <i class="bi bi-arrow-right flip-rtl small"></i> ${fmt(nv[k])}`);
      const more = Object.keys(nv).length - 3;
      return parts.join(' &middot; ') + (more > 0 ? ` <span class="text-muted">+${more}</span>` : '');
    }
    const src = nv || ov;
    if (src) {
      const label = src.Name || src.Title || src.Number || src.FullName || src.Email;
      if (label) return `<span class="fw-medium">${UI.esc(String(label))}</span>`;
      return `<span class="text-muted">${Object.keys(src).length} ${I18N.t('audit.fields')}</span>`;
    }
    return '';
  }

  function rowHtml(r) {
    const who = r.userName
      ? `<span class="d-inline-flex align-items-center gap-2">${UI.avatar(r.userName, r.userId, 'sm')}<span class="text-truncate">${UI.esc(r.userName)}</span></span>`
      : `<span class="text-muted"><i class="bi bi-gear"></i> ${I18N.t('audit.system')}</span>`;
    const when = new Date(r.createdAtUtc).toLocaleString(I18N.lang);
    const hasDetail = r.oldValuesJson || r.newValuesJson;
    return `<tr>
      <td class="text-muted small text-nowrap">${UI.esc(when)}</td>
      <td class="small" style="max-width:180px">${who}</td>
      <td><span class="status-pill ${toneOf(r.changeType)}">${actLabel(r.changeType)}</span></td>
      <td class="small text-nowrap"><span class="fw-medium">${UI.esc(r.tableName)}</span>${r.recordId ? ` <span class="text-muted">#${UI.esc(r.recordId)}</span>` : ''}</td>
      <td class="small">${summary(r)}</td>
      <td class="text-end">${hasDetail ? `<button class="btn btn-sm btn-link p-0" data-detail="${r.id}" title="${I18N.t('audit.details')}"><i class="bi bi-eye"></i></button>` : ''}</td>
    </tr>`;
  }

  function renderDetail(r) {
    const ov = parse(r.oldValuesJson) || {};
    const nv = parse(r.newValuesJson) || {};
    const keys = [...new Set([...Object.keys(ov), ...Object.keys(nv)])];
    const rows = keys.map(k => `<tr>
      <td class="fw-medium text-nowrap">${UI.esc(k)}</td>
      <td>${fmt(ov[k])}</td>
      <td>${fmt(nv[k])}</td>
    </tr>`).join('');
    $('auditDetailBody').innerHTML = `
      <div class="d-flex flex-wrap gap-3 mb-3 small">
        <div><span class="text-muted">${I18N.t('audit.action')}:</span> <span class="status-pill ${toneOf(r.changeType)}">${actLabel(r.changeType)}</span></div>
        <div><span class="text-muted">${I18N.t('audit.record')}:</span> ${UI.esc(r.tableName)}${r.recordId ? ' #' + UI.esc(r.recordId) : ''}</div>
        <div><span class="text-muted">${I18N.t('audit.user')}:</span> ${r.userName ? UI.esc(r.userName) : I18N.t('audit.system')}</div>
        <div><span class="text-muted">${I18N.t('audit.time')}:</span> ${UI.esc(new Date(r.createdAtUtc).toLocaleString(I18N.lang))}</div>
      </div>
      ${keys.length ? `<div class="table-responsive"><table class="table table-sm align-middle mb-0">
        <thead><tr><th data-i18n>${I18N.t('audit.field')}</th><th>${I18N.t('audit.old')}</th><th>${I18N.t('audit.new')}</th></tr></thead>
        <tbody>${rows}</tbody></table></div>` : `<div class="text-muted">${I18N.t('audit.noDetail')}</div>`}`;
    bootstrap.Modal.getOrCreateInstance($('auditDetail')).show();
  }

  async function load() {
    const p = new URLSearchParams();
    p.set('page', page); p.set('pageSize', pageSize);
    const from = $('fFrom').value, to = $('fTo').value;
    if (from) p.set('from', from);
    if (to) { const d = new Date(to + 'T00:00:00'); d.setDate(d.getDate() + 1); p.set('to', d.toISOString().slice(0, 10)); }
    if ($('fEntity').value) p.set('table', $('fEntity').value);
    if ($('fAction').value !== '') p.set('changeType', $('fAction').value);
    const q = $('fSearch').value.trim(); if (q) p.set('search', q);

    const res = await API.get('/audit?' + p.toString());
    last = res;
    const body = $('auditBody');
    body.innerHTML = res.items.length
      ? res.items.map(rowHtml).join('')
      : `<tr><td colspan="6"><div class="empty-state"><i class="bi bi-clipboard-check es-icon"></i><div class="es-text">${I18N.t('audit.noRows')}</div></div></td></tr>`;

    body.querySelectorAll('[data-detail]').forEach(btn => btn.onclick = () => {
      const r = res.items.find(x => String(x.id) === btn.dataset.detail);
      if (r) renderDetail(r);
    });

    const start = res.total === 0 ? 0 : res.pageSize * (res.page - 1) + 1;
    const end = Math.min(res.total, res.pageSize * res.page);
    $('pageInfo').textContent = I18N.t('audit.showing')
      .replace('{start}', start).replace('{end}', end).replace('{total}', res.total);
    $('prevBtn').disabled = res.page <= 1;
    $('nextBtn').disabled = res.page >= res.totalPages;
  }

  function reload() { page = 1; load(); }

  ['fFrom', 'fTo', 'fEntity', 'fAction'].forEach(id => $(id).onchange = reload);
  let t; $('fSearch').oninput = () => { clearTimeout(t); t = setTimeout(reload, 300); };
  $('clearBtn').onclick = () => { ['fFrom', 'fTo', 'fSearch'].forEach(id => $(id).value = ''); $('fEntity').value = ''; $('fAction').value = ''; reload(); };
  $('refreshBtn').onclick = () => load();
  $('prevBtn').onclick = () => { if (page > 1) { page--; load(); } };
  $('nextBtn').onclick = () => { if (last && page < last.totalPages) { page++; load(); } };
  document.addEventListener('lang-changed', () => location.reload());

  fillActionFilter();
  await fillEntityFilter();
  await load();
})();
