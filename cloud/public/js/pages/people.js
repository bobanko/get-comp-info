import { api } from '../api.js';
import { errorBox, h, replace, toast } from '../dom.js';
import { formatDate, hasRole, isBlank, personLinks } from '../logic.js';
import { looseKey, queryWords, splitMatches, textMatches } from '../search-logic.js';
import { invalidateDevices } from './devices.js';

// Survives navigation to a device and back.
const state = { query: '' };

// People devices are linked to by number: the actual user and the person materially responsible (МОЛ).
// focusId: the person to show first (#/people/12), e.g. from a device card.
export async function renderPeople(context, focusId = null) {
  const { main } = context;
  replace(main, h('p', { class: 'muted' }, 'Загрузка списка людей…'));
  let people;
  let devices;
  let items;
  try {
    [{ people }, { devices }, { items }] = await Promise.all([api('/people'), api('/devices'), api('/items')]);
  } catch (error) {
    replace(main, h('h1', {}, 'Люди'), errorBox(error, () => renderPeople(context, focusId)));
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
  draw(context, people, links, { focusId, editing: null });
}

function draw(context, people, links, view) {
  const { me, main } = context;
  const redraw = (changes = {}) => draw(context, people, links, { ...view, ...changes });
  const reload = () => renderPeople(context, view.focusId);
  const editor = hasRole(me, 'editor');

  const tbody = h('tbody');
  const count = h('p', { class: 'muted count' });
  const fill = () => {
    const words = queryWords(state.query);
    const shown = people.filter((person) => matches(person, words));
    count.textContent = shown.length === people.length ? `Всего: ${people.length}` : `Найдено: ${shown.length} из ${people.length}`;
    replace(tbody, shown.map((person) => row(person, words)));
  };

  function row(person, words) {
    const own = links.get(person.id) ?? { userId: [], ownerId: [] };
    const focused = person.id === view.focusId;
    if (view.editing === person.id) return editRow(person, own);
    return h(
      'tr',
      { id: 'person-' + person.id, class: focused ? 'focused' : undefined },
      h('td', { class: 'mono muted' }, '#' + person.id),
      h('td', {}, marked(person.name, words)),
      h('td', { class: 'comment' }, marked(person.note, words)),
      h('td', {}, deviceList(own.userId)),
      h('td', {}, deviceList(own.ownerId)),
      h('td', { class: 'nowrap' }, itemsLink(person.id, own.items)),
      h('td', { class: 'muted small nowrap', title: person.updatedBy }, formatDate(person.updatedAt)),
      h(
        'td',
        { class: 'row-actions' },
        editor && h('button', { class: 'button small', type: 'button', onclick: () => redraw({ editing: person.id, focusId: person.id }) }, 'Изменить'),
      ),
    );
  }

  function editRow(person, own) {
    const name = h('input', { value: person.name, maxlength: '200', 'aria-label': 'ПІБ' });
    const note = h('input', { value: person.note, maxlength: '500', 'aria-label': 'Примечание', placeholder: 'підрозділ, посада…' });
    const linked = own.userId.length + own.ownerId.length + (own.items ?? 0);
    const save = async () => {
      try {
        await api('/people/' + person.id, { method: 'PATCH', body: { name: name.value, note: note.value } });
        invalidateDevices();
        toast('Сохранено.');
        await reload();
      } catch (error) {
        toast(error.message, 'error');
      }
    };
    const remove = async () => {
      if (!confirm(`Удалить «${person.name}» (#${person.id}) из списка? Номер больше не будет использован.`)) return;
      try {
        await api('/people/' + person.id, { method: 'DELETE' });
        toast('Удалено.');
        await renderPeople(context);
      } catch (error) {
        toast(error.message, 'error');
      }
    };
    return h(
      'tr',
      { id: 'person-' + person.id, class: 'focused' },
      h('td', { class: 'mono muted' }, '#' + person.id),
      h('td', {}, name),
      h('td', {}, note),
      h('td', {}, deviceList(own.userId)),
      h('td', {}, deviceList(own.ownerId)),
      h('td', { class: 'nowrap' }, itemsLink(person.id, own.items)),
      h('td'),
      h(
        'td',
        { class: 'row-actions' },
        h('button', { class: 'button small primary', type: 'button', onclick: save }, 'Сохранить'),
        h('button', { class: 'button small', type: 'button', onclick: () => redraw({ editing: null }) }, 'Отмена'),
        hasRole(me, 'admin') &&
          h(
            'button',
            {
              class: 'button small danger',
              type: 'button',
              disabled: linked > 0,
              title: linked > 0 ? 'Сначала уберите человека из карточек устройств и из имущества' : undefined,
              onclick: remove,
            },
            'Удалить',
          ),
      ),
    );
  }

  const search = h('input', {
    type: 'search',
    class: 'search',
    value: state.query,
    placeholder: 'Поиск: ПІБ, примечание, номер',
    'aria-label': 'Поиск людей',
    oninput: (event) => {
      state.query = event.target.value;
      fill();
    },
  });

  replace(
    main,
    h('h1', {}, 'Люди'),
    h(
      'p',
      { class: 'lead' },
      'Справочник людей, к которым привязаны компьютеры и имущество по номеру: пользователь по факту и материально ответственное лицо (МОЛ) по ведомости. Номер не меняется и не используется повторно.',
    ),
    editor && h('section', { class: 'panel' }, h('h2', {}, 'Добавить человека'), addForm(reload)),
    h(
      'section',
      { class: 'panel' },
      h('div', { class: 'toolbar' }, search),
      count,
      h(
        'div',
        { class: 'table-wrap' },
        h(
          'table',
          { class: 'people' },
          h(
            'thead',
            {},
            h('tr', {}, h('th', {}, '№'), h('th', {}, 'ПІБ'), h('th', {}, 'Примечание'), h('th', {}, 'Пользователь компьютеров'), h('th', {}, 'МОЛ компьютеров'), h('th', {}, 'МОЛ имущества'), h('th', {}, 'Изменено'), h('th')),
          ),
          tbody,
        ),
      ),
    ),
  );
  fill();
  const focused = view.focusId && document.getElementById('person-' + view.focusId);
  if (focused) focused.scrollIntoView({ block: 'center' });
}

function addForm(reload) {
  const name = h('input', { maxlength: '200', placeholder: 'Прізвище Ім\'я По батькові', 'aria-label': 'ПІБ', required: true });
  const note = h('input', { maxlength: '500', placeholder: 'підрозділ, посада…', 'aria-label': 'Примечание' });
  const save = h('button', { class: 'button primary', type: 'submit' }, 'Добавить');
  return h(
    'form',
    {
      class: 'inline-form',
      onsubmit: async (event) => {
        event.preventDefault();
        save.disabled = true;
        try {
          const { person } = await api('/people', { method: 'POST', body: { name: name.value, note: note.value } });
          toast(`Добавлен: ${person.name}, №${person.id}.`);
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
  );
}

// As in the device list: every word somewhere, as typed or in another alphabet.
function matches(person, words) {
  if (words.length === 0) return true;
  const text = `#${person.id}\n${person.name}\n${person.note}`.toLowerCase();
  const key = looseKey(text).key;
  return words.every((word) => textMatches(text, word, key));
}

function marked(text, words) {
  if (isBlank(text)) return '';
  return splitMatches(text, words).map((part) => (part.match ? h('mark', {}, part.text) : part.text));
}

function itemsLink(personId, count) {
  if (!count) return h('span', { class: 'muted' }, '—');
  return h('a', { href: '#/items/owner/' + personId }, `${count} шт.`);
}

function deviceList(list) {
  if (list.length === 0) return h('span', { class: 'muted' }, '—');
  return list.map((device, index) => [
    index > 0 ? ', ' : '',
    h('a', { href: '#/devices/' + encodeURIComponent(device.id) }, isBlank(device.data.hostname) ? device.id : device.data.hostname),
  ]);
}
