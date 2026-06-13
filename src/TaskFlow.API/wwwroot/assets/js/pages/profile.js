(async function () {
  await I18N.load();
  if (!Auth.requireAuth()) return;
  await UI.mountNavbar('');

  const u = API.currentUser() ?? {};
  document.getElementById('pf_name').textContent = u.fullName ?? '—';
  document.getElementById('pf_email').textContent = u.email ?? '—';
  document.getElementById('pf_roles').textContent = (u.roles ?? []).join(', ') || '—';
  document.getElementById('pf_tenant').textContent = u.tenantId ?? '—';

  const msg = document.getElementById('pwMsg');
  document.getElementById('pwForm').onsubmit = async (e) => {
    e.preventDefault();
    msg.innerHTML = '';
    const cur = document.getElementById('pw_current').value;
    const np  = document.getElementById('pw_new').value;
    const cf  = document.getElementById('pw_confirm').value;
    if (np !== cf) {
      msg.innerHTML = `<div class="alert alert-danger py-1">${I18N.t('prof.mismatch')}</div>`;
      return;
    }
    try {
      await API.post('/auth/change-password', { currentPassword: cur, newPassword: np });
      msg.innerHTML = `<div class="alert alert-success py-1">${I18N.t('prof.changed')}</div>`;
      document.getElementById('pwForm').reset();
      // Server revoked the refresh token; new access token may still work briefly.
      // Log the user out cleanly so they re-authenticate with the new password.
      setTimeout(() => Auth.logout(), 1500);
    } catch (ex) {
      msg.innerHTML = `<div class="alert alert-danger py-1">${ex.problem?.title || I18N.t('common.error')}</div>`;
    }
  };

  document.addEventListener('lang-changed', () => location.reload());
})();
