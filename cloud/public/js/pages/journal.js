import { api } from '../api.js';
import { errorBox, h, replace } from '../dom.js';
import { authorTitle, formatDate, HISTORY_SOURCES, journalActor, journalLine } from '../logic.js';

const CATEGORIES = [
  { value: 'all', label: 'Всё' },
  { value: 'devices', label: 'Устройства' },
  { value: 'access', label: 'Доступ и ключи' },
  { value: 'programs', label: 'Версии и настройки' },
];

// Who did what in the cloud, newest first: device changes, keys, users, access requests, program versions and
// network settings in one list (GET /api/web/audit).
export async function renderJournal(context, category = 'all') {
  const { main } = context;
  replace(main, h('h1', {}, 'Журнал'), h('p', { class: 'muted' }, 'Загрузка журнала…'));
  let page;
  try {
    page = await load(category, null);
  } catch (error) {
    replace(main, h('h1', {}, 'Журнал'), errorBox(error, () => renderJournal(context, category)));
    return;
  }

  const body = h('tbody', {});
  const more = h('div', { class: 'actions' });
  const loadMore = async (cursor) => {
    replace(more, h('p', { class: 'muted' }, 'Загрузка…'));
    try {
      show(await load(category, cursor));
    } catch (error) {
      replace(more, errorBox(error, () => loadMore(cursor)));
    }
  };
  const show = (result) => {
    body.append(...result.entries.map(row));
    replace(more, result.next && h('button', { class: 'button', onclick: () => loadMore(result.next) }, 'Показать ещё'));
  };

  replace(
    main,
    h('h1', {}, 'Журнал'),
    h('p', { class: 'lead' }, 'Кто и когда менял записи, ключи, пользователей, версии программ и сетевые настройки. Сначала новые.'),
    h(
      'div',
      { class: 'segmented tabs', role: 'group', 'aria-label': 'Что показать' },
      CATEGORIES.map((c) =>
        h('button', { 'aria-pressed': String(c.value === category), onclick: () => renderJournal(context, c.value) }, c.label),
      ),
    ),
    page.entries.length === 0
      ? h('section', { class: 'panel' }, h('p', { class: 'empty' }, 'Здесь пока ничего нет.'))
      : h(
          'section',
          { class: 'panel' },
          h(
            'div',
            { class: 'table-wrap' },
            h('table', {}, h('thead', {}, h('tr', {}, h('th', {}, 'Когда'), h('th', {}, 'Кто'), h('th', {}, 'Что'), h('th', {}, ''))), body),
          ),
          more,
        ),
  );
  show(page);
}

function load(category, cursor) {
  const query = new URLSearchParams();
  if (category !== 'all') query.set('category', category);
  if (cursor) query.set('cursor', cursor);
  const text = query.toString();
  return api('/audit' + (text ? '?' + text : ''));
}

function row(entry) {
  const line = journalLine(entry);
  const source = entry.kind === 'device' ? HISTORY_SOURCES[entry.source] : null;
  return h(
    'tr',
    {},
    h('td', { class: 'nowrap', title: entry.at }, formatDate(entry.at)),
    h('td', { title: entry.kind === 'release' || entry.kind === 'suspector-release' ? '' : authorTitle(entry.actorName, entry.actor, entry.actorKey) }, journalActor(entry)),
    h('td', {}, line.what, source && h('span', { class: 'muted small' }, ` · ${source}`)),
    h('td', {}, line.href ? h('a', { href: line.href }, line.object) : line.object),
  );
}
