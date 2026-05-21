// Bilingual (en/ar) + full RTL/LTR. Switching language flips the whole page direction.
const I18N = {
  lang: localStorage.getItem('tf_lang') || 'en',
  dict: {},

  isRtl() { return this.lang === 'ar'; },

  async load() {
    const res = await fetch(`/assets/locales/${this.lang}.json`);
    this.dict = await res.json();
    this.applyDocument();
    this.translate(document);
  },

  t(key) { return this.dict[key] ?? key; },

  // Sets <html lang/dir> and swaps the Bootstrap stylesheet for the RTL build.
  applyDocument() {
    const html = document.documentElement;
    html.lang = this.lang;
    html.dir = this.isRtl() ? 'rtl' : 'ltr';

    const bs = document.getElementById('bootstrap-css');
    if (bs) {
      bs.href = this.isRtl()
        ? 'https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/css/bootstrap.rtl.min.css'
        : 'https://cdn.jsdelivr.net/npm/bootstrap@5.3.3/dist/css/bootstrap.min.css';
    }
  },

  // Replaces text/placeholders for any element carrying data-i18n / data-i18n-ph.
  translate(root) {
    root.querySelectorAll('[data-i18n]').forEach(el => {
      el.textContent = this.t(el.getAttribute('data-i18n'));
    });
    root.querySelectorAll('[data-i18n-ph]').forEach(el => {
      el.setAttribute('placeholder', this.t(el.getAttribute('data-i18n-ph')));
    });
  },

  async toggle() {
    this.lang = this.isRtl() ? 'en' : 'ar';
    localStorage.setItem('tf_lang', this.lang);
    await this.load();
    document.dispatchEvent(new CustomEvent('lang-changed'));
  }
};

window.I18N = I18N;
