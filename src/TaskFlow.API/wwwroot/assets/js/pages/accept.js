(async function () {
  await I18N.load();
  document.getElementById('langToggle').onclick = () => I18N.toggle();

  const token = new URLSearchParams(location.search).get('token');
  const msg = document.getElementById('msg');
  const form = document.getElementById('acceptForm');

  if (!token) {
    msg.innerHTML = `<div class="alert alert-danger">${I18N.t('accept.invalid')}</div>`;
    form.classList.add('d-none');
    return;
  }

  form.onsubmit = async (e) => {
    e.preventDefault();
    msg.innerHTML = '';
    const fullName = document.getElementById('fullName').value;
    const password = document.getElementById('password').value;
    try {
      const result = await API.request('POST', '/invitations/accept', { token, fullName, password });
      msg.innerHTML = `<div class="alert alert-success">${I18N.t('accept.success')}</div>`;
      // Auto sign-in with the credentials just created.
      await Auth.login(result.email, password);
      location.href = '/home.html';
    } catch (ex) {
      msg.innerHTML = `<div class="alert alert-danger">${ex.problem?.title || I18N.t('accept.invalid')}</div>`;
    }
  };
})();
