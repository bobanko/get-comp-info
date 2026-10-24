import { api } from '../api.js';
import { downloadText, errorBox, h, readSetting, replace, toast, writeSetting } from '../dom.js';
import {
  csvFileName,
  devicesToCsv,
  displayValue,
  DUPLICATE_FIELDS,
  duplicateValues,
  ESET_LABELS,
  esetState,
  filterDevices,
  formatDate,
  hasRole,
  hostnameIssue,
  isBlank,
  isDuplicate,
  isRecent,
  sortDevices,
} from '../logic.js';

const COLUMNS = [
  { key: 'hostname', label: 'Имя компьютера', always: true },
  { key: 'ip', label: 'IP' },
  { key: 'username', label: 'Пользователь' },
  { key: 'serial', label: 'Серийный номер' },
  { key: 'model', label: 'Модель' },
  { key: 'windows', label: 'Windows', hidden: true },
  { key: 'eset', label: 'ESET' },
  { key: 'collectedAt', label: 'Собрано' },
  { key: 'comment', label: 'Комментарий' },
  { key: 'updatedAt', label: 'Изменено в облаке', hidden: true },
];

// Survives navigation to a device card and back.
const state = {
  devices: null,
  showDeleted: false,
  query: '',
  eset: 'all',
  duplicatesOf: null, // 'ip' | 'hostname' | 'serial' | null
  sort: readSetting('devices.sort', { key: 'hostname', direction: 'asc' }),
  columns: readSetting(
    'devices.columns',
    COLUMNS.filter((column) => !column.hidden).map((column) => column.key),
  ),
};

export async function renderDevices({ me, main }, { reload = false } = {}) {
  if (!state.devices || reload) {
    replace(main, h('p', { class: 'muted' }, 'Загрузка устройств…'));
    try {
      const path = state.showDeleted ? '/devices?deleted=1' : '/devices';
      state.devices = (await api(path)).devices;
    } catch (error) {
      replace(main, errorBox(error, () => renderDevices({ me, main }, { reload: true })));
      return;
    }
  }

  const count = h('p', { class: 'muted count' });
  const tableHost = h('div', { class: 'table-wrap table-scroll' });
  const redraw = () => drawTable(tableHost, count);

  // Fetches the list again and redraws only the table and the repeat counters: the toolbar
  // stays in place, so it does not flicker. A newer reload wins over a slower older one.
  let loads = 0;
  const reloadList = async () => {
    const load = ++loads;
    tableHost.classList.add('loading');
    try {
      const devices = (await api(state.showDeleted ? '/devices?deleted=1' : '/devices')).devices;
      if (load !== loads) return;
      state.devices = devices;
    } catch (error) {
      if (load !== loads) return;
      tableHost.classList.remove('loading');
      replace(tableHost, errorBox(error, reloadList));
      return;
    }
    tableHost.classList.remove('loading');
    const fresh = duplicateFilter(redraw);
    duplicates.replaceWith(fresh);
    duplicates = fresh;
    redraw();
  };

  const search = h('input', {
    type: 'search',
    class: 'search',
    placeholder: 'Поиск по всем полям',
    value: state.query,
    'aria-label': 'Поиск по всем полям',
    oninput: (event) => {
      state.query = event.target.value;
      redraw();
    },
  });

  const esetSelect = h(
    'select',
    {
      'aria-label': 'Фильтр по ESET',
      onchange: (event) => {
        state.eset = event.target.value;
        redraw();
      },
    },
    h('option', { value: 'all' }, 'ESET: все'),
    Object.entries(ESET_LABELS).map(([value, label]) => h('option', { value, selected: state.eset === value }, `ESET: ${label.toLowerCase()}`)),
  );

  let duplicates = duplicateFilter(redraw);
  const toolbar = h(
    'div',
    { class: 'toolbar' },
    search,
    esetSelect,
    duplicates,
    hasRole(me, 'admin') &&
      h(
        'label',
        { class: 'check' },
        h('input', {
          type: 'checkbox',
          checked: state.showDeleted,
          onchange: (event) => {
            state.showDeleted = event.target.checked;
            reloadList();
          },
        }),
        'Удалённые',
      ),
    columnPicker(redraw),
    h('span', { class: 'spacer' }),
    h('button', { class: 'button', onclick: reloadList }, 'Обновить'),
    h('button', { class: 'button', onclick: () => exportCsv() }, 'Экспорт CSV'),
  );

  replace(main, h('h1', {}, 'Устройства'), toolbar, count, tableHost);
  redraw();
  if (state.query) search.focus();
}

function visibleColumns() {
  return COLUMNS.filter((column) => column.always || state.columns.includes(column.key));
}

// Repeated values of every checked field, for highlighting and the filter buttons.
function allDuplicates() {
  return Object.fromEntries(Object.keys(DUPLICATE_FIELDS).map((field) => [field, duplicateValues(state.devices, field)]));
}

// One block: how many values repeat per field, and a toggle to show only those devices.
function duplicateFilter(redraw) {
  const duplicates = allDuplicates();
  if (state.duplicatesOf && duplicates[state.duplicatesOf].size === 0) state.duplicatesOf = null;
  const buttons = Object.entries(DUPLICATE_FIELDS).map(([field, { label }]) => {
    const size = duplicates[field].size;
    return h(
      'button',
      {
        type: 'button',
        'aria-pressed': String(state.duplicatesOf === field),
        disabled: size === 0,
        title: size === 0 ? `Повторов нет: ${label}` : `Показать только устройства с повторами: ${label}`,
        onclick: () => {
          state.duplicatesOf = state.duplicatesOf === field ? null : field;
          buttons.forEach((button, index) => button.setAttribute('aria-pressed', String(Object.keys(DUPLICATE_FIELDS)[index] === state.duplicatesOf)));
          redraw();
        },
      },
      label,
      h('span', { class: 'n' }, String(size)),
    );
  });
  return h('div', { class: 'segmented', role: 'group', 'aria-label': 'Повторы' }, h('span', {}, 'Повторы'), buttons);
}

function currentRows() {
  const filtered = filterDevices(state.devices, { query: state.query, eset: state.eset, duplicatesOf: state.duplicatesOf });
  return sortDevices(filtered, state.sort.key, state.sort.direction);
}

function exportCsv() {
  const rows = currentRows();
  if (rows.length === 0) {
    toast('Нет устройств для экспорта.', 'error');
    return;
  }
  downloadText(csvFileName(), devicesToCsv(rows), 'text/csv;charset=utf-8');
  toast(`Экспортировано устройств: ${rows.length}`);
}

function columnPicker(redraw) {
  return h(
    'details',
    { class: 'menu' },
    h('summary', { class: 'button' }, 'Колонки'),
    h(
      'div',
      { class: 'menu-body' },
      COLUMNS.filter((column) => !column.always).map((column) =>
        h(
          'label',
          { class: 'check' },
          h('input', {
            type: 'checkbox',
            checked: state.columns.includes(column.key),
            onchange: (event) => {
              state.columns = event.target.checked
                ? [...state.columns, column.key]
                : state.columns.filter((key) => key !== column.key);
              writeSetting('devices.columns', state.columns);
              redraw();
            },
          }),
          column.label,
        ),
      ),
    ),
  );
}

function drawTable(host, count) {
  const rows = currentRows();
  const duplicates = allDuplicates();
  const columns = visibleColumns();
  const now = new Date();

  replace(count, `Показано ${rows.length} из ${state.devices.length}`);

  const header = h(
    'tr',
    {},
    columns.map((column) => {
      const active = state.sort.key === column.key;
      const arrow = active ? (state.sort.direction === 'asc' ? ' ↑' : ' ↓') : '';
      return h(
        'th',
        { 'aria-sort': active ? (state.sort.direction === 'asc' ? 'ascending' : 'descending') : undefined },
        h(
          'button',
          {
            class: active ? 'sort active' : 'sort',
            onclick: () => {
              state.sort =
                active && state.sort.direction === 'asc'
                  ? { key: column.key, direction: 'desc' }
                  : { key: column.key, direction: 'asc' };
              writeSetting('devices.sort', state.sort);
              drawTable(host, count);
            },
          },
          column.label + arrow,
        ),
      );
    }),
  );

  const body = rows.map((device) => {
    const open = () => {
      location.hash = '#/devices/' + encodeURIComponent(device.id);
    };
    return h(
      'tr',
      {
        tabindex: '0',
        class: device.deleted ? 'deleted' : undefined,
        onclick: open,
        onkeydown: (event) => {
          if (event.key === 'Enter') open();
        },
      },
      columns.map((column) => cell(column.key, device, duplicates, now)),
    );
  });

  if (rows.length === 0) {
    replace(host, h('p', { class: 'empty' }, state.devices.length === 0 ? 'В облаке пока нет устройств.' : 'Ничего не найдено.'));
    return;
  }
  replace(host, h('table', { class: 'devices' }, h('thead', {}, header), h('tbody', {}, body)));
}

function cell(key, device, duplicates, now) {
  const data = device.data;
  const repeated = (field) => isDuplicate(device, field, duplicates[field]);
  switch (key) {
    case 'hostname': {
      const issue = hostnameIssue(data.hostname);
      const notes = [
        repeated('hostname') && 'Это имя есть у нескольких устройств',
        issue === 'default' && 'Имя по умолчанию от Windows: переименуйте по правилам',
        issue === 'pattern' && 'Имя не по правилам (ожидается вид АА-000-…)',
      ].filter(Boolean);
      const classes = ['host', issue && 'off-policy', repeated('hostname') && 'dup'].filter(Boolean).join(' ');
      return h(
        'td',
        { class: classes, title: notes.join('\n') || undefined },
        isBlank(data.hostname) ? h('span', { class: 'muted' }, device.id) : data.hostname,
        device.deleted && h('span', { class: 'badge badge-danger' }, 'удалено'),
      );
    }
    case 'ip': {
      const ip = String(data.ip ?? '').trim();
      return h('td', { class: repeated('ip') ? 'mono dup' : 'mono', title: repeated('ip') ? 'Этот IP есть у нескольких устройств' : undefined }, ip);
    }
    case 'serial':
      return h('td', { class: repeated('serial') ? 'mono dup' : 'mono', title: repeated('serial') ? 'Этот серийный номер есть у нескольких устройств' : undefined }, displayValue(data.serial));
    case 'model':
      return h('td', {}, [data.manufacturer, data.model].filter((v) => !isBlank(v)).join(' '));
    case 'eset': {
      const status = esetState(data.esetInstalled);
      return h('td', {}, h('span', { class: `badge eset-${status}` }, ESET_LABELS[status]));
    }
    case 'collectedAt':
      return h('td', { class: isRecent(data.collectedAt, now) ? 'nowrap recent' : 'nowrap', title: isRecent(data.collectedAt, now) ? 'Собрано за последние 7 дней' : undefined }, formatDate(data.collectedAt));
    case 'comment':
      return h('td', { class: 'comment', title: displayValue(data.comment) }, displayValue(data.comment));
    case 'updatedAt':
      return h('td', { class: 'nowrap' }, formatDate(device.updatedAt), h('div', { class: 'muted small' }, device.updatedBy));
    default:
      return h('td', {}, displayValue(data[key]));
  }
}

// Called after a device was changed in its card, so the list shows fresh data on return.
export function invalidateDevices() {
  state.devices = null;
}
