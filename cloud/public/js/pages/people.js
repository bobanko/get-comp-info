import { api } from '../api.js';
import { errorBox, h, replace, toast } from '../dom.js';
import { formatDate, hasRole, isBlank, personLinks } from '../logic.js';
import { looseKey, queryWords, splitMatches, textMatches } from '../search-logic.js';
import { invalidateDevices } from './devices.js';

// Survives navigation to a device and back.
const state = { query: '' };

// People devices are linked to by number: the actual user and the person materially responsible (МВО).
// personId: the card of one person (#/people/12), linked from devices, property and the journal.
export async function renderPeople(context, personId = null) {
  const { main } = context;
  replace(main, h('p', { class: 'muted' }, personId === null ? 'Загрузка списка людей…' : 'Загрузка…'));
  let people;
  let devices;
  let items;
  try {
    [{ people }, { devices }, { items }] = await Promise.all([api('/people'), api('/devices'), api('/items')]);
  } catch (error) {
    replace(main, h('h1', {}, 'Люди'), errorBox(error, () => renderPeople(context, personId)));
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
  draw(context, people, links, { editing: null });
}

function draw(context, people, links, view) {
  const { me, main } = context;
  const redraw = (changes = {}) => draw(context, people, links, { ...view, ...changes });
  const reload = () => renderPeople(context);
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
    if (view.editing === person.id) return editRow(person, own);
    return h(
      'tr',
      { id: 'person-' + person.id },
      h('td', { class: 'mono muted' }, '#' + person.id),
      h('td', {}, h('a', { href: '#/people/' + person.id }, marked(person.name, words))),
      h('td', { class: 'comment' }, marked(person.note, words)),
      h('td', {}, deviceList(own.userId)),
      h('td', {}, deviceList(own.ownerId)),
      h('td', { class: 'nowrap' }, itemsLink(person.id, own.items)),
      h('td', { class: 'muted small nowrap', title: person.updatedBy }, formatDate(person.updatedAt)),
      h(
        'td',
        { class: 'row-actions' },
        editor && h('button', { class: 'button small', type: 'button', onclick: () => redraw({ editing: person.id }) }, 'Изменить'),
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
      'Справочник людей, к которым привязаны компьютеры и имущество по номеру: пользователь по факту и матеріально відповідальна особа (МВО) по ведомости. Номер не меняется и не используется повторно.',
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
            h('tr', {}, h('th', {}, '№'), h('th', {}, 'ПІБ'), h('th', {}, 'Примечание'), h('th', {}, 'Пользователь компьютеров'), h('th', {}, 'МВО компьютеров'), h('th', {}, 'МВО имущества'), h('th', {}, 'Изменено'), h('th')),
          ),
          tbody,
        ),
      ),
    ),
  );
  fill();
}

// The card of one person: name and note, the computers where the person is the user or the МВО, the property.
function drawPerson(context, people, devices, items, id, view) {
  const { me, main } = context;
  const back = h('a', { class: 'back', href: '#/people' }, '← Все люди');
  const person = people.find((p) => p.id === id);
  if (!person) {
    replace(main, back, h('div', { class: 'notice notice-error' }, h('p', {}, `Человека #${id} нет в списке: его удалили или номер неверный.`)));
    return;
  }
  const redraw = (changes = {}) => drawPerson(context, people, devices, items, id, { ...view, ...changes });
  const live = devices.filter((device) => !device.deleted);
  const asUser = live.filter((device) => device.data.userId === id);
  const asOwner = live.filter((device) => device.data.ownerId === id);
  const own = items.filter((item) => item.ownerId === id);
  const linked = asUser.length + asOwner.length + own.length;

  const remove = async () => {
    if (!confirm(`Удалить «${person.name}» (#${person.id}) из списка? Номер больше не будет использован.`)) return;
    try {
      await api('/people/' + person.id, { method: 'DELETE' });
      toast('Удалено.');
      location.hash = '#/people';
    } catch (error) {
      toast(error.message, 'error');
    }
  };

  let head;
  if (view.editing) {
    const name = h('input', { value: person.name, maxlength: '200', 'aria-label': 'ПІБ' });
    const note = h('input', { value: person.note, maxlength: '500', 'aria-label': 'Примечание', placeholder: 'підрозділ, посада…' });
    head = h(
      'form',
      {
        class: 'panel inline-form',
        onsubmit: async (event) => {
          event.preventDefault();
          try {
            await api('/people/' + person.id, { method: 'PATCH', body: { name: name.value, note: note.value } });
            invalidateDevices();
            toast('Сохранено.');
            await renderPeople(context, id);
          } catch (error) {
            toast(error.message, 'error');
          }
        },
      },
      name,
      note,
      h('button', { class: 'button primary', type: 'submit' }, 'Сохранить'),
      h('button', { class: 'button', type: 'button', onclick: () => redraw({ editing: false }) }, 'Отмена'),
    );
  } else {
    head = h(
      'div',
      { class: 'card-head' },
      h('div', {}, h('h1', {}, person.name), h('p', { class: 'muted' }, `№${person.id}`, person.note ? ` · ${person.note}` : '')),
      h(
        'div',
        { class: 'actions' },
        hasRole(me, 'editor') && h('button', { class: 'button primary', type: 'button', onclick: () => redraw({ editing: true }) }, 'Изменить'),
        hasRole(me, 'admin') &&
          h(
            'button',
            {
              class: 'button danger',
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

  const deviceTable = (list) =>
    list.length === 0
      ? h('p', { class: 'muted' }, 'Нет.')
      : h(
          'div',
          { class: 'table-wrap' },
          h(
            'table',
            {},
            h('thead', {}, h('tr', {}, h('th', {}, 'Имя компьютера'), h('th', {}, 'Модель'), h('th', {}, 'Серийный номер'), h('th', {}, 'Комментарий'))),
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
    h('section', { class: 'panel' }, h('h2', {}, `Пользователь компьютеров (${asUser.length})`), deviceTable(asUser)),
    h('section', { class: 'panel' }, h('h2', {}, `МВО компьютеров (${asOwner.length})`), deviceTable(asOwner)),
    h(
      'section',
      { class: 'panel' },
      h('h2', {}, `МВО имущества (${own.length})`, own.length > 0 ? ' ' : '', own.length > 0 && h('a', { class: 'small', href: '#/items/owner/' + id }, 'открыть в «Имуществе»')),
      own.length === 0
        ? h('p', { class: 'muted' }, 'Нет.')
        : h(
            'div',
            { class: 'table-wrap' },
            h(
              'table',
              {},
              h('thead', {}, h('tr', {}, h('th', {}, '№'), h('th', {}, 'Тип'), h('th', {}, 'Модель'), h('th', {}, 'Серийный номер'), h('th', {}, 'Место'), h('th', {}, 'Примечание'))),
              h(
                'tbody',
                {},
                own.map((item) =>
                  h(
                    'tr',
                    {},
                    h('td', { class: 'mono muted' }, '#' + item.id),
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
    h('p', { class: 'muted small', title: person.updatedBy }, `Изменено ${formatDate(person.updatedAt)}`),
  );
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
