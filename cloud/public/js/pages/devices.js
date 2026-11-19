import { api } from '../api.js';
import { downloadText, errorBox, h, readSetting, replace, toast, writeSetting } from '../dom.js';
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
  formatDate,
  hasRole,
  hostnameIssue,
  INVENTORY_LABELS,
  isBlank,
  isDuplicate,
  isInventoried,
  isRecent,
  sortDevices,
} from '../logic.js';
import { queryWords, splitMatches } from '../search-logic.js';

const COLUMNS = [
  { key: 'hostname', label: 'Имя компьютера', always: true },
  { key: 'ip', label: 'IP' },
  { key: 'username', label: 'Пользователь' },
  { key: 'serial', label: 'Серийный номер' },
  { key: 'model', label: 'Модель' },
  { key: 'windows', label: 'Windows', hidden: true },
  { key: 'eset', label: 'ESET' },
  { key: 'inventoried', label: 'Инвентаризация' },
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
  const redraw = () => {
    drawTable(tableHost, count);
    updateFiltersToggle();
  };

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

  const clear = h(
    'button',
    {
      type: 'button',
      class: 'search-clear',
      'aria-label': 'Очистить поиск',
      hidden: state.query === '',
      onclick: () => {
        search.value = '';
        state.query = '';
        clear.hidden = true;
        redraw();
        search.focus();
      },
    },
    '×',
  );
  const search = h('input', {
    type: 'search',
    class: 'search',
    placeholder: 'Поиск по всем полям',
    value: state.query,
    'aria-label': 'Поиск по всем полям',
    // Phone keyboards: a "search" Enter key, no capitals or autocorrection in host names and serials.
    enterkeyhint: 'search',
    autocomplete: 'off',
    autocapitalize: 'off',
    autocorrect: 'off',
    spellcheck: 'false',
    // A plain search field, not a login, card or address: browsers (Chrome on iPhone) then offer no
    // passwords, cards or locations above the keyboard. A neutral name avoids "user"/"email"-like guesses.
    name: 'device-filter',
    inputmode: 'search',
    'aria-autocomplete': 'none',
    'data-form-type': 'other',
    oninput: (event) => {
      state.query = event.target.value;
      clear.hidden = state.query === '';
      redraw();
    },
    // The list is already filtered while typing: Enter only hides the phone keyboard to show the results.
    onkeydown: (event) => {
      if (event.key === 'Enter') {
        event.preventDefault();
        search.blur();
      }
    },
  });

  // Phones: everything but the search is behind this button; the number counts the active filters.
  const filtersToggle = h('button', {
    type: 'button',
    class: 'button filters-toggle',
    'aria-expanded': 'false',
    onclick: () => filtersToggle.setAttribute('aria-expanded', String(toolbar.classList.toggle('open'))),
  });
  function updateFiltersToggle() {
    const active = [state.eset !== 'all', state.inventory !== 'all', Boolean(state.duplicatesOf), state.showDeleted].filter(Boolean).length;
    replace(filtersToggle, 'Фильтры', active > 0 && h('span', { class: 'n' }, String(active)));
  }

  // Phones have no table header to sort by.
  const sortSelect = h(
    'select',
    {
      class: 'mobile-only',
      'aria-label': 'Сортировка',
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
          `Сортировка: ${column.label} ${direction === 'asc' ? '↑' : '↓'}`,
        ),
      ),
    ),
  );

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

  const inventorySelect = h(
    'select',
    {
      'aria-label': 'Фильтр по инвентаризации',
      onchange: (event) => {
        state.inventory = event.target.value;
        redraw();
      },
    },
    h('option', { value: 'all' }, 'Инвентаризация: все'),
    Object.entries(INVENTORY_LABELS).map(([value, label]) =>
      h('option', { value, selected: state.inventory === value }, `Инвентаризация: ${label.toLowerCase()}`),
    ),
  );

  let duplicates = duplicateFilter(redraw);
  // Phones: the search row at the bottom of the screen, within reach of the thumb (default), or at the top.
  const searchAtBottom = h('label', { class: 'check mobile-only' },
    h('input', {
      type: 'checkbox',
      checked: readSetting('devices.searchBottom', true),
      onchange: (event) => {
        writeSetting('devices.searchBottom', event.target.checked);
        toolbar.classList.toggle('at-bottom', event.target.checked);
      },
    }),
    'Поиск внизу экрана',
  );

  const toolbar = h(
    'div',
    { class: readSetting('devices.searchBottom', true) ? 'toolbar devices-toolbar at-bottom' : 'toolbar devices-toolbar' },
    // A search form (never submitted): autofill treats its field as a search box.
    h(
      'form',
      { class: 'search-box', role: 'search', autocomplete: 'off', onsubmit: (event) => event.preventDefault() },
      search,
      clear,
    ),
    filtersToggle,
    h(
      'div',
      { class: 'toolbar-extra' },
      esetSelect,
      inventorySelect,
      sortSelect,
      searchAtBottom,
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
    ),
  );

  replace(main, h('h1', { class: 'devices-title' }, 'Устройства'), toolbar, count, tableHost);
  trackKeyboard();
  redraw();
  // Not on touch screens: the keyboard would cover the list on the way back from a device card.
  if (state.query && !matchMedia('(pointer: coarse)').matches) search.focus();
}

// The search row at the bottom must stay above the on-screen keyboard. Browsers that do not shrink the page
// for the keyboard (iOS Safari) only shrink the visual viewport: --keyboard is the part it covers (app.css).
let keyboardTracked = false;
function trackKeyboard() {
  const viewport = window.visualViewport;
  if (keyboardTracked || !viewport) return;
  keyboardTracked = true;
  const update = () => {
    const covered = Math.max(0, window.innerHeight - viewport.height - viewport.offsetTop);
    document.documentElement.style.setProperty('--keyboard', `${Math.round(covered)}px`);
  };
  viewport.addEventListener('resize', update);
  viewport.addEventListener('scroll', update);
  update();
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
  const filtered = filterDevices(state.devices, {
    query: state.query,
    eset: state.eset,
    inventory: state.inventory,
    duplicatesOf: state.duplicatesOf,
  });
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
  const words = queryWords(state.query);

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
    replace(host, h('p', { class: 'empty' }, state.devices.length === 0 ? 'В облаке пока нет устройств.' : 'Ничего не найдено.'));
    return;
  }
  replace(host, h('table', { class: 'devices' }, h('thead', {}, header), h('tbody', {}, body)));
}

// Text with the parts found by the search wrapped in <mark>.
function marked(text, words) {
  if (words.length === 0) return text;
  return splitMatches(text, words).map((part) => (part.match ? h('mark', {}, part.text) : part.text));
}

function cell(key, device, duplicates, now, words = []) {
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
        isBlank(data.hostname) ? h('span', { class: 'muted' }, marked(device.id, words)) : marked(data.hostname, words),
        device.deleted && h('span', { class: 'badge badge-danger' }, 'удалено'),
      );
    }
    case 'ip': {
      const ip = String(data.ip ?? '').trim();
      return h('td', { class: repeated('ip') ? 'mono dup' : 'mono', title: repeated('ip') ? 'Этот IP есть у нескольких устройств' : undefined }, marked(ip, words));
    }
    case 'serial':
      return h('td', { class: repeated('serial') ? 'mono dup' : 'mono', title: repeated('serial') ? 'Этот серийный номер есть у нескольких устройств' : undefined }, marked(displayValue(data.serial), words));
    case 'model':
      return h('td', {}, marked([data.manufacturer, data.model].filter((v) => !isBlank(v)).join(' '), words));
    case 'eset': {
      const status = esetState(data.esetInstalled);
      return h('td', {}, h('span', { class: `badge eset-${status}` }, ESET_LABELS[status]));
    }
    case 'inventoried':
      return h(
        'td',
        { class: isInventoried(device) ? undefined : 'not-inventoried' },
        isInventoried(device)
          ? h('span', { class: 'badge badge-ok' }, INVENTORY_LABELS.yes)
          : h('span', { class: 'badge badge-muted' }, INVENTORY_LABELS.no),
      );
    case 'collectedAt':
      return h('td', { class: isRecent(data.collectedAt, now) ? 'nowrap recent' : 'nowrap', title: isRecent(data.collectedAt, now) ? 'Собрано за последние 7 дней' : undefined }, formatDate(data.collectedAt));
    case 'comment':
      return h('td', { class: 'comment', title: displayValue(data.comment) }, marked(displayValue(data.comment), words));
    case 'updatedAt':
      return h('td', { class: 'nowrap' }, formatDate(device.updatedAt), h('div', { class: 'muted small', title: authorTitle(device.updatedByName, device.updatedBy, device.updatedByKey) }, deviceAuthor(device)));
    default:
      return h('td', {}, marked(displayValue(data[key]), words));
  }
}

// Called after a device was changed in its card, so the list shows fresh data on return.
export function invalidateDevices() {
  state.devices = null;
}
