import { api } from '../api.js';
import { errorBox, h, replace, toast } from '../dom.js';
import { f, t } from '../i18n.js';
import { focusSearch, listToolbar, marked } from '../list-toolbar.js';
import { formatDate, hasRole, isBlank, personLinks } from '../logic.js';
import { looseKey, queryWords, textMatches } from '../search-logic.js';
import { invalidateDevices } from './devices.js';

// Survives navigation to a device and back.
const state = { query: '' };

// О/С (особовий склад): people devices and property are linked to by number — the actual user and the person
// materially responsible (МВО). The number stays: it tells namesakes apart in the forms («ПІБ · #12»).
// personId: the card of one person (#/people/12), linked from devices, property and the journal.
export async function renderPeople(context, personId = null) {
  const { main } = context;
  replace(main, h('p', { class: 'muted' }, personId === null ? t('Загрузка О/С…') : t('Загрузка…')));
  let people;
  let devices;
  let items;
  try {
    [{ people }, { devices }, { items }] = await Promise.all([api('/people'), api('/devices'), api('/items')]);
  } catch (error) {
    replace(main, h('h1', {}, 'О/С'), errorBox(error, () => renderPeople(context, personId)));
    return;
  }
  if (personId !== null) {
    drawPerson(context, people, devices, items, personId, { editing: false });
    return;
  }
  const links = personLinks(devices);
  // Items of property each person is responsible for.
  for (const item of items) {
    if (item.ownerId === null) continue;
    if (!links.has(item.ownerId)) links.set(item.ownerId, { userId: [], ownerId: [] });
    const own = links.get(item.ownerId);
    own.items = (own.items ?? 0) + 1;
  }
  draw(context, people, links, { editing: null, adding: false });
}

// Escape cancels the editing, as the «Отмена» button.
const escapeTo = (cancel) => (event) => {
  if (event.key !== 'Escape') return;
  event.preventDefault();
  cancel();
};

const DELETE_BLOCKED = () => t('Сначала уберите человека из карточек устройств и из майна');

function draw(context, people, links, view) {
  const { me, main } = context;
  const redraw = (changes = {}) => draw(context, people, links, { ...view, ...changes });
  const reload = () => renderPeople(context);
  const editor = hasRole(me, 'editor');
  const cancelEdit = () => redraw({ editing: null });

  const tbody = h('tbody');
  const count = h('p', { class: 'muted count' });
  const fill = () => {
    const words = queryWords(state.query);
    const shown = people.filter((person) => matches(person, words));
    count.textContent = shown.length === people.length ? f('Всего: {0}', people.length) : f('Найдено: {0} из {1}', shown.length, people.length);
    replace(tbody, shown.map((person) => row(person, words)));
    if (shown.length === 0) replace(tbody, h('tr', { class: 'empty-row' }, h('td', { colspan: '8', class: 'empty' }, t('Ничего не найдено.'))));
  };

  function row(person, words) {
    const own = links.get(person.id) ?? { userId: [], ownerId: [] };
    if (view.editing === person.id) return editRow(person, own);
    return h(
      'tr',
      { id: 'person-' + person.id },
      h('td', { class: 'mono muted', 'data-col': 'id' }, marked('#' + person.id, words)),
      h('td', { 'data-col': 'name' }, h('a', { href: '#/people/' + person.id }, marked(person.name, words))),
      h('td', { class: 'comment', 'data-col': 'note' }, marked(person.note, words)),
      h('td', { 'data-col': 'user', 'data-label': t('Пользователь') }, deviceList(own.userId)),
      h('td', { 'data-col': 'owner', 'data-label': t('МВО компьютеров') }, deviceList(own.ownerId)),
      h('td', { class: 'nowrap', 'data-col': 'items', 'data-label': t('МВО майна') }, itemsLink(person.id, own.items)),
      h('td', { class: 'muted small nowrap', 'data-col': 'updated', title: person.updatedBy }, formatDate(person.updatedAt)),
      h(
        'td',
        { class: 'row-actions' },
        editor && h('button', { class: 'button small', type: 'button', onclick: () => redraw({ editing: person.id, adding: false }) }, t('Изменить')),
      ),
    );
  }

  function editRow(person, own) {
    const name = h('input', { value: person.name, maxlength: '200', 'aria-label': 'ПІБ' });
    const note = h('input', { value: person.note, maxlength: '500', 'aria-label': t('Примечание'), placeholder: t('підрозділ, посада…') });
    const linked = own.userId.length + own.ownerId.length + (own.items ?? 0);
    const save = async () => {
      try {
        await api('/people/' + person.id, { method: 'PATCH', body: { name: name.value, note: note.value } });
        invalidateDevices();
        toast(t('Сохранено.'));
        await reload();
      } catch (error) {
        toast(error.message, 'error');
      }
    };
    const remove = async () => {
      if (!confirm(f('Удалить «{0}» (#{1}) из О/С? Номер больше не будет использован.', person.name, person.id))) return;
      try {
        await api('/people/' + person.id, { method: 'DELETE' });
        toast(t('Удалено.'));
        await renderPeople(context);
      } catch (error) {
        toast(error.message, 'error');
      }
    };
    return h(
      'tr',
      { id: 'person-' + person.id, class: 'focused', onkeydown: escapeTo(cancelEdit) },
      h('td', { class: 'mono muted', 'data-col': 'id' }, '#' + person.id),
      h('td', { 'data-col': 'name' }, name),
      h('td', { 'data-col': 'note' }, note),
      h('td', { 'data-col': 'user', 'data-label': t('Пользователь') }, deviceList(own.userId)),
      h('td', { 'data-col': 'owner', 'data-label': t('МВО компьютеров') }, deviceList(own.ownerId)),
      h('td', { class: 'nowrap', 'data-col': 'items', 'data-label': t('МВО майна') }, itemsLink(person.id, own.items)),
      h('td', { 'data-col': 'updated' }),
      h(
        'td',
        { class: 'row-actions' },
        h('button', { class: 'button small primary', type: 'button', onclick: save }, t('Сохранить')),
        h('button', { class: 'button small', type: 'button', title: t('Отмена (Esc)'), onclick: cancelEdit }, t('Отмена')),
        hasRole(me, 'admin') &&
          h(
            'button',
            { class: 'button small danger', type: 'button', disabled: linked > 0, title: linked > 0 ? DELETE_BLOCKED() : undefined, onclick: remove },
            t('Удалить'),
          ),
      ),
    );
  }

  const { toolbar, search } = listToolbar({
    name: 'people-filter',
    placeholder: t('Поиск: ПІБ, примечание, номер'),
    query: state.query,
    onQuery: (query) => {
      state.query = query;
      fill();
    },
    actions: [
      editor &&
        !view.adding &&
        h('button', { class: 'button primary', type: 'button', title: t('Добавить человека'), onclick: () => redraw({ adding: true, editing: null }) }, t('+ Добавить')),
    ],
  });

  const panel = editor && view.adding && h('section', { class: 'panel add-panel' }, h('h2', {}, t('Добавить человека')), addForm(reload, () => redraw({ adding: false })));
  replace(
    main,
    h('h1', { class: 'list-title' }, t('О/С (особовий склад)')),
    h(
      'p',
      { class: 'lead list-lead' },
      t('Люди, к которым привязаны компьютеры и майно по номеру: пользователь по факту и матеріально відповідальна особа (МВО) по ведомости. Номер не меняется и не используется повторно.'),
    ),
    panel,
    toolbar,
    count,
    h(
      'div',
      { class: 'table-wrap table-scroll' },
      h(
        'table',
        { class: 'people' },
        h(
          'thead',
          {},
          h(
            'tr',
            {},
            h('th', {}, '№'),
            h('th', {}, 'ПІБ'),
            h('th', {}, t('Примечание')),
            h('th', {}, t('Пользователь компьютеров')),
            h('th', {}, t('МВО компьютеров')),
            h('th', {}, t('МВО майна')),
            h('th', {}, t('Изменено')),
            h('th'),
          ),
        ),
        tbody,
      ),
    ),
  );
  fill();
  if (panel) {
    window.scrollTo(0, 0);
    panel.querySelector('input')?.focus();
  } else if (view.editing !== null) {
    tbody.querySelector('tr.focused input')?.focus();
  } else {
    focusSearch(search, state.query);
  }
}

// The card of one person: name and note, the computers where the person is the user or the МВО, the property.
function drawPerson(context, people, devices, items, id, view) {
  const { me, main } = context;
  const back = h('a', { class: 'back', href: '#/people' }, t('← Весь О/С'));
  const person = people.find((p) => p.id === id);
  if (!person) {
    replace(main, back, h('div', { class: 'notice notice-error' }, h('p', {}, f('Человека #{0} нет в О/С: его удалили или номер неверный.', id))));
    return;
  }
  const redraw = (changes = {}) => drawPerson(context, people, devices, items, id, { ...view, ...changes });
  const live = devices.filter((device) => !device.deleted);
  const asUser = live.filter((device) => device.data.userId === id);
  const asOwner = live.filter((device) => device.data.ownerId === id);
  const own = items.filter((item) => item.ownerId === id);
  const linked = asUser.length + asOwner.length + own.length;

  const remove = async () => {
    if (!confirm(f('Удалить «{0}» (#{1}) из О/С? Номер больше не будет использован.', person.name, person.id))) return;
    try {
      await api('/people/' + person.id, { method: 'DELETE' });
      toast(t('Удалено.'));
      location.hash = '#/people';
    } catch (error) {
      toast(error.message, 'error');
    }
  };

  let head;
  if (view.editing) {
    const cancel = () => redraw({ editing: false });
    const name = h('input', { value: person.name, maxlength: '200', 'aria-label': 'ПІБ' });
    const note = h('input', { value: person.note, maxlength: '500', 'aria-label': t('Примечание'), placeholder: t('підрозділ, посада…') });
    head = h(
      'form',
      {
        class: 'panel inline-form',
        onkeydown: escapeTo(cancel),
        onsubmit: async (event) => {
          event.preventDefault();
          try {
            await api('/people/' + person.id, { method: 'PATCH', body: { name: name.value, note: note.value } });
            invalidateDevices();
            toast(t('Сохранено.'));
            await renderPeople(context, id);
          } catch (error) {
            toast(error.message, 'error');
          }
        },
      },
      name,
      note,
      h('button', { class: 'button primary', type: 'submit' }, t('Сохранить')),
      h('button', { class: 'button', type: 'button', title: t('Отмена (Esc)'), onclick: cancel }, t('Отмена')),
    );
  } else {
    head = h(
      'div',
      { class: 'card-head' },
      h('div', {}, h('h1', {}, person.name), h('p', { class: 'muted' }, `№${person.id}`, person.note ? ` · ${person.note}` : '')),
      h(
        'div',
        { class: 'actions' },
        hasRole(me, 'editor') && h('button', { class: 'button primary', type: 'button', onclick: () => redraw({ editing: true }) }, t('Изменить')),
        hasRole(me, 'admin') &&
          h(
            'button',
            { class: 'button danger', type: 'button', disabled: linked > 0, title: linked > 0 ? DELETE_BLOCKED() : undefined, onclick: remove },
            t('Удалить'),
          ),
      ),
    );
  }

  const deviceTable = (list) =>
    list.length === 0
      ? h('p', { class: 'muted' }, t('Нет.'))
      : h(
          'div',
          { class: 'table-wrap' },
          h(
            'table',
            {},
            h('thead', {}, h('tr', {}, h('th', {}, t('Имя компьютера')), h('th', {}, t('Модель')), h('th', {}, t('Серийный номер')), h('th', {}, t('Комментарий')))),
            h(
              'tbody',
              {},
              list.map((device) =>
                h(
                  'tr',
                  {},
                  h('td', {}, h('a', { href: '#/devices/' + encodeURIComponent(device.id) }, isBlank(device.data.hostname) ? device.id : device.data.hostname)),
                  h('td', {}, [device.data.manufacturer, device.data.model].filter((v) => !isBlank(v)).join(' ')),
                  h('td', { class: 'mono' }, device.data.serial ?? ''),
                  h('td', { class: 'comment', title: device.data.comment || undefined }, device.data.comment ?? ''),
                ),
              ),
            ),
          ),
        );

  replace(
    main,
    back,
    head,
    h('section', { class: 'panel' }, h('h2', {}, f('Пользователь компьютеров ({0})', asUser.length)), deviceTable(asUser)),
    h('section', { class: 'panel' }, h('h2', {}, f('МВО компьютеров ({0})', asOwner.length)), deviceTable(asOwner)),
    h(
      'section',
      { class: 'panel' },
      h('h2', {}, f('МВО майна ({0})', own.length), own.length > 0 ? ' ' : '', own.length > 0 && h('a', { class: 'small', href: '#/items/owner/' + id }, t('открыть в «Майне»'))),
      own.length === 0
        ? h('p', { class: 'muted' }, t('Нет.'))
        : h(
            'div',
            { class: 'table-wrap' },
            h(
              'table',
              {},
              h('thead', {}, h('tr', {}, h('th', {}, t('Тип')), h('th', {}, t('Модель')), h('th', {}, t('Серийный номер')), h('th', {}, t('Место')), h('th', {}, t('Примечание')))),
              h(
                'tbody',
                {},
                own.map((item) =>
                  h(
                    'tr',
                    {},
                    h('td', {}, item.type),
                    h('td', {}, item.model),
                    h('td', { class: 'mono' }, item.serial),
                    h('td', {}, item.location),
                    h('td', { class: 'comment', title: item.note || undefined }, item.note),
                  ),
                ),
              ),
            ),
          ),
    ),
    h('p', { class: 'muted small', title: person.updatedBy }, f('Изменено {0}', formatDate(person.updatedAt))),
  );
}

function addForm(reload, cancel) {
  const name = h('input', { maxlength: '200', placeholder: 'Прізвище Ім’я По батькові', 'aria-label': 'ПІБ', required: true });
  const note = h('input', { maxlength: '500', placeholder: t('підрозділ, посада…'), 'aria-label': t('Примечание') });
  const save = h('button', { class: 'button primary', type: 'submit' }, t('Добавить'));
  return h(
    'form',
    {
      class: 'inline-form',
      onkeydown: escapeTo(cancel),
      onsubmit: async (event) => {
        event.preventDefault();
        save.disabled = true;
        try {
          const { person } = await api('/people', { method: 'POST', body: { name: name.value, note: note.value } });
          toast(f('Добавлен: {0}, №{1}.', person.name, person.id));
          state.query = '';
          await reload();
        } catch (error) {
          save.disabled = false;
          toast(error.message, 'error');
        }
      },
    },
    name,
    note,
    save,
    h('button', { class: 'button', type: 'button', title: t('Отмена (Esc)'), onclick: cancel }, t('Отмена')),
  );
}

// As in the device list: every word somewhere, as typed or in another alphabet.
function matches(person, words) {
  if (words.length === 0) return true;
  const text = `#${person.id}\n${person.name}\n${person.note}`.toLowerCase();
  const key = looseKey(text).key;
  return words.every((word) => textMatches(text, word, key));
}

function itemsLink(personId, count) {
  if (!count) return h('span', { class: 'muted' }, '—');
  return h('a', { href: '#/items/owner/' + personId }, f('{0} шт.', count));
}

function deviceList(list) {
  if (list.length === 0) return h('span', { class: 'muted' }, '—');
  return list.map((device, index) => [
    index > 0 ? ', ' : '',
    h('a', { href: '#/devices/' + encodeURIComponent(device.id) }, isBlank(device.data.hostname) ? device.id : device.data.hostname),
  ]);
}
