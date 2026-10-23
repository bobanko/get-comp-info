import { api } from '../api.js';
import { downloadText, errorBox, h, readSetting, replace, toast, writeSetting } from '../dom.js';
import {
  csvFileName,
  devicesToCsv,
  displayValue,
  duplicateIps,
  ESET_LABELS,
  esetState,
  filterDevices,
  formatDate,
  hasRole,
  isBlank,
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
  duplicatesOnly: false,
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
  const tableHost = h('div', { class: 'table-wrap' });
  const redraw = () => drawTable(tableHost, count);

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

  const toolbar = h(
    'div',
    { class: 'toolbar' },
    search,
    esetSelect,
    h(
      'label',
      { class: 'check' },
      h('input', {
        type: 'checkbox',
        checked: state.duplicatesOnly,
        onchange: (event) => {
          state.duplicatesOnly = event.target.checked;
          redraw();
        },
      }),
      'Повторяющиеся IP',
    ),
    hasRole(me, 'admin') &&
      h(
        'label',
        { class: 'check' },
        h('input', {
          type: 'checkbox',
          checked: state.showDeleted,
          onchange: (event) => {
            state.showDeleted = event.target.checked;
            renderDevices({ me, main }, { reload: true });
          },
        }),
        'Удалённые',
      ),
    columnPicker(redraw),
    h('span', { class: 'spacer' }),
    h('button', { class: 'button', onclick: () => renderDevices({ me, main }, { reload: true }) }, 'Обновить'),
    h('button', { class: 'button', onclick: () => exportCsv() }, 'Экспорт CSV'),
  );

  replace(main, h('h1', {}, 'Устройства'), toolbar, count, tableHost);
  redraw();
  if (state.query) search.focus();
}

function visibleColumns() {
  return COLUMNS.filter((column) => column.always || state.columns.includes(column.key));
}

function currentRows() {
  const filtered = filterDevices(state.devices, { query: state.query, eset: state.eset, duplicatesOnly: state.duplicatesOnly });
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
  const duplicates = duplicateIps(state.devices.filter((device) => !device.deleted));
  const columns = visibleColumns();
  const now = new Date();

  replace(
    count,
    `Показано ${rows.length} из ${state.devices.length}`,
    duplicates.size > 0 && h('span', { class: 'legend legend-dup' }, `повторяющихся IP: ${duplicates.size}`),
  );

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
  switch (key) {
    case 'hostname':
      return h(
        'td',
        { class: 'host' },
        isBlank(data.hostname) ? h('span', { class: 'muted' }, device.id) : data.hostname,
        device.deleted && h('span', { class: 'badge badge-danger' }, 'удалено'),
      );
    case 'ip': {
      const ip = String(data.ip ?? '').trim();
      return h('td', { class: duplicates.has(ip) ? 'mono dup' : 'mono', title: duplicates.has(ip) ? 'Этот IP есть у нескольких устройств' : undefined }, ip);
    }
    case 'serial':
      return h('td', { class: 'mono' }, displayValue(data.serial));
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
