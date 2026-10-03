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

  const buttons = document.querySelectorAll('[data-download-arch]');
  const versionTargets = document.querySelectorAll('[data-latest-version]');
  const statusTargets = document.querySelectorAll('[data-download-status]');
  const releasePills = document.querySelectorAll('[data-release-pill]');
  if (buttons.length) {
    const releasesUrl = 'https://github.com/guiasysstudio/GuiaPlay/releases';

    const setVersion = (value) => versionTargets.forEach(el => el.textContent = value);
    const setStatus = (value) => statusTargets.forEach(el => el.textContent = value);
    const setReleasePill = (value) => releasePills.forEach(el => el.textContent = value);
    const buttonFor = (architecture) => document.querySelector(`[data-download-arch="${architecture}"]`);
    const enableDownload = (architecture, url) => {
      const button = buttonFor(architecture);
      if (!button) return;
      button.href = url;
      button.removeAttribute('aria-disabled');
      button.hidden = false;
    };
    const disableDownloads = () => buttons.forEach(button => {
      button.removeAttribute('href');
      button.setAttribute('aria-disabled', 'true');
    });
    const showLookupFailure = () => {
      setVersion('temporariamente indisponível');
      setReleasePill('Consulta indisponível');
      setStatus('Não foi possível consultar os instaladores agora. Use o link “Ver Releases no GitHub” para tentar novamente; nenhuma versão antiga foi selecionada automaticamente.');
      disableDownloads();
    };

    disableDownloads();
    setVersion('consultando…');
    setReleasePill('Consultando Release');
    setStatus('Consultando a Release mais recente…');

    fetch('https://api.github.com/repos/guiasysstudio/GuiaPlay/releases?per_page=20', {
      headers: { 'Accept': 'application/vnd.github+json' }
    })
      .then(response => {
        if (!response.ok) throw new Error('release lookup failed');
        return response.json();
      })
      .then(releases => {
        const release = releases.find(item => {
          if (item.draft || !Array.isArray(item.assets)) return false;
          return item.assets.some(asset => /^GuiaPlay-Setup-(?!.*-win-x86\.exe$).+\.exe$/i.test(asset.name));
        });
        if (!release) throw new Error('compatible release not found');

        const x64Asset = release.assets.find(asset => /^GuiaPlay-Setup-(?!.*-win-x86\.exe$).+\.exe$/i.test(asset.name));
        const x86Asset = release.assets.find(asset => /^GuiaPlay-Setup-.+-win-x86\.exe$/i.test(asset.name));
        if (!x64Asset) throw new Error('x64 installer not found');

        const version = String(release.tag_name || release.name || 'versão publicada').replace(/^v/i, '');
        setVersion(version);
        setReleasePill('Versão disponível');
        enableDownload('x64', x64Asset.browser_download_url);
        if (x86Asset) {
          enableDownload('x86', x86Asset.browser_download_url);
          setStatus('Escolha x64 para Windows 64 bits (recomendado) ou x86 para Windows 32 bits e cenários compatíveis.');
        } else {
          const x86Button = buttonFor('x86');
          if (x86Button) x86Button.hidden = true;
          setStatus('Instalador x64 disponível. Esta Release não contém instalador x86.');
        }
      })
      .catch(showLookupFailure);

    document.querySelectorAll('[data-releases-link]').forEach(link => { link.href = releasesUrl; });
  }

  document.querySelectorAll('[data-year]').forEach(el => el.textContent = new Date().getFullYear());
})();
