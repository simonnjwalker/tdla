// index.html stays static. Logout uses a POST with the session's anti-forgery token.
(async () => {
    const nav = document.getElementById('nav');
    if (!nav || nav.querySelector('[data-account-nav]')) return;
    const item = document.createElement('li');
    item.className = 'nav-item';
    item.dataset.accountNav = 'true';
    const link = document.createElement('a');
    link.href = '/account/login';
    link.textContent = 'Sign in';
    item.append(link);
    nav.append(item);
    try {
        const response = await fetch('/api/session', { credentials: 'same-origin', cache: 'no-store' });
        if (!response.ok) return;
        const session = await response.json();
        if (session.authenticated) {
            const links = [['Files', '/files']];
            if (session.admin) links.push(['Administration', '/admin']);
            for (const [label, href] of links) {
                const entry = document.createElement('li');
                entry.className = 'nav-item';
                entry.dataset.accountNav = 'true';
                const anchor = document.createElement('a');
                anchor.textContent = label;
                anchor.href = href;
                entry.append(anchor);
                nav.insertBefore(entry, item);
            }
            link.textContent = 'Sign Out';
            link.href = '/account/logout';
            if (session.logoutToken) link.addEventListener('click', (event) => {
                event.preventDefault();
                const form = document.createElement('form');
                form.method = 'post';
                form.action = '/account/logout';
                form.hidden = true;
                const token = document.createElement('input');
                token.type = 'hidden';
                token.name = '__RequestVerificationToken';
                token.value = session.logoutToken;
                form.append(token);
                document.body.append(form);
                form.submit();
            });
        }
    } catch { /* The sign-in link remains available if the session check fails. */ }
})();
