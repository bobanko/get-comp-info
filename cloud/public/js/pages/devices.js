import { api } from '../api.js';
import { downloadText, errorBox, h, readSetting, replace, toast, writeSetting } from '../dom.js';
import { f, t } from '../i18n.js';
import { focusSearch, listToolbar, marked } from '../list-toolbar.js';
import {
  authorTitle,
  csvFileName,
  deviceAuthor,
  devicesToCsv,
  displayValue,
  DUPLICATE_FIELDS,
  duplicateValues,
  ESET_LABELS,
  esetState,
  filterDevices,
  findPerson,
  formatDate,
  hasRole,
  hostnameIssue,
  INVENTORY_LABELS,
  isBlank,
  isDuplicate,
  isInventoried,
  isRecent,
  personName,
  setPeople,
  sortDevices,
} from '../logic.js';
import { queryWords } from '../search-logic.js';

const COLUMNS = [
  { key: 'hostname', label: t('Имя компьютера'), always: true },
  { key: 'ip', label: 'IP' },
  { key: 'userId', label: t('Пользователь') },
  { key: 'ownerId', label: 'МВО', hidden: true },
  { key: 'username', label: t('Учётная запись Windows') },
  { key: 'serial', label: t('Серийный номер') },
  { key: 'model', label: t('Модель') },
  { key: 'windows', label: 'Windows', hidden: true },
  { key: 'eset', label: 'ESET' },
  { key: 'inventoried', label: t('Инвентаризация') },
  { key: 'collectedAt', label: t('Собрано') },
  { key: 'comment', label: t('Комментарий') },
  { key: 'updatedAt', label: t('Изменено в облаке'), hidden: true },
];

// Survives navigation to a device card and back.
const state = {
  devices: null,
  showDeleted: false,
  query: '',
  eset: 'all',
  inventory: 'all', // 'all' | 'yes' | 'no'
  duplicatesOf: null, // 'ip' | 'hostname' | 'serial' | null
  sort: readSetting('devices.sort', { key: 'hostname', direction: 'asc' }),
  columns: savedColumns(),
};

// The saved column choice; columns added to the site after it was saved are shown once (unless hidden by default).
function savedColumns() {
  const defaults = COLUMNS.filter((column) => !column.hidden).map((column) => column.key);
  const columns = readSetting('devices.columns', defaults);
  // Columns that existed before the list of known columns was saved.
  const known = readSetting('devices.knownColumns', ['hostname', 'ip', 'username', 'serial', 'model', 'windows', 'eset', 'collectedAt', 'comment', 'updatedAt']);
  const added = defaults.filter((key) => !known.includes(key) && !columns.includes(key));
  writeSetting('devices.knownColumns', COLUMNS.map((column) => column.key));
  if (added.length === 0) return columns;
  const result = [...columns, ...added];
  writeSetting('devices.columns', result);
  return result;
}

export async function renderDevices({ me, main }, { reload = false } = {}) {
  if (!state.devices || reload) {
    replace(main, h('p', { class: 'muted' }, t('Загрузка устройств…')));
    try {
      const path = state.showDeleted ? '/devices?deleted=1' : '/devices';
      const [{ devices }, { people }] = await Promise.all([api(path), api('/people')]);
      setPeople(people);
      state.devices = devices;
    } catch (error) {
      replace(main, errorBox(error, () => renderDevices({ me, main }, { reload: true })));
      return;
    }
  }

  const count = h('p', { class: 'muted count' });
  const tableHost = h('div', { class: 'table-wrap table-scroll' });
  const redraw = () => {
    drawTable(tableHost, count);
    updateFilters();
  };

  // Fetches the list again and redraws only the table and the repeat counters: the toolbar
  // stays in place, so it does not flicker. A newer reload wins over a slower older one.
  let loads = 0;
  const reloadList = async () => {
    const load = ++loads;
    tableHost.classList.add('loading');
    try {
      const [{ devices }, { people }] = await Promise.all([api(state.showDeleted ? '/devices?deleted=1' : '/devices'), api('/people')]);
      if (load !== loads) return;
      setPeople(people);
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

  // Phones have no table header to sort by.
  const sortSelect = h(
    'select',
    {
      class: 'mobile-only',
      'aria-label': t('Сортировка'),
      onchange: (event) => {
        const [key, direction] = event.target.value.split(':');
        state.sort = { key, direction };
        writeSetting('devices.sort', state.sort);
        redraw();
      },
    },
    COLUMNS.flatMap((column) =>
      ['asc', 'desc'].map((direction) =>
        h(
          'option',
          { value: `${column.key}:${direction}`, selected: state.sort.key === column.key && state.sort.direction === direction },
          f('Сортировка: {0} {1}', column.label, direction === 'asc' ? '↑' : '↓'),
        ),
      ),
    ),
  );

  const esetSelect = h(
    'select',
    {
      'aria-label': t('Фильтр по ESET'),
      onchange: (event) => {
        state.eset = event.target.value;
        redraw();
      },
    },
    h('option', { value: 'all' }, t('ESET: все')),
    Object.entries(ESET_LABELS).map(([value, label]) => h('option', { value, selected: state.eset === value }, `ESET: ${label.toLowerCase()}`)),
  );

  const inventorySelect = h(
    'select',
    {
      'aria-label': t('Фильтр по инвентаризации'),
      onchange: (event) => {
        state.inventory = event.target.value;
        redraw();
      },
    },
    h('option', { value: 'all' }, t('Инвентаризация: все')),
    Object.entries(INVENTORY_LABELS).map(([value, label]) =>
      h('option', { value, selected: state.inventory === value }, f('Инвентаризация: {0}', label.toLowerCase())),
    ),
  );

  let duplicates = duplicateFilter(redraw);

  const { toolbar, search, updateFilters } = listToolbar({
    name: 'device-filter',
    placeholder: t('Поиск по всем полям'),
    query: state.query,
    onQuery: (query) => {
      state.query = query;
      redraw();
    },
    activeFilters: () => [state.eset !== 'all', state.inventory !== 'all', Boolean(state.duplicatesOf), state.showDeleted].filter(Boolean).length,
    extra: [
      esetSelect,
      inventorySelect,
      sortSelect,
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
          t('Удалённые'),
        ),
      columnPicker(redraw),
      h('span', { class: 'spacer' }),
      h('button', { class: 'button', onclick: reloadList }, t('Обновить')),
      h('button', { class: 'button', onclick: () => exportCsv() }, t('Экспорт CSV')),
    ],
  });

  replace(main, h('h1', { class: 'list-title' }, t('Устройства')), toolbar, count, tableHost);
  redraw();
  focusSearch(search, state.query);
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
        title: size === 0 ? f('Повторов нет: {0}', label) : f('Показать только устройства с повторами: {0}', label),
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
  return h('div', { class: 'segmented', role: 'group', 'aria-label': t('Повторы') }, h('span', {}, t('Повторы')), buttons);
}

function currentRows() {
  const filtered = filterDevices(state.devices, {
    query: state.query,
    eset: state.eset,
    inventory: state.inventory,
    duplicatesOf: state.duplicatesOf,
    authors: state.columns.includes('updatedAt'),
  });
  return sortDevices(filtered, state.sort.key, state.sort.direction);
}

function exportCsv() {
  const rows = currentRows();
  if (rows.length === 0) {
    toast(t('Нет устройств для экспорта.'), 'error');
    return;
  }
  downloadText(csvFileName(), devicesToCsv(rows), 'text/csv;charset=utf-8');
  toast(f('Экспортировано устройств: {0}', rows.length));
}

function columnPicker(redraw) {
  return h(
    'details',
    { class: 'menu' },
    h('summary', { class: 'button' }, t('Колонки')),
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
  const words = queryWords(state.query);

  replace(count, f('Показано {0} из {1}', rows.length, state.devices.length));

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
      columns.map((column) => {
        const td = cell(column.key, device, duplicates, now, words);
        // Phones lay the row out as a card by column (app.css).
        td.dataset.col = column.key;
        td.dataset.label = column.label;
        return td;
      }),
    );
  });

  if (rows.length === 0) {
    replace(host, h('p', { class: 'empty' }, state.devices.length === 0 ? t('В облаке пока нет устройств.') : t('Ничего не найдено.')));
    return;
  }
  replace(host, h('table', { class: 'devices' }, h('thead', {}, header), h('tbody', {}, body)));
}

function cell(key, device, duplicates, now, words = []) {
  const data = device.data;
  const repeated = (field) => isDuplicate(device, field, duplicates[field]);
  switch (key) {
    case 'hostname': {
      const issue = hostnameIssue(data.hostname);
      const nameless = isBlank(data.hostname);
      const notes = [
        nameless && t('Имени нет: программа этот компьютер ещё не собирала. Показан идентификатор записи (серийный номер или MAC)'),
        repeated('hostname') && t('Это имя есть у нескольких устройств'),
        issue === 'default' && t('Имя по умолчанию от Windows: переименуйте по правилам'),
        issue === 'pattern' && t('Имя не по правилам (ожидается вид АА-000-…)'),
      ].filter(Boolean);
      const classes = ['host', issue && 'off-policy', repeated('hostname') && 'dup'].filter(Boolean).join(' ');
      return h(
        'td',
        { class: classes, title: notes.join('\n') || undefined },
        nameless ? [h('span', { class: 'muted nameless' }, marked(device.id, words)), h('span', { class: 'nameless-mark' }, '*')] : marked(data.hostname, words),
        device.deleted && h('span', { class: 'badge badge-danger' }, t('удалено')),
      );
    }
    case 'ip': {
      const ip = String(data.ip ?? '').trim();
      return h('td', { class: repeated('ip') ? 'mono dup' : 'mono', title: repeated('ip') ? t('Этот IP есть у нескольких устройств') : undefined }, marked(ip, words));
    }
    case 'serial':
      return h(
        'td',
        { class: repeated('serial') ? 'mono dup' : 'mono', title: repeated('serial') ? t('Этот серийный номер есть у нескольких устройств') : undefined },
        marked(displayValue(data.serial), words),
      );
    case 'userId':
    case 'ownerId': {
      if (!Number.isInteger(data[key])) return h('td');
      const person = findPerson(data[key]);
      // The link opens the person, not the device the row opens.
      return h(
        'td',
        { title: person?.note || undefined },
        h('a', { href: '#/people/' + data[key], onclick: (event) => event.stopPropagation(), onkeydown: (event) => event.stopPropagation() }, marked(personName(data[key]), words)),
      );
    }
    case 'model':
      return h('td', {}, marked([data.manufacturer, data.model].filter((v) => !isBlank(v)).join(' '), words));
    case 'eset': {
      const status = esetState(data.esetInstalled);
      return h('td', {}, h('span', { class: `badge eset-${status}` }, ESET_LABELS[status]));
    }
    case 'inventoried':
      return h(
        'td',
        { class: isInventoried(device) ? undefined : 'not-inventoried', 'data-short': t('Инв.') },
        isInventoried(device)
          ? h('span', { class: 'badge badge-ok' }, INVENTORY_LABELS.yes)
          : h('span', { class: 'badge badge-muted' }, INVENTORY_LABELS.no),
      );
    case 'collectedAt':
      return h(
        'td',
        { class: isRecent(data.collectedAt, now) ? 'nowrap recent' : 'nowrap', title: isRecent(data.collectedAt, now) ? t('Собрано за последние 7 дней') : undefined },
        formatDate(data.collectedAt),
      );
    case 'comment':
      return h('td', { class: 'comment', title: displayValue(data.comment) }, marked(displayValue(data.comment), words));
    case 'updatedAt':
      return h(
        'td',
        { class: 'nowrap' },
        formatDate(device.updatedAt),
        h('div', { class: 'muted small', title: authorTitle(device.updatedByName, device.updatedBy, device.updatedByKey, device.updatedSource) }, marked(deviceAuthor(device), words)),
      );
    default:
      return h('td', {}, marked(displayValue(data[key]), words));
  }
}

// Called after a device was changed in its card, so the list shows fresh data on return.
export function invalidateDevices() {
  state.devices = null;
}
