const Auth = {
  isAuthed() { return !!API.accessToken; },

  requireAuth() {
    if (!this.isAuthed()) { location.href = '/login.html'; return false; }
    return true;
  },

  async login(email, password) {
    const auth = await API.request('POST', '/auth/login', { email, password });
    API.saveTokens(auth);
    return auth;
  },

  async register(companyName, fullName, email, password) {
    const auth = await API.request('POST', '/auth/register',
      { companyName, fullName, email, password, locale: window.I18N?.lang || 'en' });
    API.saveTokens(auth);
    return auth;
  },

  async logout() {
    try { if (API.refreshToken) await API.post('/auth/logout', { refreshToken: API.refreshToken }); } catch {}
    API.clear();
    location.href = '/login.html';
  }
};

window.Auth = Auth;
