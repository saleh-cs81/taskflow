(async function () {
  await I18N.load();
  if (!Auth.requireAuth()) return;
  await UI.mountNavbar('billing');

  const msg = document.getElementById('msg');
  const lim = (n) => n === -1 ? I18N.t('bill.unlimited') : n;

  // If returning from PayPal approval (sandbox), confirm the subscription.
  const approved = new URLSearchParams(location.search).get('approved');
  if (approved) {
    try {
      await API.post('/billing/confirm', { providerSubscriptionId: approved });
      msg.innerHTML = `<div class="alert alert-success">${I18N.t('bill.activated')}</div>`;
      history.replaceState({}, '', '/billing.html');
    } catch {}
  }

  let current;

  async function load() {
    const [plans, sub, usage] = await Promise.all([
      API.get('/billing/plans'), API.get('/billing/subscription'), API.get('/billing/usage')
    ]);
    current = sub;

    document.getElementById('currentPlan').textContent = sub.planName;
    document.getElementById('currentStatus').textContent = `${sub.status === 1 ? 'Active' : 'Trial'}` +
      (sub.currentPeriodEndUtc ? ` · ${new Date(sub.currentPeriodEndUtc).toLocaleDateString(I18N.lang)}` : '');
    document.getElementById('manageLink').href = sub.manageUrl;

    document.getElementById('usage').innerHTML = `
      <div class="d-flex justify-content-between"><span data-i18n="bill.users">${I18N.t('bill.users')}</span>
        <strong>${usage.users} / ${lim(usage.maxUsers)}</strong></div>
      <div class="d-flex justify-content-between"><span data-i18n="bill.projects">${I18N.t('bill.projects')}</span>
        <strong>${usage.projects} / ${lim(usage.maxProjects)}</strong></div>
      ${usage.overLimit ? '<div class="text-danger small mt-1">⚠ Over plan limit</div>' : ''}`;

    document.getElementById('plans').innerHTML = plans.map(p => {
      const isCurrent = p.code === sub.planCode;
      return `<div class="col-md-4">
        <div class="card shadow-sm h-100 ${isCurrent ? 'border-primary' : ''}">
          <div class="card-body d-flex flex-column">
            <h5>${UI.esc(p.name)} ${isCurrent ? `<span class="badge bg-primary">${I18N.t('bill.currentBadge')}</span>` : ''}</h5>
            <div class="fs-3 fw-bold">$${p.priceMonthly}<small class="fs-6 text-muted" data-i18n="bill.month">${I18N.t('bill.month')}</small></div>
            <ul class="list-unstyled small text-muted my-3">
              <li>${I18N.t('bill.users')}: ${lim(p.maxUsers)}</li>
              <li>${I18N.t('bill.projects')}: ${lim(p.maxProjects)}</li>
            </ul>
            <button class="btn btn-primary mt-auto ${isCurrent ? 'disabled' : ''}" data-plan="${p.id}" data-i18n="bill.subscribe">${I18N.t('bill.subscribe')}</button>
          </div>
        </div></div>`;
    }).join('');

    document.querySelectorAll('[data-plan]').forEach(b => b.onclick = async () => {
      try {
        const res = await API.post('/billing/subscribe', { planId: parseInt(b.dataset.plan, 10) });
        location.href = res.approvalUrl; // redirect to PayPal approval (sandbox returns back here)
      } catch (ex) { msg.innerHTML = `<div class="alert alert-danger">${ex.problem?.title || I18N.t('common.error')}</div>`; }
    });
  }

  document.addEventListener('lang-changed', () => location.reload());
  await load();
})();
