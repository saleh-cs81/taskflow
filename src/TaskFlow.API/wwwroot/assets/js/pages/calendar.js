(async function () {
  await I18N.load();
  if (!Auth.requireAuth()) return;
  await UI.mountNavbar('calendar');

  let viewYear, viewMonth; // month is 0-based
  const today = new Date();

  function setMonth(y, m) { viewYear = y; viewMonth = m; render(); }

  document.getElementById('prevMonth').onclick = () => {
    const m = viewMonth - 1; setMonth(m < 0 ? viewYear - 1 : viewYear, (m + 12) % 12);
  };
  document.getElementById('nextMonth').onclick = () => {
    const m = viewMonth + 1; setMonth(m > 11 ? viewYear + 1 : viewYear, m % 12);
  };
  document.getElementById('todayBtn').onclick = () => setMonth(today.getFullYear(), today.getMonth());

  function ymd(d) { return d.toISOString().slice(0, 10); }
  function statusDone(s) { return s === 3; }

  async function render() {
    const first = new Date(Date.UTC(viewYear, viewMonth, 1));
    const next = new Date(Date.UTC(viewYear, viewMonth + 1, 1));
    document.getElementById('monthLabel').textContent =
      first.toLocaleDateString(I18N.lang, { month: 'long', year: 'numeric' });

    // Day-of-week header
    const dow = [];
    for (let i = 0; i < 7; i++) {
      const d = new Date(Date.UTC(2024, 0, 7 + i)); // a known Sunday-start week
      dow.push(`<div class="cal-dow">${d.toLocaleDateString(I18N.lang, { weekday: 'short' })}</div>`);
    }
    document.getElementById('calDow').innerHTML = dow.join('');

    // Fetch tasks due in the visible month
    const items = await API.get(`/reports/calendar?from=${ymd(first)}&to=${ymd(next)}`);
    const byDay = {};
    items.forEach(t => {
      if (!t.dueDate) return;
      const key = t.dueDate.slice(0, 10);
      (byDay[key] ||= []).push(t);
    });

    // Build grid: pad leading blanks for the first day's weekday
    const startDow = first.getUTCDay();
    const daysInMonth = new Date(Date.UTC(viewYear, viewMonth + 1, 0)).getUTCDate();
    const cells = [];
    for (let i = 0; i < startDow; i++) cells.push('<div class="cal-cell muted"></div>');
    for (let day = 1; day <= daysInMonth; day++) {
      const date = new Date(Date.UTC(viewYear, viewMonth, day));
      const key = ymd(date);
      const isToday = key === ymd(new Date(Date.UTC(today.getFullYear(), today.getMonth(), today.getDate())));
      const tasks = (byDay[key] || []).map(t =>
        `<span class="cal-task ${statusDone(t.status) ? 'done' : ''}" title="${UI.esc(t.title)}">${UI.esc(t.title)}</span>`).join('');
      cells.push(`<div class="cal-cell ${isToday ? 'today' : ''}"><div class="cal-daynum">${day}</div>${tasks}</div>`);
    }
    document.getElementById('calGrid').innerHTML = cells.join('');

    renderGantt(items, first, next);
  }

  // Simple timeline: bar from start (or due) to due, positioned within the month.
  function renderGantt(items, first, next) {
    const span = next - first;
    const withDates = items.filter(t => t.dueDate);
    const el = document.getElementById('gantt');
    if (withDates.length === 0) { el.innerHTML = `<div class="text-muted">${I18N.t('cal.noTasks')}</div>`; return; }

    el.innerHTML = withDates.map(t => {
      const due = new Date(t.dueDate);
      const start = t.startDate ? new Date(t.startDate) : due;
      const s = Math.max(0, (start - first) / span) * 100;
      const e = Math.min(1, (due - first) / span) * 100;
      const width = Math.max(2, e - s);
      return `<div class="gantt-row">
        <div class="gantt-label" title="${UI.esc(t.title)}">${UI.esc(t.title)}</div>
        <div class="gantt-track"><div class="gantt-bar" style="inset-inline-start:${s}%;width:${width}%"></div></div>
      </div>`;
    }).join('');
  }

  document.addEventListener('lang-changed', () => render());
  setMonth(today.getFullYear(), today.getMonth());
})();
