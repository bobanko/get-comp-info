import { api } from '../api.js';
import { errorBox, h, replace, toast } from '../dom.js';
import { filterItems, findPerson, formatDate, hasRole, isBlank, itemTypes, personName, setPeople } from '../logic.js';
import { peopleDatalist, personInput, personInputText, resolvePeople } from '../person-input.js';
import { splitMatches, queryWords } from '../search-logic.js';

// Survives navigation to a person and back.
const state = { query: '', type: '' };

const FIELDS = [
  { key: 'type', label: 'Тип', max: 100 },
  { key: 'model', label: 'Модель', max: 200 },
  { key: 'serial', label: 'Серийный номер', max: 200, mono: true },
  { key: 'location', label: 'Место / секция', max: 200 },
];

// Property that is not a computer, from the property register: who is materially responsible (МОЛ) for what.
// ownerId: show only the items of this person (#/items/owner/12).
export async function renderItems(context, ownerId = null) {
  const { main } = context;
  replace(main, h('p', { class: 'muted' }, 'Загрузка имущества…'));
  let items;
  let people;
  try {
    [{ items }, { people }] = await Promise.all([api('/items'), api('/people')]);
  } catch (error) {
    replace(main, h('h1', {}, 'Имущество'), errorBox(error, () => renderItems(context, ownerId)));
    return;
  }
  setPeople(people);
  draw(context, items, people, { ownerId, editing: null, adding: false });
}

function draw(context, items, people, view) {
  const { me, main } = context;
  const editor = hasRole(me, 'editor');
  const redraw = (changes = {}) => draw(context, items, people, { ...view, ...changes });
  const reload = () => renderItems(context, view.ownerId);

  const tbody = h('tbody');
  const count = h('p', { class: 'muted count' });
  const fill = () => {
    const words = queryWords(state.query);
    const shown = filterItems(items, { query: state.query, type: state.type, ownerId: view.ownerId });
    count.textContent = shown.length === items.length ? `Всего: ${items.length}` : `Показано: ${shown.length} из ${items.length}`;
    replace(tbody, shown.map((item) => (view.editing === item.id ? editRow(item) : row(item, words))));
  };

  function row(item, words) {
    const person = findPerson(item.ownerId);
    return h(
      'tr',
      {},
      h('td', { class: 'mono muted' }, '#' + item.id),
      h('td', {}, marked(item.type, words)),
      h('td', {}, marked(item.model, words)),
      h('td', { class: 'mono' }, marked(item.serial, words)),
      h('td', {}, marked(item.location, words)),
      h(
        'td',
        { title: person?.note || undefined },
        item.ownerId === null ? h('span', { class: 'muted' }, '—') : h('a', { href: '#/people/' + item.ownerId }, marked(personName(item.ownerId), words)),
      ),
      h('td', { class: 'comment', title: item.note || undefined }, marked(item.note, words)),
      h('td', { class: 'muted small nowrap', title: item.updatedBy }, formatDate(item.updatedAt)),
      h('td', { class: 'row-actions' }, editor && h('button', { class: 'button small', type: 'button', onclick: () => redraw({ editing: item.id, adding: false }) }, 'Изменить')),
    );
  }

  // The form fields of an item, shared by adding and editing.
  function inputs(item) {
    const fields = Object.fromEntries(
      FIELDS.map((field) => [
        field.key,
        h('input', {
          value: item[field.key] ?? '',
          maxlength: String(field.max),
          'aria-label': field.label,
          placeholder: field.label,
          class: field.mono ? 'mono' : undefined,
          list: field.key === 'type' ? 'item-types' : undefined,
        }),
      ]),
    );
    fields.owner = personInput('owner', personInputText(item.ownerId), { 'aria-label': 'МОЛ' });
    fields.note = h('input', { value: item.note ?? '', maxlength: '1000', 'aria-label': 'Примечание', placeholder: 'Примечание' });
    return fields;
  }

  async function values(fields) {
    const owner = await resolvePeople({ ownerId: { label: 'МОЛ', text: fields.owner.value } }, people);
    if (owner === null) return null;
    const result = { ownerId: owner.ownerId, note: fields.note.value };
    for (const field of FIELDS) result[field.key] = fields[field.key].value;
    return result;
  }

  function editRow(item) {
    const fields = inputs(item);
    const save = async () => {
      try {
        const body = await values(fields);
        if (body === null) return;
        await api('/items/' + item.id, { method: 'PATCH', body });
        toast('Сохранено.');
        await reload();
      } catch (error) {
        toast(error.message, 'error');
      }
    };
    const remove = async () => {
      if (!confirm(`Удалить «${item.type} ${item.model}» (#${item.id})?`)) return;
      try {
        await api('/items/' + item.id, { method: 'DELETE' });
        toast('Удалено.');
        await reload();
      } catch (error) {
        toast(error.message, 'error');
      }
    };
    return h(
      'tr',
      { class: 'focused' },
      h('td', { class: 'mono muted' }, '#' + item.id),
      FIELDS.map((field) => h('td', {}, fields[field.key])),
      h('td', {}, fields.owner),
      h('td', {}, fields.note),
      h('td'),
      h(
        'td',
        { class: 'row-actions' },
        h('button', { class: 'button small primary', type: 'button', onclick: save }, 'Сохранить'),
        h('button', { class: 'button small', type: 'button', onclick: () => redraw({ editing: null }) }, 'Отмена'),
        hasRole(me, 'admin') && h('button', { class: 'button small danger', type: 'button', onclick: remove }, 'Удалить'),
      ),
    );
  }

  function addPanel() {
    const fields = inputs({ ownerId: view.ownerId });
    const add = h('button', { class: 'button primary', type: 'submit' }, 'Добавить');
    return h(
      'section',
      { class: 'panel' },
      h('h2', {}, 'Добавить имущество'),
      h(
        'form',
        {
          class: 'inline-form items-form',
          onsubmit: async (event) => {
            event.preventDefault();
            add.disabled = true;
            try {
              const body = await values(fields);
              if (body === null) {
                add.disabled = false;
                return;
              }
              const { item } = await api('/items', { method: 'POST', body });
              toast(`Добавлено: ${item.type}, №${item.id}.`);
              await reload();
            } catch (error) {
              add.disabled = false;
              toast(error.message, 'error');
            }
          },
        },
        FIELDS.map((field) => fields[field.key]),
        fields.owner,
        fields.note,
        add,
        h('button', { class: 'button', type: 'button', onclick: () => redraw({ adding: false }) }, 'Отмена'),
      ),
    );
  }

  const types = itemTypes(items);
  const search = h('input', {
    type: 'search',
    class: 'search',
    value: state.query,
    placeholder: 'Поиск: тип, модель, серийный номер, МОЛ, примечание',
    'aria-label': 'Поиск имущества',
    oninput: (event) => {
      state.query = event.target.value;
      fill();
    },
  });
  const typeSelect = h(
    'select',
    {
      'aria-label': 'Тип',
      onchange: (event) => {
        state.type = event.target.value;
        fill();
      },
    },
    h('option', { value: '' }, 'Все типы'),
    types.map(([type, n]) => h('option', { value: type, selected: type === state.type }, `${type} (${n})`)),
  );
  const owner = view.ownerId === null ? null : findPerson(view.ownerId);

  replace(
    main,
    h('h1', {}, 'Имущество'),
    h(
      'p',
      { class: 'lead' },
      'Техника по ведомости, кроме компьютеров: мониторы, принтеры, зарядные станции, флешки, SIM-карты и прочее. МОЛ компьютеров указан в их карточках, на вкладке «Устройства».',
    ),
    view.ownerId !== null &&
      h(
        'div',
        { class: 'notice' },
        h('p', {}, `Только имущество, за которое отвечает ${owner ? owner.name : '#' + view.ownerId}. `, h('a', { href: '#/items' }, 'Показать всё')),
      ),
    editor && view.adding && addPanel(),
    h(
      'section',
      { class: 'panel' },
      h(
        'div',
        { class: 'toolbar' },
        search,
        typeSelect,
        editor && !view.adding && h('button', { class: 'button primary', type: 'button', onclick: () => redraw({ adding: true, editing: null }) }, '+ Добавить'),
      ),
      count,
      h(
        'div',
        { class: 'table-wrap' },
        h(
          'table',
          { class: 'items' },
          h(
            'thead',
            {},
            h(
              'tr',
              {},
              h('th', {}, '№'),
              FIELDS.map((field) => h('th', {}, field.label)),
              h('th', {}, 'МОЛ'),
              h('th', {}, 'Примечание'),
              h('th', {}, 'Изменено'),
              h('th'),
            ),
          ),
          tbody,
        ),
      ),
    ),
    h('datalist', { id: 'item-types' }, types.map(([type]) => h('option', { value: type }))),
    peopleDatalist(people),
  );
  fill();
}

function marked(text, words) {
  if (isBlank(text)) return '';
  if (words.length === 0) return text;
  return splitMatches(text, words).map((part) => (part.match ? h('mark', {}, part.text) : part.text));
}
