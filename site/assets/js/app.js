(() => {
  const menuButton = document.querySelector('[data-menu-toggle]');
  const menu = document.querySelector('[data-nav-links]');
  if (menuButton && menu) {
    menuButton.addEventListener('click', () => {
      const open = menu.classList.toggle('open');
      menuButton.setAttribute('aria-expanded', String(open));
    });
  }

  const fadeItems = document.querySelectorAll('.fade-up');
  const observer = 'IntersectionObserver' in window
    ? new IntersectionObserver((entries) => {
        entries.forEach(entry => {
          if (entry.isIntersecting) {
            entry.target.classList.add('visible');
            observer.unobserve(entry.target);
          }
        });
      }, { threshold: 0.12 })
    : null;
  fadeItems.forEach(el => observer ? observer.observe(el) : el.classList.add('visible'));

  const lightbox = document.querySelector('[data-lightbox]');
  const lightboxImage = lightbox?.querySelector('img');
  const closeButton = lightbox?.querySelector('[data-lightbox-close]');
  const closeLightbox = () => lightbox?.classList.remove('open');

  document.querySelectorAll('[data-zoom]').forEach(img => {
    img.addEventListener('click', () => {
      if (!lightbox || !lightboxImage) return;
      lightboxImage.src = img.currentSrc || img.src;
      lightboxImage.alt = img.alt || 'Captura do GuiaPlay';
      lightbox.classList.add('open');
    });
  });
  closeButton?.addEventListener('click', closeLightbox);
  lightbox?.addEventListener('click', (event) => { if (event.target === lightbox) closeLightbox(); });
  document.addEventListener('keydown', (event) => { if (event.key === 'Escape') closeLightbox(); });

  const buttons = document.querySelectorAll('[data-download-latest]');
  const versionTargets = document.querySelectorAll('[data-latest-version]');
  const statusTargets = document.querySelectorAll('[data-download-status]');
  if (buttons.length) {
    const fallbackVersion = '0.9.0-prototipo';
    const fallbackUrl = 'https://github.com/guiasysstudio/GuiaPlay/releases/download/v0.9.0-prototipo/GuiaPlay-Setup-0.9.0-prototipo.exe';

    const setVersion = (value) => versionTargets.forEach(el => el.textContent = value);
    const setStatus = (value) => statusTargets.forEach(el => el.textContent = value);
    const setUrl = (url) => buttons.forEach(el => {
      el.href = url;
      el.removeAttribute('aria-disabled');
      el.classList.remove('is-loading');
    });

    setVersion(fallbackVersion);
    setUrl(fallbackUrl);
    setStatus('Download direto do instalador para Windows x64.');

    fetch('https://api.github.com/repos/guiasysstudio/GuiaPlay/releases?per_page=20', {
      headers: { 'Accept': 'application/vnd.github+json' }
    })
      .then(response => {
        if (!response.ok) throw new Error('release lookup failed');
        return response.json();
      })
      .then(releases => {
        const release = releases.find(item => !item.draft && Array.isArray(item.assets) && item.assets.some(asset => /^GuiaPlay-Setup-.*\.exe$/i.test(asset.name)));
        if (!release) return;
        const asset = release.assets.find(asset => /^GuiaPlay-Setup-.*\.exe$/i.test(asset.name));
        if (!asset) return;
        const version = String(release.tag_name || release.name || fallbackVersion).replace(/^v/i, '');
        setVersion(version);
        setUrl(asset.browser_download_url);
      })
      .catch(() => {
        // Fallback remains active so the page still offers a working installer.
      });
  }

  document.querySelectorAll('[data-year]').forEach(el => el.textContent = new Date().getFullYear());
})();
