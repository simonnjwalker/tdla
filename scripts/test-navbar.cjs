const assert = require('node:assert/strict');
const fs = require('node:fs');
const vm = require('node:vm');
const code = fs.readFileSync('js/navbar-session.js', 'utf8');

class Element {
    constructor(tag) { this.tag = tag; this.children = []; this.dataset = {}; this.events = {}; }
    append(child) { this.children.push(child); }
    insertBefore(child, before) { this.children.splice(this.children.indexOf(before), 0, child); }
    querySelector() { return this.children.find(child => child.dataset.accountNav); }
    addEventListener(name, handler) { this.events[name] = handler; }
    submit() { this.submitted = true; }
}

async function scenario(session, expected, fails = false) {
    const nav = new Element('ul');
    const home = new Element('li');
    const homeLink = new Element('a'); homeLink.textContent = 'Home'; home.append(homeLink); nav.append(home);
    const body = new Element('body');
    const document = { getElementById: () => nav, createElement: tag => new Element(tag), body };
    const context = vm.createContext({ document, fetch: async (url, options) => {
        assert.equal(url, '/api/session');
        assert.equal(options.credentials, 'same-origin');
        assert.equal(options.cache, 'no-store');
        if (fails) throw new Error('Offline');
        return { ok: true, json: async () => session };
    } });
    await vm.runInContext(code, context);
    assert.deepEqual(nav.children.map(item => item.children[0].textContent), expected);
    await vm.runInContext(code, context);
    assert.equal(nav.children.length, expected.length, 'Repeated script loading must not duplicate navigation');
    if (session.authenticated && !fails) {
        const logout = nav.children.at(-1).children[0];
        assert.equal(logout.href, '/account/logout');
        let prevented = false;
        logout.events.click({ preventDefault() { prevented = true; } });
        assert.equal(prevented, true);
        const form = body.children.at(-1);
        assert.equal(form.method, 'post');
        assert.equal(form.action, '/account/logout');
        assert.equal(form.submitted, true);
        assert.equal(form.children[0].name, '__RequestVerificationToken');
        assert.equal(form.children[0].value, session.logoutToken);
    }
}

(async () => {
    await scenario({ authenticated: false }, ['Home', 'Sign in']);
    await scenario({ authenticated: true, admin: false, logoutToken: 'member-token' }, ['Home', 'Files', 'Sign Out']);
    await scenario({ authenticated: true, admin: true, logoutToken: 'admin-token', username: 'must-not-be-shown@example.com' }, ['Home', 'Files', 'Administration', 'Sign Out']);
    await scenario({}, ['Home', 'Sign in'], true);
    console.log('PASS: navbar order, role visibility, no email label, CSRF-protected sign out, duplicate prevention and network fallback.');
})().catch(error => { console.error(error); process.exitCode = 1; });
