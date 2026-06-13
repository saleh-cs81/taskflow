(async function () {
  await I18N.load();
  if (!Auth.requireAuth()) return;
  await UI.mountNavbar('recurring');

  const UNIT = [I18N.t('rec.daily'), I18N.t('rec.weekly'), I18N.t('rec.monthly')];
  let projects = [];
  try { projects = (await API.get('/projects?pageSize=100')).items; } catch {}
  document.getElementById('r_project').innerHTML =
    projects.map(p => `<option value="${p.id}">${UI.esc(p.name)}</option>`).join('');

  async function load() {
    const list = await API.get('/recurring-tasks');
    const body = document.getElementById('recBody');
    body.innerHTML = list.length ? list.map(r => `
      <tr>
        <td>${UI.esc(r.titleTemplate)}</td>
        <td class="small">${r.interval} ${UNIT[r.unit] || ''}</td>
        <td class="small">${r.nextRunUtc ? r.nextRunUtc.slice(0,10) : ''}</td>
        <td>${r.isActive ? '✓' : '—'}</td>
        <td class="text-end text-nowrap">
          <button class="btn btn-sm btn-outline-secondary" data-toggle="${r.id}">${I18N.t('rec.toggle')}</button>
          <button class="btn btn-sm btn-outline-danger" data-del="${r.id}">×</button>
        </td>
      </tr>`).join('') : `<tr><td colspan="5" class="text-muted text-center py-3">${I18N.t('rec.empty')}</td></tr>`;
    body.querySelectorAll('[data-toggle]').forEach(b => b.onclick = async () => { await API.post(`/recurring-tasks/${b.dataset.toggle}/toggle`); load(); });
    body.querySelectorAll('[data-del]').forEach(b => b.onclick = async () => { await API.del(`/recurring-tasks/${b.dataset.del}`); load(); });
  }

  const modal = new bootstrap.Modal(document.getElementById('recModal'));
  document.getElementById('recForm').onsubmit = async (e) => {
    e.preventDefault();
    const first = document.getElementById('r_first').value;
    const body = {
      projectId: parseInt(document.getElementById('r_project').value, 10),
      taskListId: null,
      titleTemplate: document.getElementById('r_title').value.trim(),
      description: null, priority: 1, assigneeId: null,
      unit: parseInt(document.getElementById('r_unit').value, 10),
      interval: parseInt(document.getElementById('r_interval').value, 10) || 1,
      dueInDays: null,
      firstRunUtc: `${first}T00:00:00Z`
    };
    try { await API.post('/recurring-tasks', body); document.getElementById('recForm').reset(); modal.hide(); load(); }
    catch (ex) { document.getElementById('recErr').innerHTML = `<div class="alert alert-danger py-1">${ex.problem?.title || I18N.t('common.error')}</div>`; }
  };

  document.addEventListener('lang-changed', () => location.reload());
  await load();
})();
