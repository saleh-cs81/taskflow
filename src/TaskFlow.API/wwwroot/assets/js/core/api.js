// Single entry point for all API calls: attaches JWT, sends the active language,
// and transparently refreshes the access token on a 401 then retries once.
const API = {
  base: '/api/v1',

  get accessToken() { return localStorage.getItem('tf_access'); },
  set accessToken(v) { v ? localStorage.setItem('tf_access', v) : localStorage.removeItem('tf_access'); },
  get refreshToken() { return localStorage.getItem('tf_refresh'); },
  set refreshToken(v) { v ? localStorage.setItem('tf_refresh', v) : localStorage.removeItem('tf_refresh'); },

  saveTokens(auth) {
    this.accessToken = auth.accessToken;
    this.refreshToken = auth.refreshToken;
    if (auth.user) localStorage.setItem('tf_user', JSON.stringify(auth.user));
  },
  clear() {
    this.accessToken = null; this.refreshToken = null;
    localStorage.removeItem('tf_user');
  },
  currentUser() {
    try { return JSON.parse(localStorage.getItem('tf_user')); } catch { return null; }
  },

  async request(method, path, body, isRetry = false) {
    const headers = { 'Accept-Language': window.I18N?.lang || 'en' };
    if (this.accessToken) headers['Authorization'] = `Bearer ${this.accessToken}`;
    const opts = { method, headers };
    if (body !== undefined) { headers['Content-Type'] = 'application/json'; opts.body = JSON.stringify(body); }

    const res = await fetch(this.base + path, opts);

    if (res.status === 401 && !isRetry && this.refreshToken) {
      if (await this.tryRefresh()) return this.request(method, path, body, true);
      this.clear();
      if (!location.pathname.endsWith('login.html')) location.href = '/login.html';
      throw new Error('unauthorized');
    }
    if (!res.ok) {
      let problem; try { problem = await res.json(); } catch {}
      throw Object.assign(new Error(problem?.title || 'request failed'), { status: res.status, problem });
    }
    if (res.status === 204) return null;
    const ct = res.headers.get('content-type') || '';
    return ct.includes('application/json') ? res.json() : res.text();
  },

  async tryRefresh() {
    try {
      const res = await fetch(`${this.base}/auth/refresh`, {
        method: 'POST', headers: { 'Content-Type': 'application/json' },
        body: JSON.stringify({ refreshToken: this.refreshToken })
      });
      if (!res.ok) return false;
      this.saveTokens(await res.json());
      return true;
    } catch { return false; }
  },

  get(p) { return this.request('GET', p); },
  post(p, b) { return this.request('POST', p, b ?? {}); },
  put(p, b) { return this.request('PUT', p, b); },
  patch(p, b) { return this.request('PATCH', p, b); },
  del(p) { return this.request('DELETE', p); }
};

window.API = API;
