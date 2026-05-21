(async function () {
  await I18N.load();
  if (!Auth.requireAuth()) return;
  await UI.mountNavbar('reports');

  const card = (labelKey, value, color) => `
    <div class="col-6 col-md-3">
      <div class="card shadow-sm border-0 h-100">
        <div class="card-body">
          <div class="text-muted small" data-i18n="${labelKey}">${I18N.t(labelKey)}</div>
          <div class="fs-3 fw-bold text-${color}">${value}</div>
        </div>
      </div>
    </div>`;

  async function load() {
    const stats = await API.get('/reports/dashboard');
    document.getElementById('statCards').innerHTML =
      card('rep.activeProjects', stats.activeProjects, 'primary') +
      card('rep.openTasks', stats.openTasks, 'info') +
      card('rep.completedTasks', stats.completedTasks, 'success') +
      card('rep.overdue', stats.overdueTasks, 'danger') +
      card('rep.totalProjects', stats.totalProjects, 'secondary') +
      card('rep.hoursWeek', stats.hoursThisWeek, 'dark') +
      card('rep.billableWeek', stats.billableHoursThisWeek, 'success');

    const progress = await API.get('/reports/project-progress');
    document.getElementById('progressBody').innerHTML = progress.map(p => `
      <tr>
        <td>${UI.esc(p.projectName)}</td>
        <td><span class="badge bg-light text-dark">${p.status}</span></td>
        <td>${p.totalTasks}</td>
        <td>${p.todoTasks}</td>
        <td>${p.inProgressTasks}</td>
        <td>${p.doneTasks}</td>
        <td>
          <div class="d-flex align-items-center gap-2">
            <div class="progress flex-grow-1" style="height:8px">
              <div class="progress-bar bg-success" style="width:${p.percentComplete}%"></div>
            </div>
            <small>${p.percentComplete}%</small>
          </div>
        </td>
      </tr>`).join('');
  }

  // Export downloads carry the JWT, so fetch as a blob and save.
  document.querySelectorAll('[data-fmt]').forEach(a => a.onclick = async (e) => {
    e.preventDefault();
    const fmt = a.dataset.fmt;
    const res = await fetch(`/api/v1/reports/0/export?format=${fmt}`, {
      headers: { 'Authorization': `Bearer ${API.accessToken}` }
    });
    if (!res.ok) return;
    const blob = await res.blob();
    const disp = res.headers.get('content-disposition') || '';
    const m = disp.match(/filename=([^;]+)/);
    const name = m ? m[1].trim().replace(/"/g, '') : 'report';
    const url = URL.createObjectURL(blob);
    const link = document.createElement('a');
    link.href = url; link.download = name; link.click();
    URL.revokeObjectURL(url);
  });

  document.addEventListener('lang-changed', () => location.reload());
  await load();
})();
