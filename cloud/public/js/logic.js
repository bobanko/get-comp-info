// Pure helpers for the web interface: no DOM access, covered by tests in test/web-logic.test.ts.

export const ROLE_LABELS = {
  viewer: 'Просмотр',
  harvester: 'Сборщик',
  editor: 'Редактор',
  admin: 'Администратор',
};

const ROLE_ORDER = ['viewer', 'harvester', 'editor', 'admin'];

export function hasRole(user, minimum) {
  return ROLE_ORDER.indexOf(user.role) >= ROLE_ORDER.indexOf(minimum);
}

// Same labels as the console (sync-merge.ps1, Get-SyncFieldLabel).
export const FIELD_LABELS = {
  computerId: 'Идентификатор компьютера',
  computerIdSource: 'Источник идентификатора',
  hostname: 'Имя компьютера',
  ip: 'IP-адрес',
  mac: 'MAC-адрес',
  serial: 'Серийный номер',
  smbiosUuid: 'UUID',
  manufacturer: 'Производитель',
  model: 'Модель',
  username: 'Пользователь',
  comment: 'Комментарий',
  windows: 'Windows',
  esetInstalled: 'ESET установлен',
  collectedAt: 'Дата сбора',
  createdAt: 'Дата создания',
};

export const FIELD_ORDER = Object.keys(FIELD_LABELS);

// Fields an editor may change in the web interface. Identity and collection dates come from the console.
export const EDITABLE_FIELDS = ['hostname', 'ip', 'mac', 'serial', 'manufacturer', 'model', 'username', 'windows', 'comment'];

export function fieldLabel(name) {
  return FIELD_LABELS[name] ?? name;
}

export function isBlank(value) {
  return value === null || value === undefined || (typeof value === 'string' && value.trim() === '');
}

// Text shown for a field value; objects and arrays as compact JSON.
export function displayValue(value) {
  if (value === null || value === undefined) return '';
  if (typeof value === 'object') return JSON.stringify(value);
  return String(value);
}

export function esetState(value) {
  if (value === true) return 'installed';
  if (value === false) return 'missing';
  return 'unknown';
}

export const ESET_LABELS = { installed: 'Установлен', missing: 'Не установлен', unknown: 'Не проверено' };

// IPv4 as a number so 10.0.0.9 sorts before 10.0.0.10; anything else sorts last.
export function ipSortValue(value) {
  const parts = String(value ?? '').trim().split('.');
  if (parts.length !== 4) return Number.POSITIVE_INFINITY;
  let number = 0;
  for (const part of parts) {
    if (!/^\d{1,3}$/.test(part) || Number(part) > 255) return Number.POSITIVE_INFINITY;
    number = number * 256 + Number(part);
  }
  return number;
}

export function parseDate(value) {
  if (isBlank(value)) return null;
  const time = Date.parse(String(value));
  return Number.isNaN(time) ? null : new Date(time);
}

// "2026-10-23 14:05" in the viewer's local time, like the console viewer.
export function formatDate(value) {
  const date = value instanceof Date ? value : parseDate(value);
  if (!date) return '';
  const pad = (n) => String(n).padStart(2, '0');
  return `${date.getFullYear()}-${pad(date.getMonth() + 1)}-${pad(date.getDate())} ${pad(date.getHours())}:${pad(date.getMinutes())}`;
}

export function isRecent(value, now = new Date(), days = 7) {
  const date = parseDate(value);
  return date !== null && now.getTime() - date.getTime() <= days * 24 * 60 * 60 * 1000 && date.getTime() <= now.getTime() + 60_000;
}

// Firmware placeholders, not real serial numbers (same list as get-comp-info.ps1).
const PLACEHOLDER_SERIALS = new Set(['TO BE FILLED BY O.E.M.', 'DEFAULT STRING', 'SYSTEM SERIAL NUMBER', 'NONE', 'UNKNOWN', '0']);

// Fields checked for repeats; the key is compared, empty keys are ignored.
export const DUPLICATE_FIELDS = {
  ip: { label: 'IP', key: (device) => String(device.data.ip ?? '').trim() },
  hostname: { label: 'имена', key: (device) => String(device.data.hostname ?? '').trim().toUpperCase() },
  serial: {
    label: 'серийники',
    key: (device) => {
      const serial = String(device.data.serial ?? '').trim().toUpperCase();
      return PLACEHOLDER_SERIALS.has(serial) ? '' : serial;
    },
  },
};

// Keys of the field shared by more than one device; deleted devices do not count.
export function duplicateValues(devices, field) {
  const key = DUPLICATE_FIELDS[field].key;
  const counts = new Map();
  for (const device of devices) {
    if (device.deleted) continue;
    const value = key(device);
    if (value) counts.set(value, (counts.get(value) ?? 0) + 1);
  }
  return new Set([...counts].filter(([, count]) => count > 1).map(([value]) => value));
}

export function isDuplicate(device, field, duplicates) {
  return !device.deleted && duplicates.has(DUPLICATE_FIELDS[field].key(device));
}

// Naming policy: two letters, three digits, then the rest ("AB-123-..."). The real
// prefixes and unit number are deliberately not listed here.
const HOSTNAME_POLICY = /^[A-Z]{2}-\d{3}-./i;

// Why a hostname breaks the naming policy: 'default' (name Windows made up),
// 'pattern' (does not match), or null when it is fine or empty.
export function hostnameIssue(hostname) {
  const name = String(hostname ?? '').trim();
  if (!name) return null;
  if (/^(DESKTOP|LAPTOP|WIN)-/i.test(name)) return 'default';
  return HOSTNAME_POLICY.test(name) ? null : 'pattern';
}

function searchText(device) {
  const values = [device.id, device.updatedBy];
  for (const value of Object.values(device.data)) values.push(displayValue(value));
  return values.join('\n').toLowerCase();
}

// Every word of the query must occur somewhere in the record.
// duplicatesOf: a DUPLICATE_FIELDS name to keep only devices repeating that field.
export function filterDevices(devices, { query = '', eset = 'all', duplicatesOf = null } = {}) {
  const words = query.toLowerCase().split(/\s+/).filter(Boolean);
  const duplicates = duplicatesOf ? duplicateValues(devices, duplicatesOf) : null;
  return devices.filter((device) => {
    if (eset !== 'all' && esetState(device.data.esetInstalled) !== eset) return false;
    if (duplicates && !isDuplicate(device, duplicatesOf, duplicates)) return false;
    if (words.length === 0) return true;
    const text = searchText(device);
    return words.every((word) => text.includes(word));
  });
}

const collator = new Intl.Collator('ru', { numeric: true, sensitivity: 'base' });

function compareValues(a, b) {
  const aEmpty = a === null || a === undefined || a === '' || a === Number.POSITIVE_INFINITY;
  const bEmpty = b === null || b === undefined || b === '' || b === Number.POSITIVE_INFINITY;
  if (aEmpty || bEmpty) return aEmpty === bEmpty ? 0 : aEmpty ? 1 : -1;
  if (typeof a === 'number' && typeof b === 'number') return a - b;
  return collator.compare(String(a), String(b));
}

export const SORT_KEYS = {
  hostname: (d) => d.data.hostname,
  ip: (d) => ipSortValue(d.data.ip),
  username: (d) => d.data.username,
  serial: (d) => d.data.serial,
  model: (d) => [d.data.manufacturer, d.data.model].filter((v) => !isBlank(v)).join(' '),
  windows: (d) => d.data.windows,
  eset: (d) => ['missing', 'unknown', 'installed'].indexOf(esetState(d.data.esetInstalled)),
  collectedAt: (d) => parseDate(d.data.collectedAt)?.getTime() ?? null,
  comment: (d) => d.data.comment,
  updatedAt: (d) => parseDate(d.updatedAt)?.getTime() ?? null,
};

// Empty values always go last, whatever the direction; ties are broken by host name.
export function sortDevices(devices, key, direction = 'asc') {
  const getter = SORT_KEYS[key] ?? SORT_KEYS.hostname;
  const sign = direction === 'desc' ? -1 : 1;
  return [...devices].sort((a, b) => {
    const va = getter(a);
    const vb = getter(b);
    const aEmpty = va === null || va === undefined || va === '' || va === Number.POSITIVE_INFINITY;
    const bEmpty = vb === null || vb === undefined || vb === '' || vb === Number.POSITIVE_INFINITY;
    if (aEmpty !== bEmpty) return aEmpty ? 1 : -1;
    const result = aEmpty ? 0 : sign * compareValues(va, vb);
    return result !== 0 ? result : compareValues(a.data.hostname, b.data.hostname);
  });
}

function canonical(value) {
  if (Array.isArray(value)) return `[${value.map(canonical).join(',')}]`;
  if (value !== null && typeof value === 'object') {
    return `{${Object.keys(value)
      .sort()
      .map((key) => `${JSON.stringify(key)}:${canonical(value[key])}`)
      .join(',')}}`;
  }
  return JSON.stringify(value ?? null);
}

export function sameValue(a, b) {
  return canonical(a) === canonical(b);
}

// Field-level differences between two record versions (before may be null for a new record).
export function diffRecords(before, after) {
  const names = [];
  for (const name of [...FIELD_ORDER, ...Object.keys(before ?? {}), ...Object.keys(after ?? {})]) {
    if (!names.includes(name) && ((before && name in before) || (after && name in after))) names.push(name);
  }
  const changes = [];
  for (const name of names) {
    const had = before !== null && before !== undefined && name in before;
    const has = after !== null && after !== undefined && name in after;
    if (had && has && sameValue(before[name], after[name])) continue;
    changes.push({ name, kind: !had ? 'added' : !has ? 'removed' : 'changed', before: had ? before[name] : undefined, after: has ? after[name] : undefined });
  }
  return changes;
}

// Fields to send in PATCH: only those whose text in the form differs from what the form showed.
// Comparing text keeps untouched non-string values (e.g. a legacy "mac": {}) as they are.
export function changedFields(original, edited) {
  const changes = {};
  for (const [name, value] of Object.entries(edited)) {
    if (displayValue(original[name]) !== value) changes[name] = value;
  }
  return changes;
}

// CSV in the console export format (export-comp-info.ps1): every field, ';' separated, all values quoted.
export function devicesToCsv(devices, delimiter = ';') {
  const columns = [];
  for (const device of devices) {
    for (const name of Object.keys(device.data)) if (!columns.includes(name)) columns.push(name);
  }
  const quote = (value) => `"${String(value).replace(/"/g, '""')}"`;
  const cell = (value) => {
    if (value === null || value === undefined) return quote('');
    if (typeof value === 'boolean') return quote(value ? 'True' : 'False');
    if (typeof value === 'object') return quote(JSON.stringify(value));
    return quote(value);
  };
  const rows = sortDevices(devices, 'hostname').map((device) => columns.map((name) => cell(device.data[name])).join(delimiter));
  return [columns.map(quote).join(delimiter), ...rows].join('\r\n') + '\r\n';
}

export function csvFileName(now = new Date()) {
  const pad = (n, size = 2) => String(n).padStart(size, '0');
  return `devices_${now.getFullYear()}-${pad(now.getMonth() + 1)}-${pad(now.getDate())}_${pad(now.getHours())}-${pad(now.getMinutes())}-${pad(now.getSeconds())}.csv`;
}

export const HISTORY_ACTIONS = { create: 'Создана', update: 'Изменена', delete: 'Удалена', restore: 'Восстановлена' };
export const HISTORY_SOURCES = { console: 'консоль', web: 'веб' };
