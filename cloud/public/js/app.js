import { api } from './api.js';
import { errorBox, h, replace } from './dom.js';
import { hasRole, ROLE_LABELS } from './logic.js';
import { renderDevice } from './pages/device.js';
import { renderDevices } from './pages/devices.js';
import { renderKeys } from './pages/keys.js';
import { renderNetwork } from './pages/network.js';
import { renderReleases } from './pages/releases.js';
import { renderUsers } from './pages/users.js';

const main = document.getElementById('main');
const nav = document.getElementById('nav');
const whoami = document.getElementById('whoami');

const PAGES = [
  { hash: '#/devices', icon: '💻', label: 'Устройства', role: 'viewer' },
  { hash: '#/keys', icon: '🔑', label: 'Мои ключи', role: 'harvester' },
  { hash: '#/users', icon: '👥', label: 'Пользователи', role: 'admin' },
  { hash: '#/network', icon: '🌐', label: 'Сеть', role: 'admin' },
  { hash: '#/releases', icon: '📦', label: 'Версии программы', role: 'admin' },
];

let me = null;
let currentHash = null;
// Access requests waiting for an admin, shown next to "Пользователи".
let pendingRequests = 0;

// Signed in through Cloudflare Access, but not (yet) let in by the app.
const NO_ACCESS = {
  access_pending: 'Запрос отправлен',
  access_denied: 'Нет доступа',
  user_disabled: 'Доступ отключён',
  access_requests_full: 'Нет доступа',
};
// Position in the device list, restored when coming back from a device card.
let listScroll = 0;

async function start() {
  replace(main, h('p', { class: 'muted' }, 'Загрузка…'));
  try {
    me = await api('/me');
  } catch (error) {
    replace(main, NO_ACCESS[error.code] ? noAccess(error) : errorBox(error, start));
    return;
  }
  replace(whoami, h('span', {}, me.name), h('span', { class: 'role' }, ROLE_LABELS[me.role] ?? me.role));
  if (hasRole(me, 'admin')) await refreshPendingRequests();
  route();
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
      h('button', { class: 'button primary', onclick: start }, 'Проверить снова'),
      // Ends the Cloudflare Access session, e.g. to sign in with another email.
      h('a', { class: 'button', href: '/cdn-cgi/access/logout' }, 'Войти с другой почтой'),
    ),
  );
}

async function refreshPendingRequests() {
  try {
    pendingRequests = (await api('/access-requests')).requests.length;
  } catch {
    pendingRequests = 0;
  }
}

function renderNav(current) {
  replace(
    nav,
    PAGES.filter((page) => hasRole(me, page.role)).map((page) =>
      h(
        'a',
        { href: page.hash, class: current.startsWith(page.hash) ? 'active' : undefined },
        h('span', { class: 'nav-icon', 'aria-hidden': 'true' }, page.icon),
        page.label,
        page.hash === '#/users' && pendingRequests > 0 && h('span', { class: 'nav-count', title: 'Запросы на доступ' }, String(pendingRequests)),
      ),
    ),
  );
}

async function route() {
  if (!me) return;
  const hash = location.hash || '#/devices';
  if (currentHash === '#/devices') listScroll = window.scrollY;
  currentHash = hash;
  renderNav(hash);
  const context = {
    me,
    main,
    // Pages call this after approving or rejecting access requests.
    onRequestsChanged: async () => {
      await refreshPendingRequests();
      renderNav(currentHash);
    },
  };
  const deviceMatch = /^#\/devices\/(.+)$/.exec(hash);
  if (deviceMatch) {
    renderDevice(context, decodeURIComponent(deviceMatch[1]));
  } else if (hash === '#/keys' && hasRole(me, 'harvester')) {
    renderKeys(context);
  } else if (hash === '#/users' && hasRole(me, 'admin')) {
    renderUsers(context);
  } else if (hash === '#/network' && hasRole(me, 'admin')) {
    renderNetwork(context);
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
