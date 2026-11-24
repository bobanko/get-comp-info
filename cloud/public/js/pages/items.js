import { api } from '../api.js';
import { errorBox, h, replace, toast } from '../dom.js';
import { f, t } from '../i18n.js';
import { focusSearch, listToolbar, marked } from '../list-toolbar.js';
import { filterItems, findPerson, formatDate, hasRole, isBlank, itemTypes, personName, setPeople } from '../logic.js';
import { peopleDatalist, personInput, personInputText, resolvePeople } from '../person-input.js';
import { queryWords } from '../search-logic.js';

// Survives navigation to a person and back.
const state = { query: '', type: '' };

const FIELDS = [
  { key: 'type', label: t('Тип'), max: 100 },
  { key: 'model', label: t('Модель'), max: 200 },
  { key: 'serial', label: t('Серийный номер'), max: 200, mono: true },
  { key: 'location', label: t('Место / секция'), max: 200 },
];

// Property from the property register, «Майно» in every language: who is materially responsible (МВО) for what.
// Items have numbers in the cloud (the journal, the API), but the page does not show them: the register is
// read by type, model and serial number, and the numbers meant nothing to people.
// ownerId: show only the items of this person (#/items/owner/12).
export async function renderItems(context, ownerId = null) {
  const { main } = context;
  replace(main, h('p', { class: 'muted' }, t('Загрузка майна…')));
  let items;
  let people;
  let devices;
  try {
    [{ items }, { people }, { devices }] = await Promise.all([api('/items'), api('/people'), api('/devices')]);
  } catch (error) {
    replace(main, h('h1', {}, t('Майно')), errorBox(error, () => renderItems(context, ownerId)));
    return;
  }
  setPeople(people);
  // Computers are property too: their rows come from the device records, where their МВО is kept.
  const rows = [...items.map((item) => ({ ...item, kind: 'item' })), ...devices.filter((device) => !device.deleted).map(computerRow)];
  draw(context, rows, people, { ownerId, editing: null, adding: false });
}

const COMPUTER_TYPE = 'Комп’ютер';

function computerRow(device) {
  const d = device.data;
  return {
    kind: 'device',
    id: device.id,
    hostname: isBlank(d.hostname) ? device.id : d.hostname,
    type: COMPUTER_TYPE,
    model: [d.manufacturer, d.model].filter((v) => !isBlank(v)).join(' '),
    serial: d.serial ?? '',
    location: '',
    ownerId: Number.isInteger(d.ownerId) ? d.ownerId : null,
    note: d.comment ?? '',
    updatedAt: device.updatedAt,
    updatedBy: device.updatedBy,
  };
}

function draw(context, items, people, view) {
  const { me, main } = context;
  const editor = hasRole(me, 'editor');
  const redraw = (changes = {}) => draw(context, items, people, { ...view, ...changes });
  const reload = () => renderItems(context, view.ownerId);
  const cancelEdit = () => redraw({ editing: null });

  const tbody = h('tbody');
  const count = h('p', { class: 'muted count' });
  const fill = () => {
    const words = queryWords(state.query);
    const shown = filterItems(items, { query: state.query, type: state.type, ownerId: view.ownerId });
    const computers = shown.filter((item) => item.kind === 'device').length;
    count.textContent =
      (shown.length === items.length ? f('Всего: {0}', items.length) : f('Показано: {0} из {1}', shown.length, items.length)) +
      (computers > 0 ? f(', из них компьютеров: {0}', computers) : '');
    replace(tbody, shown.map((item) => (item.kind === 'item' && view.editing === item.id ? editRow(item) : row(item, words))));
    if (shown.length === 0) replace(tbody, h('tr', { class: 'empty-row' }, h('td', { colspan: '8', class: 'empty' }, t('Ничего не найдено.'))));
    updateFilters();
  };

  // The type works as a tag: a click shows only that type (the same as the type list), a second click shows all.
  const pickType = (type) => {
    state.type = state.type === type ? '' : type;
    typeSelect.value = state.type;
    fill();
  };

  function typeCell(item, words) {
    const computer = item.kind === 'device';
    return h(
      'td',
      { class: 'type', 'data-col': 'type' },
      h(
        'button',
        {
          type: 'button',
          class: state.type === item.type ? 'tag active' : 'tag',
          title: state.type === item.type ? t('Показать все типы') : f('Показать только: {0}', item.type),
          onclick: () => pickType(item.type),
        },
        marked(item.type, words),
      ),
      computer && h('a', { class: 'host', href: '#/devices/' + encodeURIComponent(item.id) }, marked(item.hostname, words)),
    );
  }

  function row(item, words) {
    const person = findPerson(item.ownerId);
    const computer = item.kind === 'device';
    return h(
      'tr',
      { class: computer ? 'computer' : undefined },
      typeCell(item, words),
      h('td', { 'data-col': 'model' }, marked(item.model, words)),
      h('td', { class: 'mono', 'data-col': 'serial' }, marked(item.serial, words)),
      h('td', { 'data-col': 'location' }, marked(item.location, words)),
      h(
        'td',
        { 'data-col': 'owner', title: person?.note || undefined },
        item.ownerId === null ? h('span', { class: 'muted' }, '—') : h('a', { href: '#/people/' + item.ownerId }, marked(personName(item.ownerId), words)),
      ),
      h('td', { class: 'comment', 'data-col': 'note', title: item.note || undefined }, marked(item.note, words)),
      h('td', { class: 'muted small nowrap', 'data-col': 'updated', title: item.updatedBy }, formatDate(item.updatedAt)),
      h(
        'td',
        { class: 'row-actions' },
        computer
          ? h('a', { class: 'button small', href: '#/devices/' + encodeURIComponent(item.id), title: t('МВО компьютера меняется в его карточке') }, t('Карточка'))
          : editor && h('button', { class: 'button small', type: 'button', onclick: () => redraw({ editing: item.id, adding: false }) }, t('Изменить')),
      ),
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
    fields.owner = personInput('owner', personInputText(item.ownerId), { 'aria-label': 'МВО' });
    fields.note = h('input', { value: item.note ?? '', maxlength: '1000', 'aria-label': t('Примечание'), placeholder: t('Примечание') });
    return fields;
  }

  async function values(fields) {
    const owner = await resolvePeople({ ownerId: { label: 'МВО', text: fields.owner.value } }, people);
    if (owner === null) return null;
    const result = { ownerId: owner.ownerId, note: fields.note.value };
    for (const field of FIELDS) result[field.key] = fields[field.key].value;
    return result;
  }

  // Escape cancels the editing, as the «Отмена» button.
  const escapeTo = (cancel) => (event) => {
    if (event.key !== 'Escape') return;
    event.preventDefault();
    cancel();
  };

  function editRow(item) {
    const fields = inputs(item);
    const save = async () => {
      try {
        const body = await values(fields);
        if (body === null) return;
        await api('/items/' + item.id, { method: 'PATCH', body });
        toast(t('Сохранено.'));
        await reload();
      } catch (error) {
        toast(error.message, 'error');
      }
    };
    const remove = async () => {
      if (!confirm(f('Удалить «{0}»?', [item.type, item.model].filter((v) => !isBlank(v)).join(' ')))) return;
      try {
        await api('/items/' + item.id, { method: 'DELETE' });
        toast(t('Удалено.'));
        await reload();
      } catch (error) {
        toast(error.message, 'error');
      }
    };
    return h(
      'tr',
      { class: 'focused', onkeydown: escapeTo(cancelEdit) },
      FIELDS.map((field) => h('td', { 'data-col': field.key }, fields[field.key])),
      h('td', { 'data-col': 'owner' }, fields.owner),
      h('td', { 'data-col': 'note' }, fields.note),
      h('td', { 'data-col': 'updated' }),
      h(
        'td',
        { class: 'row-actions' },
        h('button', { class: 'button small primary', type: 'button', onclick: save }, t('Сохранить')),
        h('button', { class: 'button small', type: 'button', title: t('Отмена (Esc)'), onclick: cancelEdit }, t('Отмена')),
        hasRole(me, 'admin') && h('button', { class: 'button small danger', type: 'button', onclick: remove }, t('Удалить')),
      ),
    );
  }

  function addPanel() {
    const fields = inputs({ ownerId: view.ownerId, type: state.type });
    const add = h('button', { class: 'button primary', type: 'submit' }, t('Добавить'));
    const cancel = () => redraw({ adding: false });
    return h(
      'section',
      { class: 'panel add-panel' },
      h('h2', {}, t('Добавить майно')),
      h(
        'form',
        {
          class: 'inline-form items-form',
          onkeydown: escapeTo(cancel),
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
              toast(f('Добавлено: {0}.', [item.type, item.model].filter((v) => !isBlank(v)).join(' ')));
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
        h('button', { class: 'button', type: 'button', title: t('Отмена (Esc)'), onclick: cancel }, t('Отмена')),
      ),
    );
  }

  const types = itemTypes(items);
  const typeSelect = h(
    'select',
    {
      'aria-label': t('Тип'),
      onchange: (event) => {
        state.type = event.target.value;
        fill();
      },
    },
    h('option', { value: '' }, t('Все типы')),
    types.map(([type, n]) => h('option', { value: type, selected: type === state.type }, `${type} (${n})`)),
  );
  const { toolbar, search, updateFilters } = listToolbar({
    name: 'items-filter',
    placeholder: t('Поиск: тип, модель, серийный номер, МВО, примечание'),
    query: state.query,
    onQuery: (query) => {
      state.query = query;
      fill();
    },
    extra: [typeSelect],
    activeFilters: () => (state.type ? 1 : 0),
    actions: [
      editor &&
        !view.adding &&
        h('button', { class: 'button primary', type: 'button', title: t('Добавить майно'), onclick: () => redraw({ adding: true, editing: null }) }, t('+ Добавить')),
    ],
  });
  const owner = view.ownerId === null ? null : findPerson(view.ownerId);

  const panel = editor && view.adding && addPanel();
  replace(
    main,
    h('h1', { class: 'list-title' }, t('Майно')),
    h(
      'p',
      { class: 'lead list-lead' },
      t('Вся техника по ведомости: компьютеры, мониторы, принтеры, зарядные станции, флешки, SIM-карты и прочее. Компьютеры — записи вкладки «Устройства»: имя под типом ведёт в их карточку, там же меняется их МВО. Нажмите на тип, чтобы оставить только его.'),
    ),
    view.ownerId !== null &&
      h(
        'div',
        { class: 'notice' },
        h('p', {}, f('Только майно, за которое отвечает {0}. ', owner ? owner.name : '#' + view.ownerId), h('a', { href: '#/items' }, t('Показать всё'))),
      ),
    panel,
    toolbar,
    count,
    h(
      'div',
      { class: 'table-wrap table-scroll' },
      h(
        'table',
        { class: 'items' },
        h(
          'thead',
          {},
          h('tr', {}, FIELDS.map((field) => h('th', {}, field.label)), h('th', {}, 'МВО'), h('th', {}, t('Примечание')), h('th', {}, t('Изменено')), h('th')),
        ),
        tbody,
      ),
    ),
    h('datalist', { id: 'item-types' }, types.map(([type]) => h('option', { value: type }))),
    peopleDatalist(people),
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
