import { api } from './api.js';
import { errorBox, h, replace } from './dom.js';
import { hasRole, ROLE_LABELS } from './logic.js';
import { renderDevice } from './pages/device.js';
import { renderDevices } from './pages/devices.js';
import { renderKeys } from './pages/keys.js';
import { renderUsers } from './pages/users.js';

const main = document.getElementById('main');
const nav = document.getElementById('nav');
const whoami = document.getElementById('whoami');

const PAGES = [
  { hash: '#/devices', label: 'Устройства', role: 'viewer' },
  { hash: '#/keys', label: 'Мои ключи', role: 'harvester' },
  { hash: '#/users', label: 'Пользователи', role: 'admin' },
];

let me = null;
let currentHash = null;
// Position in the device list, restored when coming back from a device card.
let listScroll = 0;

async function start() {
  replace(main, h('p', { class: 'muted' }, 'Загрузка…'));
  try {
    me = await api('/me');
  } catch (error) {
    replace(main, errorBox(error, start));
    return;
  }
  replace(whoami, h('span', {}, me.name), h('span', { class: 'role' }, ROLE_LABELS[me.role] ?? me.role));
  route();
}

function renderNav(current) {
  replace(
    nav,
    PAGES.filter((page) => hasRole(me, page.role)).map((page) =>
      h('a', { href: page.hash, class: current.startsWith(page.hash) ? 'active' : undefined }, page.label),
    ),
  );
}

async function route() {
  if (!me) return;
  const hash = location.hash || '#/devices';
  if (currentHash === '#/devices') listScroll = window.scrollY;
  currentHash = hash;
  renderNav(hash);
  const context = { me, main };
  const deviceMatch = /^#\/devices\/(.+)$/.exec(hash);
  if (deviceMatch) {
    renderDevice(context, decodeURIComponent(deviceMatch[1]));
  } else if (hash === '#/keys' && hasRole(me, 'harvester')) {
    renderKeys(context);
  } else if (hash === '#/users' && hasRole(me, 'admin')) {
    renderUsers(context);
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
