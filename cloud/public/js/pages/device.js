import { api } from '../api.js';
import { errorBox, h, replace, toast } from '../dom.js';
import {
  ALWAYS_SHOWN_FIELDS,
  authorLabel,
  BOOLEAN_FIELDS,
  INVENTORY_LABELS,
  authorTitle,
  changedFields,
  deviceAuthor,
  diffRecords,
  displayValue,
  EDITABLE_FIELDS,
  ESET_LABELS,
  esetState,
  FIELD_ORDER,
  fieldChanges,
  fieldLabel,
  fieldValueText,
  findPerson,
  formatDate,
  hasRole,
  HISTORY_ACTIONS,
  HISTORY_SOURCES,
  isBlank,
  isRecent,
  PERSON_FIELDS,
  setPeople,
  shortAge,
} from '../logic.js';
import { peopleDatalist, personInput, personInputText, resolvePeople } from '../person-input.js';
import { invalidateDevices } from './devices.js';

// The people list of the last loaded card, offered in the person fields of the edit form.
let people = [];

export async function renderDevice(context, id) {
  const { main } = context;
  replace(main, h('p', { class: 'muted' }, 'Загрузка…'));
  let device;
  let history;
  try {
    const path = '/devices/' + encodeURIComponent(id);
    [device, { history }, { people }] = await Promise.all([api(path), api(path + '/history'), api('/people')]);
  } catch (error) {
    replace(main, backLink(), errorBox(error, () => renderDevice(context, id)));
    return;
  }
  setPeople(people);
  draw(context, device, history, { editing: false, selectedRevision: history[0]?.revision });
}

function backLink() {
  return h('a', { class: 'back', href: '#/devices' }, '← Все устройства');
}

function draw(context, device, history, view) {
  const { me, main } = context;
  const title = isBlank(device.data.hostname) ? device.id : device.data.hostname;
  const redraw = (changes) => draw(context, device, history, { ...view, ...changes });

  const actions = h(
    'div',
    { class: 'actions' },
    !device.deleted && hasRole(me, 'editor') && !view.editing &&
      h('button', { class: 'button primary', onclick: () => redraw({ editing: true, conflict: null }) }, 'Изменить'),
    !device.deleted && hasRole(me, 'admin') && !view.editing &&
      h('button', { class: 'button danger', onclick: () => setDeleted(context, device, true) }, 'Удалить'),
    device.deleted && hasRole(me, 'admin') &&
      h('button', { class: 'button primary', onclick: () => setDeleted(context, device, false) }, 'Восстановить'),
  );

  replace(
    main,
    backLink(),
    h(
      'div',
      { class: 'card-head' },
      h('div', {}, h('h1', {}, title), h('p', { class: 'muted mono small' }, device.id)),
      actions,
    ),
    device.deleted && h('div', { class: 'notice notice-error' }, `Запись удалена в облаке: ${deviceAuthor(device)}, ${formatDate(device.updatedAt)}.`),
    h(
      'div',
      { class: 'card-grid' },
      h('section', { class: 'panel' }, view.editing ? editForm(context, device, history, view, redraw) : dataList(device, history, view, redraw)),
      h('section', { class: 'panel' }, historyPanel(history, view, redraw)),
    ),
  );
}

function valueNode(name, value) {
  if (name === 'esetInstalled') {
    const status = esetState(value);
    return h('span', { class: `badge eset-${status}` }, ESET_LABELS[status]);
  }
  if (name === 'inventoried') {
    return value === true
      ? h('span', { class: 'badge badge-ok' }, INVENTORY_LABELS.yes)
      : h('span', { class: 'badge badge-muted' }, INVENTORY_LABELS.no);
  }
  if (PERSON_FIELDS.includes(name)) {
    if (isBlank(value)) return h('span', { class: 'muted' }, 'не указан');
    const person = findPerson(value);
    return h(
      'a',
      { href: '#/people/' + value, title: person?.note || undefined },
      person ? person.name : `#${value}`,
      h('span', { class: 'muted small' }, person ? ` · #${value}` : ' (нет в списке людей)'),
    );
  }
  if (/At$/.test(name) && !isBlank(value)) return h('span', { title: displayValue(value) }, formatDate(value));
  if (isBlank(value)) return h('span', { class: 'muted' }, value === undefined ? '—' : 'не указано');
  return h('span', { class: ['ip', 'mac', 'serial', 'smbiosUuid', 'computerId'].includes(name) ? 'mono' : undefined }, displayValue(value));
}

function orderedFields(data) {
  return [
    ...FIELD_ORDER.filter((name) => name in data || ALWAYS_SHOWN_FIELDS.includes(name)),
    ...Object.keys(data).filter((name) => !FIELD_ORDER.includes(name)),
  ];
}

// When the field got its current value; a click shows that revision in the history.
function ageNode(entry, view, redraw) {
  if (!entry) return null;
  const author = authorLabel(entry.changedByName, entry.changedBy, entry.changedByKey);
  return h(
    'button',
    {
      type: 'button',
      class: [
        'age',
        isRecent(entry.changedAt) ? 'recent' : '',
        entry.revision === view.selectedRevision ? 'active' : '',
      ].join(' ').trim(),
      title: `Изменено ${formatDate(entry.changedAt)} · ${author} · ревизия #${entry.revision}`,
      onclick: () => redraw({ selectedRevision: entry.revision }),
    },
    shortAge(entry.changedAt),
  );
}

function dataList(device, history, view, redraw) {
  const changes = fieldChanges(history);
  return [
    h('h2', {}, 'Данные'),
    h(
      'dl',
      { class: 'fields' },
      orderedFields(device.data).flatMap((name) => [
        h('dt', {}, fieldLabel(name)),
        h('dd', { class: 'with-age' }, h('span', { class: 'value' }, valueNode(name, device.data[name])), ageNode(changes[name], view, redraw)),
      ]),
      h('dt', {}, 'Ревизия в облаке'),
      h('dd', {}, String(device.revision)),
      h('dt', {}, 'Изменено в облаке'),
      h('dd', { title: authorTitle(device.updatedByName, device.updatedBy, device.updatedByKey) }, `${formatDate(device.updatedAt)}, ${deviceAuthor(device)}`),
    ),
  ];
}

function editForm(context, device, history, view, redraw) {
  const inputs = {};
  const form = h(
    'form',
    {
      class: 'edit',
      onsubmit: async (event) => {
        event.preventDefault();
        const edited = Object.fromEntries(
          Object.entries(inputs).map(([name, input]) => [name, input.type === 'checkbox' ? input.checked : input.value]),
        );
        // Person fields: a name from the list, or a new name added to the list on save.
        const draft = { ...edited };
        save.disabled = true;
        try {
          const fields = Object.fromEntries(PERSON_FIELDS.map((name) => [name, { label: fieldLabel(name), text: edited[name] }]));
          const ids = await resolvePeople(fields, people);
          if (ids === null) {
            save.disabled = false;
            return;
          }
          Object.assign(edited, ids);
        } catch (error) {
          save.disabled = false;
          toast(error.message, 'error');
          return;
        }
        const changes = changedFields(device.data, edited);
        if (Object.keys(changes).length === 0) {
          redraw({ editing: false });
          return;
        }
        try {
          const result = await api('/devices/' + encodeURIComponent(device.id), {
            method: 'PATCH',
            body: { baseRevision: device.revision, changes },
          });
          invalidateDevices();
          toast(result.changed ? 'Изменения сохранены.' : 'Изменений нет.');
          await renderDevice(context, device.id);
        } catch (error) {
          save.disabled = false;
          if (error.code === 'conflict') {
            redraw({ editing: true, conflict: error.body.current, draft });
          } else {
            toast(error.message, 'error');
          }
        }
      },
    },
    h('h2', {}, 'Правка'),
    view.conflict && conflictNotice(device, view.conflict, () => {
      invalidateDevices();
      renderDevice(context, device.id);
    }),
    EDITABLE_FIELDS.map((name) => {
      if (BOOLEAN_FIELDS.includes(name)) {
        const input = h('input', { type: 'checkbox', name, checked: view.draft?.[name] ?? device.data[name] === true });
        inputs[name] = input;
        return h('label', { class: 'check' }, input, name === 'inventoried' ? 'Инвентаризировано' : fieldLabel(name));
      }
      if (PERSON_FIELDS.includes(name)) {
        const input = personInput(name, view.draft?.[name] ?? personInputText(device.data[name]));
        inputs[name] = input;
        return h('label', { class: 'field' }, h('span', {}, fieldLabel(name)), input);
      }
      const initial = view.draft?.[name] ?? displayValue(device.data[name]);
      const input =
        name === 'comment'
          ? h('textarea', { name, rows: '4', value: initial })
          : h('input', { name, value: initial, class: ['ip', 'mac', 'serial'].includes(name) ? 'mono' : undefined });
      inputs[name] = input;
      return h('label', { class: 'field' }, h('span', {}, fieldLabel(name)), input);
    }),
  );
  const save = h('button', { class: 'button primary', type: 'submit' }, 'Сохранить');
  form.append(
    peopleDatalist(people),
    h(
      'div',
      { class: 'actions' },
      save,
      h('button', { class: 'button', type: 'button', onclick: () => redraw({ editing: false, conflict: null, draft: null }) }, 'Отмена'),
    ),
  );
  return form;
}

function conflictNotice(device, current, onReload) {
  const changes = diffRecords(device.data, current.data);
  return h(
    'div',
    { class: 'notice notice-error' },
    h('p', {}, `Пока вы редактировали, запись изменил(а) ${deviceAuthor(current)} (${formatDate(current.updatedAt)}). Ваши правки не сохранены.`),
    changes.length > 0 && h('ul', {}, changes.map((change) => h('li', {}, `${fieldLabel(change.name)}: ${fieldValueText(change.name, change.after) || '—'}`))),
    h('button', { class: 'button', type: 'button', onclick: onReload }, 'Открыть актуальную версию'),
  );
}

function historyPanel(history, view, redraw) {
  const selectedIndex = Math.max(0, history.findIndex((entry) => entry.revision === view.selectedRevision));
  const selected = history[selectedIndex];
  const previous = history[selectedIndex + 1] ?? null;
  return [
    h('h2', {}, 'История'),
    h(
      'ol',
      { class: 'history' },
      history.map((entry) =>
        h(
          'li',
          {},
          h(
            'button',
            {
              class: entry.revision === selected?.revision ? 'history-item active' : 'history-item',
              onclick: () => redraw({ selectedRevision: entry.revision }),
            },
            h('span', { class: 'rev' }, `#${entry.revision}`),
            h('span', {}, HISTORY_ACTIONS[entry.action] ?? entry.action),
            h(
              'span',
              { class: 'muted small', title: authorTitle(entry.changedByName, entry.changedBy, entry.changedByKey) },
              `${formatDate(entry.changedAt)} · `,
              h('b', { class: 'author' }, authorLabel(entry.changedByName, entry.changedBy, entry.changedByKey)),
              // A key already means the console.
              entry.changedByKey ? '' : ` · ${HISTORY_SOURCES[entry.source] ?? entry.source}`,
            ),
          ),
        ),
      ),
    ),
    selected && diffTable(selected, previous),
  ];
}

function diffTable(selected, previous) {
  if (selected.action === 'delete' || selected.action === 'restore') {
    return h('p', { class: 'muted' }, 'Данные в этой ревизии не менялись.');
  }
  const changes = diffRecords(previous?.data ?? null, selected.data);
  if (changes.length === 0) return h('p', { class: 'muted' }, 'Данные в этой ревизии не менялись.');
  return h(
    'table',
    { class: 'diff' },
    h('thead', {}, h('tr', {}, h('th', {}, 'Поле'), h('th', {}, previous ? `Было (#${previous.revision})` : 'Было'), h('th', {}, `Стало (#${selected.revision})`))),
    h(
      'tbody',
      {},
      changes.map((change) =>
        h(
          'tr',
          {},
          h('td', {}, fieldLabel(change.name)),
          h('td', { class: 'before' }, change.kind === 'added' ? h('span', { class: 'muted' }, '—') : fieldValueText(change.name, change.before)),
          h('td', { class: 'after' }, change.kind === 'removed' ? h('span', { class: 'muted' }, 'поле удалено') : fieldValueText(change.name, change.after)),
        ),
      ),
    ),
  );
}

async function setDeleted(context, device, deleted) {
  const name = isBlank(device.data.hostname) ? device.id : device.data.hostname;
  if (deleted && !confirm(`Удалить запись «${name}»? Её можно будет восстановить, история сохранится.`)) return;
  try {
    const path = '/devices/' + encodeURIComponent(device.id);
    await api(deleted ? path : path + '/restore', { method: deleted ? 'DELETE' : 'POST' });
    invalidateDevices();
    toast(deleted ? 'Запись удалена.' : 'Запись восстановлена.');
    if (deleted) location.hash = '#/devices';
    else await renderDevice(context, device.id);
  } catch (error) {
    toast(error.message, 'error');
  }
}
