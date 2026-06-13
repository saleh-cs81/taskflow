(async function () {
  await I18N.load();
  if (!Auth.requireAuth()) return;
  await UI.mountNavbar('board');

  const boardEl = document.getElementById('board');
  const picker = document.getElementById('projectPicker');
  const STATUS_BY_INDEX = [0, 1, 3]; // To Do, In Progress, Done -> WorkStatus

  let projects = [];
  try { projects = (await API.get('/projects?pageSize=100')).items; } catch {}
  if (!projects.length) { boardEl.innerHTML = `<div class="text-muted p-3">${I18N.t('dash.noProjects')}</div>`; return; }

  picker.innerHTML = projects.map(p => `<option value="${p.id}">${UI.esc(p.name)}</option>`).join('');

  const params = new URLSearchParams(location.search);
  let projectId = parseInt(params.get('projectId') || projects[0].id, 10);
  picker.value = projectId;
  picker.onchange = () => { projectId = parseInt(picker.value, 10); render(); subscribe(); };

  function priorityDot(p) { return `<span class="priority priority-${p}"></span>`; }

  // Load users for the assignee dropdown once.
  let users = [];
  try { users = await API.get('/users'); } catch {}
  const assigneeSelect = document.getElementById('tf_assignee');
  assigneeSelect.innerHTML =
    `<option value="">—</option>` +
    users.map(u => `<option value="${u.id}">${UI.esc(u.fullName)} (${UI.esc(u.email)})</option>`).join('');

  async function render() {
    document.getElementById('projectName').textContent = projects.find(p => p.id === projectId)?.name ?? '';
    const board = await API.get(`/projects/${projectId}/board`);

    boardEl.innerHTML = board.columns.map((c, idx) => `
      <div class="board-column" data-list-id="${c.id}" data-status-idx="${idx}">
        <h6 class="d-flex justify-content-between">
          <span>${UI.esc(c.name)}</span><span class="badge bg-light text-dark">${c.tasks.length}</span>
        </h6>
        <div class="task-list-dropzone" data-list-id="${c.id}">
          ${c.tasks.map(t => taskCard(t)).join('')}
        </div>
        <button class="btn btn-sm btn-link p-0 add-task" data-list-id="${c.id}" data-status-idx="${idx}">+ ${I18N.t('board.addTask')}</button>
      </div>`).join('');

    boardEl.querySelectorAll('.task-list-dropzone').forEach(zone => {
      Sortable.create(zone, { group: 'board', animation: 150, ghostClass: 'sortable-ghost', onEnd: onDrop });
    });
    boardEl.querySelectorAll('.add-task').forEach(b => b.onclick = () => openTaskModal(b.dataset.listId));
  }

  function taskCard(t) {
    const cl = t.checklistTotal ? `<span class="badge bg-light text-dark ms-1">${t.checklistDone}/${t.checklistTotal}</span>` : '';
    return `<div class="task-card" data-task-id="${t.id}">
      ${priorityDot(t.priority)}${UI.esc(t.title)}${cl}</div>`;
  }

  async function onDrop(evt) {
    const taskId = parseInt(evt.item.dataset.taskId, 10);
    const zone = evt.to;
    const listId = parseInt(zone.dataset.listId, 10);
    const statusIdx = parseInt(zone.closest('.board-column').dataset.statusIdx, 10);
    const pos = (evt.newIndex + 1) * 1000;
    try {
      await API.patch(`/tasks/${taskId}/move`, { taskListId: listId, position: pos, status: STATUS_BY_INDEX[statusIdx] });
    } catch { render(); }
  }

  // --- Task creation modal ---
  const taskModal = new bootstrap.Modal(document.getElementById('taskModal'));
  function openTaskModal(listId) {
    document.getElementById('taskForm').reset();
    document.getElementById('tf_listId').value = listId;
    document.getElementById('taskError').innerHTML = '';
    taskModal.show();
  }
  document.getElementById('taskForm').onsubmit = async (e) => {
    e.preventDefault();
    const listId = parseInt(document.getElementById('tf_listId').value, 10);
    const due = document.getElementById('tf_due').value;
    const assigneeId = document.getElementById('tf_assignee').value;
    const body = {
      projectId,
      taskListId: listId,
      parentTaskId: null,
      milestoneId: null,
      title: document.getElementById('tf_title').value.trim(),
      description: document.getElementById('tf_desc').value.trim() || null,
      priority: parseInt(document.getElementById('tf_priority').value, 10),
      assigneeId: assigneeId ? parseInt(assigneeId, 10) : null,
      startDate: null,
      dueDate: due ? `${due}T00:00:00Z` : null,
      estimateHours: null,
      isBillable: false
    };
    try { await API.post('/tasks', body); taskModal.hide(); render(); }
    catch (ex) { document.getElementById('taskError').innerHTML = `<div class="alert alert-danger py-1">${ex.problem?.title || I18N.t('common.error')}</div>`; }
  };

  let conn;
  async function subscribe() {
    conn = await Realtime.connect('board');
    if (!conn) return;
    conn.off('task.moved'); conn.off('task.created');
    await conn.invoke('JoinProject', projectId).catch(()=>{});
    conn.on('task.moved', () => render());
    conn.on('task.created', () => render());
  }

  document.addEventListener('lang-changed', () => render());
  await render();
  await subscribe();
})();
