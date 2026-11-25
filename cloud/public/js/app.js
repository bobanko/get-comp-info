import { api } from './api.js';
import { errorBox, h, replace } from './dom.js';
import { f, LANG, LANGUAGES, languageChoice, setLanguageChoice, t } from './i18n.js';
import { hasRole, ROLE_LABELS } from './logic.js';
import { renderDevice } from './pages/device.js';
import { renderDevices } from './pages/devices.js';
import { renderDownloads } from './pages/downloads.js';
import { renderItems } from './pages/items.js';
import { renderJournal } from './pages/journal.js';
import { renderKeys } from './pages/keys.js';
import { renderNetwork } from './pages/network.js';
import { renderPeople } from './pages/people.js';
import { renderReleases } from './pages/releases.js';
import { renderTriggerWords } from './pages/trigger-words.js';
import { renderUsers } from './pages/users.js';

document.documentElement.lang = LANG;

const main = document.getElementById('main');
const nav = document.getElementById('nav');
const whoami = document.getElementById('whoami');
const topbar = document.querySelector('.topbar');
const menuToggle = document.getElementById('menu-toggle');
const menuCurrent = document.getElementById('menu-current');

menuToggle.setAttribute('aria-label', t('Меню'));
nav.setAttribute('aria-label', t('Разделы'));

function setMenuOpen(open) {
  topbar.classList.toggle('open', open);
  menuToggle.setAttribute('aria-expanded', String(open));
}

menuToggle.addEventListener('click', () => setMenuOpen(!topbar.classList.contains('open')));
// A tap on the section already open changes no hash: close the menus anyway.
nav.addEventListener('click', (event) => {
  if (!event.target.closest('a')) return;
  setMenuOpen(false);
  nav.querySelector('details[open]')?.removeAttribute('open');
});
// The admin submenu of wide screens closes on a click anywhere else.
document.addEventListener('click', (event) => {
  const open = nav.querySelector('details[open]');
  if (open && !open.contains(event.target)) open.removeAttribute('open');
});

// Groups: the records (devices, property, people) in the bar, the rest, used much less often, in a submenu.
// short: the label of the phone menu button, where the full one does not fit next to the brand.
const PAGES = [
  { hash: '#/devices', icon: '💻', label: t('Устройства'), role: 'viewer', group: 'records' },
  { hash: '#/items', icon: '🗃️', label: t('Майно'), role: 'viewer', group: 'records' },
  { hash: '#/people', icon: '🧑', label: t('Особовий склад'), short: 'О/С', role: 'viewer', group: 'records' },
  { hash: '#/users', icon: '👥', label: t('Пользователи'), role: 'admin', group: 'more' },
  { hash: '#/keys', icon: '🔑', label: t('Ключи'), role: 'harvester', group: 'more' },
  { hash: '#/downloads', icon: '📥', label: t('Загрузки'), role: 'viewer', group: 'more' },
  { hash: '#/network', icon: '🌐', label: t('Настройки сети'), role: 'admin', group: 'more' },
  { hash: '#/trigger-words', icon: '🔎', label: t('Суспектор: слова-триггеры'), role: 'admin', group: 'more' },
  { hash: '#/releases', icon: '📦', label: t('Версии программ'), role: 'admin', group: 'more' },
  { hash: '#/journal', icon: '📜', label: t('Журнал'), role: 'admin', group: 'more' },
];

let me = null;
let currentHash = null;
// Access requests waiting for an admin, shown next to "Пользователи".
let pendingRequests = 0;

// Signed in through Cloudflare Access, but not (yet) let in by the app.
const NO_ACCESS = {
  access_pending: t('Запрос отправлен'),
  access_denied: t('Нет доступа'),
  user_disabled: t('Доступ отключён'),
  access_requests_full: t('Нет доступа'),
};
// Position in the device list, restored when coming back from a device card.
let listScroll = 0;

async function start() {
  replace(main, h('p', { class: 'muted' }, t('Загрузка…')));
  try {
    me = await api('/me');
  } catch (error) {
    replace(main, NO_ACCESS[error.code] ? noAccess(error) : errorBox(error, start));
    return;
  }
  replace(whoami, h('span', {}, me.name), h('span', { class: 'role' }, ROLE_LABELS[me.role] ?? me.role), languageSelect());
  if (hasRole(me, 'admin')) await refreshPendingRequests();
  route();
}

// The interface language: as the browser (the default) or chosen; a change reloads the page.
function languageSelect() {
  const choice = languageChoice();
  return h(
    'select',
    {
      class: 'lang-select',
      'aria-label': t('Язык интерфейса'),
      title: t('Язык интерфейса'),
      onchange: (event) => {
        setLanguageChoice(event.target.value);
        location.reload();
      },
    },
    h('option', { value: 'system', selected: choice === 'system' }, f('Язык браузера ({0})', LANG.toUpperCase())),
    Object.entries(LANGUAGES).map(([code, name]) => h('option', { value: code, selected: choice === code }, name)),
  );
}

function noAccess(error) {
  return h(
    'section',
    { class: 'panel no-access' },
    h('h1', {}, NO_ACCESS[error.code]),
    h('p', {}, error.message),
    h(
      'div',
      { class: 'actions' },
      h('button', { class: 'button primary', onclick: start }, t('Проверить снова')),
      // Ends the Cloudflare Access session, e.g. to sign in with another email.
      h('a', { class: 'button', href: '/cdn-cgi/access/logout' }, t('Войти с другой почтой')),
    ),
    h('p', {}, languageSelect()),
  );
}

async function refreshPendingRequests() {
  try {
    pendingRequests = (await api('/access-requests')).requests.length;
  } catch {
    pendingRequests = 0;
  }
}

// Phones keep a tab open for days and bring it back without a reload, so the counter loaded at start-up goes
// stale there. It is asked again on every section change, when the page is shown again and once a minute
// while it is on screen; a change redraws the menu and the users page, where the requests are approved.
const REQUESTS_POLL_MS = 60_000;
let requestsCheck = null;

function checkPendingRequests() {
  if (!me || !hasRole(me, 'admin') || document.visibilityState !== 'visible') return;
  requestsCheck ??= (async () => {
    const before = pendingRequests;
    await refreshPendingRequests();
    if (pendingRequests === before) return;
    renderNav(currentHash);
    if (currentHash === '#/users') renderUsers(pageContext());
  })().finally(() => {
    requestsCheck = null;
  });
}

document.addEventListener('visibilitychange', checkPendingRequests);
window.addEventListener('pageshow', (event) => event.persisted && checkPendingRequests());
setInterval(checkPendingRequests, REQUESTS_POLL_MS);

function requestsBadge() {
  return pendingRequests > 0 && h('span', { class: 'nav-count', title: t('Запросы на доступ') }, String(pendingRequests));
}

function renderNav(current) {
  const page = PAGES.find((p) => current.startsWith(p.hash));
  replace(
    menuCurrent,
    page && h('span', { class: 'nav-icon', 'aria-hidden': 'true' }, page.icon),
    page ? page.short ?? page.label : t('Меню'),
    requestsBadge(),
  );
  const link = (p) =>
    h(
      'a',
      { href: p.hash, class: current.startsWith(p.hash) ? 'active' : undefined },
      h('span', { class: 'nav-icon', 'aria-hidden': 'true' }, p.icon),
      p.label,
      p.hash === '#/users' && requestsBadge(),
    );
  const allowed = PAGES.filter((p) => hasRole(me, p.role));
  const group = (name) => allowed.filter((p) => p.group === name);
  const more = group('more');
  replace(
    nav,
    h('div', { class: 'nav-group' }, group('records').map(link)),
    // Admins have their pages there too; for the others it holds only the keys and the downloads.
    more.length > 0 &&
      h(
        'details',
        { class: more.some((p) => current.startsWith(p.hash)) ? 'nav-admin active' : 'nav-admin' },
        h('summary', {}, h('span', { class: 'nav-icon', 'aria-hidden': 'true' }, '⚙️'), hasRole(me, 'admin') ? t('Админка') : t('Ещё'), requestsBadge()),
        h('div', { class: 'nav-admin-body' }, more.map(link)),
      ),
  );
}

function pageContext() {
  return {
    me,
    main,
    // Pages call this after approving or rejecting access requests.
    onRequestsChanged: async () => {
      await refreshPendingRequests();
      renderNav(currentHash);
    },
  };
}

async function route() {
  if (!me) return;
  const hash = location.hash || '#/devices';
  if (currentHash === '#/devices') listScroll = window.scrollY;
  currentHash = hash;
  setMenuOpen(false);
  renderNav(hash);
  checkPendingRequests();
  const context = pageContext();
  const deviceMatch = /^#\/devices\/(.+)$/.exec(hash);
  const personMatch = /^#\/people(?:\/(\d+))?$/.exec(hash);
  const itemsMatch = /^#\/items(?:\/owner\/(\d+))?$/.exec(hash);
  if (deviceMatch) {
    renderDevice(context, decodeURIComponent(deviceMatch[1]));
  } else if (itemsMatch) {
    renderItems(context, itemsMatch[1] ? Number(itemsMatch[1]) : null);
  } else if (personMatch) {
    renderPeople(context, personMatch[1] ? Number(personMatch[1]) : null);
  } else if (hash === '#/keys' && hasRole(me, 'harvester')) {
    renderKeys(context);
  } else if (hash === '#/downloads') {
    renderDownloads(context);
  } else if (hash === '#/users' && hasRole(me, 'admin')) {
    renderUsers(context);
  } else if (hash === '#/network' && hasRole(me, 'admin')) {
    renderNetwork(context);
  } else if (hash === '#/trigger-words' && hasRole(me, 'admin')) {
    renderTriggerWords(context);
  } else if (hash === '#/journal' && hasRole(me, 'admin')) {
    renderJournal(context);
  } else if (hash === '#/releases' && hasRole(me, 'admin')) {
    renderReleases(context);
  } else {
    if (hash !== '#/devices') history.replaceState(null, '', '#/devices');
    currentHash = '#/devices';
    renderNav('#/devices');
    await renderDevices(context);
    window.scrollTo(0, listScroll);
    return;
  }
  window.scrollTo(0, 0);
}

window.addEventListener('hashchange', route);
start();
