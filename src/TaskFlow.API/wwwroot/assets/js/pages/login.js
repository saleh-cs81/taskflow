(async function () {
  await I18N.load();
  if (API.accessToken) { location.href = '/dashboard.html'; return; }

  const err = document.getElementById('errorBox');
  const showError = (msg) => { err.textContent = msg; err.classList.remove('d-none'); };
  const clearError = () => err.classList.add('d-none');

  document.getElementById('langToggle').onclick = () => I18N.toggle();

  const loginForm = document.getElementById('loginForm');
  const registerForm = document.getElementById('registerForm');
  const title = document.getElementById('formTitle');

  document.getElementById('showRegister').onclick = () => {
    loginForm.classList.add('d-none'); registerForm.classList.remove('d-none');
    title.setAttribute('data-i18n', 'register.title'); I18N.translate(document); clearError();
  };
  document.getElementById('showLogin').onclick = () => {
    registerForm.classList.add('d-none'); loginForm.classList.remove('d-none');
    title.setAttribute('data-i18n', 'login.title'); I18N.translate(document); clearError();
  };

  loginForm.onsubmit = async (e) => {
    e.preventDefault(); clearError();
    try {
      await Auth.login(document.getElementById('loginEmail').value, document.getElementById('loginPassword').value);
      location.href = '/dashboard.html';
    } catch (ex) { showError(ex.problem?.title || I18N.t('login.error')); }
  };

  registerForm.onsubmit = async (e) => {
    e.preventDefault(); clearError();
    try {
      await Auth.register(
        document.getElementById('regCompany').value,
        document.getElementById('regName').value,
        document.getElementById('regEmail').value,
        document.getElementById('regPassword').value);
      location.href = '/dashboard.html';
    } catch (ex) { showError(ex.problem?.title || I18N.t('common.error')); }
  };
})();
