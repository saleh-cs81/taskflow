(async function () {
  await I18N.load();
  if (!Auth.requireAuth()) return;
  await UI.mountNavbar('scheduling');

  const fmt = d => d.toISOString().slice(0, 10);
  const today = new Date();
  const from = new Date(today); from.setDate(from.getDate() - 60);
  const to = new Date(today); to.setDate(to.getDate() + 120);
  const weekEnd = new Date(today); weekEnd.setDate(weekEnd.getDate() + 7);

  const [users, items] = await Promise.all([
    API.get('/users'),
    API.get(`/reports/calendar?from=${fmt(from)}&to=${fmt(to)}`)
  ]);

  // Aggregate per assignee (status 3 = Done).
  const agg = {};
  users.forEach(u => agg[u.id] = { name: u.fullName, open: 0, dueSoon: 0, overdue: 0 });
  items.forEach(t => {
    if (t.assigneeId == null || t.status === 3 || !agg[t.assigneeId]) return;
    const a = agg[t.assigneeId];
    a.open++;
    const due = t.dueDate ? new Date(t.dueDate) : null;
    if (due) {
      if (due < today) a.overdue++;
      else if (due <= weekEnd) a.dueSoon++;
    }
  });

  const rows = Object.values(agg);
  const body = document.getElementById('schBody');
  body.innerHTML = rows.length ? rows.map(a => `
    <tr>
      <td>${UI.esc(a.name)}</td>
      <td class="text-center">${a.open}</td>
      <td class="text-center">${a.dueSoon ? `<span class="badge bg-warning text-dark">${a.dueSoon}</span>` : 0}</td>
      <td class="text-center">${a.overdue ? `<span class="badge bg-danger">${a.overdue}</span>` : 0}</td>
    </tr>`).join('') : `<tr><td colspan="4" class="text-muted text-center py-3">${I18N.t('sch.empty')}</td></tr>`;

  document.addEventListener('lang-changed', () => location.reload());
})();
