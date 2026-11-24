import { api } from '../api.js';
import { errorBox, h, replace } from '../dom.js';
import { t } from '../i18n.js';
import { formatDate, journalActor, journalActorTitle, journalLine } from '../logic.js';

const CATEGORIES = [
  { value: 'all', label: t('Всё') },
  { value: 'devices', label: t('Учёт') },
  { value: 'access', label: t('Доступ и ключи') },
  { value: 'programs', label: t('Версии и настройки') },
];

// Who did what in the cloud, newest first: device changes, keys, users, access requests, program versions and
// network settings in one list (GET /api/web/audit). 🔑 marks a change through a console key, 🌐 one on the site.
export async function renderJournal(context, category = 'all') {
  const { main } = context;
  replace(main, h('h1', {}, t('Журнал')), h('p', { class: 'muted' }, t('Загрузка журнала…')));
  let page;
  try {
    page = await load(category, null);
  } catch (error) {
    replace(main, h('h1', {}, t('Журнал')), errorBox(error, () => renderJournal(context, category)));
    return;
  }

  const body = h('tbody', {});
  const more = h('div', { class: 'actions' });
  const loadMore = async (cursor) => {
    replace(more, h('p', { class: 'muted' }, t('Загрузка…')));
    try {
      show(await load(category, cursor));
    } catch (error) {
      replace(more, errorBox(error, () => loadMore(cursor)));
    }
  };
  const show = (result) => {
    body.append(...result.entries.map(row));
    replace(more, result.next && h('button', { class: 'button', onclick: () => loadMore(result.next) }, t('Показать ещё')));
  };

  replace(
    main,
    h('h1', {}, t('Журнал')),
    h('p', { class: 'lead' }, t('Кто и когда менял записи, ключи, пользователей, версии программ и сетевые настройки. Сначала новые. 🔑 — изменено программой по ключу, 🌐 — на сайте.')),
    h(
      'div',
      { class: 'segmented tabs', role: 'group', 'aria-label': t('Что показать') },
      CATEGORIES.map((c) => h('button', { 'aria-pressed': String(c.value === category), onclick: () => renderJournal(context, c.value) }, c.label)),
    ),
    page.entries.length === 0
      ? h('section', { class: 'panel' }, h('p', { class: 'empty' }, t('Здесь пока ничего нет.')))
      : h(
          'section',
          { class: 'panel journal-panel' },
          h(
            'div',
            { class: 'table-wrap' },
            h('table', { class: 'journal' }, h('thead', {}, h('tr', {}, h('th', {}, t('Когда')), h('th', {}, t('Кто')), h('th', {}, t('Что')), h('th', {}, ''))), body),
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
  return h(
    'tr',
    {},
    h('td', { class: 'nowrap when', title: entry.at }, formatDate(entry.at)),
    h('td', { class: 'who', title: journalActorTitle(entry) }, journalActor(entry)),
    h('td', { class: 'what' }, line.what),
    h('td', { class: 'object' }, line.href ? h('a', { href: line.href }, line.object) : line.object),
  );
}
