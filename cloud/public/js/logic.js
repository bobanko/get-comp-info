// Pure helpers for the web interface: no DOM access, covered by tests in test/web-logic.test.ts.

import { f, LANG, t } from './i18n.js';
import { looseKey, queryWords, textMatches } from './search-logic.js';

export const ROLE_LABELS = {
  viewer: t('Просмотр'),
  harvester: t('Сборщик'),
  editor: t('Редактор'),
  admin: t('Администратор'),
};

const ROLE_ORDER = ['viewer', 'harvester', 'editor', 'admin'];

export function hasRole(user, minimum) {
  return ROLE_ORDER.indexOf(user.role) >= ROLE_ORDER.indexOf(minimum);
}

// Same labels as the console (sync-merge.ps1, Get-SyncFieldLabel).
export const FIELD_LABELS = {
  computerId: t('Идентификатор компьютера'),
  computerIdSource: t('Источник идентификатора'),
  hostname: t('Имя компьютера'),
  ip: t('IP-адрес'),
  mac: t('MAC-адрес'),
  serial: t('Серийный номер'),
  smbiosUuid: 'UUID',
  windowsMachineGuid: t('ID установки Windows'),
  partOfDomain: t('Член домена'),
  domain: t('Домен / рабочая группа'),
  manufacturer: t('Производитель'),
  model: t('Модель'),
  userId: t('Пользователь (по факту)'),
  ownerId: t('МВО (по ведомости)'),
  username: t('Учётная запись Windows'),
  comment: t('Комментарий'),
  windows: 'Windows',
  esetInstalled: t('ESET установлен'),
  inventoried: t('Инвентаризация'),
  collectedAt: t('Дата сбора'),
  createdAt: t('Дата создания'),
};

export const FIELD_ORDER = Object.keys(FIELD_LABELS);

// Fields an editor may change in the web interface. Identity and collection dates come from the console.
export const EDITABLE_FIELDS = ['hostname', 'ip', 'mac', 'serial', 'manufacturer', 'model', 'userId', 'ownerId', 'username', 'windows', 'comment', 'inventoried'];

// Yes/no fields: a checkbox in the edit form; a missing value means "no".
export const BOOLEAN_FIELDS = ['inventoried'];

// Shown in the card even when the record does not have them yet.
export const ALWAYS_SHOWN_FIELDS = ['userId', 'ownerId', 'inventoried'];

// ---------- People ----------
// Device fields that hold a person number (src/people.ts); the names come from GET /people.
export const PERSON_FIELDS = ['userId', 'ownerId'];

let peopleById = new Map();

export function setPeople(list) {
  peopleById = new Map((list ?? []).map((person) => [person.id, person]));
  searchCache = new WeakMap();
}

export function findPerson(id) {
  return peopleById.get(id) ?? null;
}

// The name of a linked person, '#12' when the list does not have it, '' when nobody is linked.
export function personName(id) {
  if (id === null || id === undefined || id === '') return '';
  return peopleById.get(id)?.name ?? `#${id}`;
}

// What the person field of the edit form shows and the datalist offers: «Іваненко Іван · #12».
export function personOption(person) {
  return `${person.name} · #${person.id}`;
}

// The edit form text back to a person: { id } (null = nobody), { newName } to add, or { error }.
export function parsePerson(text, people) {
  const value = String(text ?? '').trim().replace(/\s+/g, ' ');
  if (value === '') return { id: null };
  const live = (people ?? []).filter((person) => !person.deleted);
  const number = /#(\d+)$/.exec(value);
  if (number) {
    const id = Number(number[1]);
    return live.some((person) => person.id === id) ? { id } : { error: f('Человека #{0} нет в списке.', id) };
  }
  const same = live.filter((person) => person.name.toLowerCase() === value.toLowerCase());
  if (same.length === 1) return { id: same[0].id };
  if (same.length > 1) return { error: f('Несколько людей «{0}»: выберите нужного из списка, с номером.', value) };
  return { newName: value };
}

// Person number -> { userId: [devices], ownerId: [devices] } of the live devices that link the person.
export function personLinks(devices) {
  const links = new Map();
  for (const device of devices ?? []) {
    if (device.deleted) continue;
    for (const field of PERSON_FIELDS) {
      const id = device.data[field];
      if (!Number.isInteger(id)) continue;
      if (!links.has(id)) links.set(id, { userId: [], ownerId: [] });
      links.get(id)[field].push(device);
    }
  }
  return links;
}

// ---------- Property ----------

// The types of the items with their counts, the most common first: [[type, count], …].
export function itemTypes(items) {
  const counts = new Map();
  for (const item of items ?? []) counts.set(item.type, (counts.get(item.type) ?? 0) + 1);
  return [...counts].sort((a, b) => b[1] - a[1] || collator.compare(a[0], b[0]));
}

// Models typed before, for the model field of the property form: the models of this type when it has any
// (so a monitor suggests monitors), otherwise every model. The most used first; the same model in another
// case or with other spaces counts once.
export function itemModels(items, type = '') {
  const all = (items ?? []).filter((item) => !isBlank(item.model));
  const sameType = (item) => !isBlank(type) && String(item.type ?? '').trim().toLowerCase() === type.trim().toLowerCase();
  const ofType = all.filter(sameType);
  const counts = new Map();
  for (const item of ofType.length > 0 ? ofType : all) {
    const model = item.model.trim().replace(/\s+/g, ' ');
    const key = model.toLowerCase();
    const seen = counts.get(key);
    if (seen) seen[1] += 1;
    else counts.set(key, [model, 1]);
  }
  return [...counts.values()].sort((a, b) => b[1] - a[1] || collator.compare(a[0], b[0])).map(([model]) => model);
}

// Items of one type and person whose text has every word of the query, as typed or in another alphabet.
/**
 * @param {any[]} items
 * @param {{ query?: string, type?: string, ownerId?: number | null }} [options]
 */
export function filterItems(items, options = {}) {
  const { query = '', type = '', ownerId = null } = options;
  const words = queryWords(query);
  return (items ?? []).filter((item) => {
    if (type && item.type !== type) return false;
    if (ownerId !== null && item.ownerId !== ownerId) return false;
    if (words.length === 0) return true;
    const text = [item.hostname, item.type, item.model, item.serial, item.location, item.note, personName(item.ownerId)]
      .filter((value) => !isBlank(value))
      .join('\n')
      .toLowerCase();
    const key = looseKey(text).key;
    return words.every((word) => textMatches(text, word, key));
  });
}

// A field value as people read it: names for person fields.
export function fieldValueText(name, value) {
  return PERSON_FIELDS.includes(name) ? personName(value) : displayValue(value);
}

// Inventory mark, set by people in the web interface (the console keeps it, never sets it).
export function isInventoried(device) {
  return device.data.inventoried === true;
}

export const INVENTORY_LABELS = { yes: t('Да'), no: t('Нет') };

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

export const ESET_LABELS = { installed: t('Установлен'), missing: t('Не установлен'), unknown: t('Не проверено') };

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

// Compact age for the field list: "5 мин", "3 ч", "12 д", "2 мес", "1 г".
export function shortAge(value, now = new Date()) {
  const date = parseDate(value);
  if (!date) return '';
  const minutes = Math.max(0, Math.floor((now.getTime() - date.getTime()) / 60_000));
  if (minutes < 60) return f('{0} мин', Math.max(1, minutes));
  const hours = Math.floor(minutes / 60);
  if (hours < 24) return f('{0} ч', hours);
  const days = Math.floor(hours / 24);
  if (days < 30) return f('{0} д', days);
  if (days < 365) return f('{0} мес', Math.floor(days / 30));
  return f('{0} г', Math.floor(days / 365));
}

// For every field: the history entry (newest first, as the API returns it) that last set its value.
export function fieldChanges(history) {
  const result = {};
  let previous = null;
  for (const entry of [...history].reverse()) {
    for (const change of diffRecords(previous, entry.data)) {
      if (change.kind === 'removed') delete result[change.name];
      else result[change.name] = entry;
    }
    previous = entry.data;
  }
  return result;
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
  hostname: { label: t('имена'), key: (device) => String(device.data.hostname ?? '').trim().toUpperCase() },
  serial: {
    label: t('серийники'),
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

// Text and loose key of a device, kept while the device object lives (the list is filtered on every key press).
// Reset by setPeople: person names are part of the text.
let searchCache = new WeakMap();

function searchText(device) {
  let cached = searchCache.get(device);
  if (!cached) {
    const values = [device.id];
    for (const value of Object.values(device.data)) values.push(displayValue(value));
    for (const field of PERSON_FIELDS) values.push(personName(device.data[field]));
    const text = values.join('\n').toLowerCase();
    const author = [device.updatedBy, device.updatedByName, device.updatedByKey].filter(Boolean).join('\n').toLowerCase();
    cached = { text, key: looseKey(text).key, author, authorKey: looseKey(author).key };
    searchCache.set(device, cached);
  }
  return cached;
}

// Every word of the query must occur somewhere in the record, as typed or in another alphabet (search-logic.js).
// duplicatesOf: a DUPLICATE_FIELDS name to keep only devices repeating that field (empty: all).
// inventory: 'all' | 'yes' | 'no'.
// authors: also search the author of the last change. Only while its column is shown: one admin who edited
// every record would otherwise be found in all of them, with nothing visible highlighted.
export function filterDevices(devices, { query = '', eset = 'all', inventory = 'all', duplicatesOf = '', authors = false } = {}) {
  const words = queryWords(query);
  const duplicates = duplicatesOf ? duplicateValues(devices, duplicatesOf) : null;
  return devices.filter((device) => {
    if (eset !== 'all' && esetState(device.data.esetInstalled) !== eset) return false;
    if (inventory !== 'all' && isInventoried(device) !== (inventory === 'yes')) return false;
    if (duplicates && !isDuplicate(device, duplicatesOf, duplicates)) return false;
    if (words.length === 0) return true;
    const { text, key, author, authorKey } = searchText(device);
    return words.every((word) => textMatches(text, word, key) || (authors && textMatches(author, word, authorKey)));
  });
}

const collator = new Intl.Collator(LANG, { numeric: true, sensitivity: 'base' });

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
  userId: (d) => personName(d.data.userId),
  ownerId: (d) => personName(d.data.ownerId),
  serial: (d) => d.data.serial,
  model: (d) => [d.data.manufacturer, d.data.model].filter((v) => !isBlank(v)).join(' '),
  windows: (d) => d.data.windows,
  eset: (d) => ['missing', 'unknown', 'installed'].indexOf(esetState(d.data.esetInstalled)),
  inventoried: (d) => (isInventoried(d) ? 1 : 0),
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
    // A person field gives a number or null (parsePerson).
    if (PERSON_FIELDS.includes(name)) {
      if ((original[name] ?? null) !== value) changes[name] = value;
      continue;
    }
    // A checkbox gives true/false; a missing value counts as false.
    const before = typeof value === 'boolean' ? original[name] === true : displayValue(original[name]);
    if (before !== value) changes[name] = value;
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


export const HISTORY_ACTIONS = { create: t('Создана'), update: t('Изменена'), delete: t('Удалена'), restore: t('Восстановлена') };

// Who made a change: 🔑 and the console key name when it is known (one user may hand keys to colleagues),
// 🌐 and the nickname for a change made on this site, the bare nickname otherwise (old console rows, GitHub).
// The email goes to the tooltip.
/** @param {string | null} [source] console, web or github */
export function authorLabel(name, email, key, source = null) {
  if (key) return `🔑 ${key}`;
  const who = name || email || '—';
  return source === 'web' ? `🌐 ${who}` : who;
}

/** @param {string | null} [source] */
export function authorTitle(name, email, key, source = null) {
  if (key) return f('Ключ пользователя {0} ({1})', name || email, email);
  return source === 'web' ? f('{0}, на сайте', email) : email;
}

// The author of the last change of a device.
export function deviceAuthor(device) {
  return authorLabel(device.updatedByName, device.updatedBy, device.updatedByKey, device.updatedSource);
}

// ---------- Journal ----------

// Keys of the settings table (src/settings.ts, src/trigger-words.ts) and their names in the journal.
const SETTINGS_NAMES = { network: t('Настройки сети'), 'suspector-trigger-words': t('Суспектор: слова-триггеры') };
const ACTIVE_LABELS = { true: t('включён'), false: t('отключён') };

// One journal entry as text: what was done and to what, with a link to the device card when there is one.
export function journalLine(entry) {
  const d = entry.details ?? {};
  const object = (text, href = null) => ({ object: text, href });
  switch (`${entry.kind}:${entry.action}`) {
    case 'device:create':
    case 'device:update':
    case 'device:delete':
    case 'device:restore': {
      const name = d.hostname || entry.target;
      return { what: f('Запись {0}', HISTORY_ACTIONS[entry.action].toLowerCase()), ...object(name, '#/devices/' + encodeURIComponent(entry.target)) };
    }
    case 'settings:update':
      return { what: t('Изменены настройки'), ...object(SETTINGS_NAMES[entry.target] ?? entry.target) };
    case 'release:publish':
      return { what: t('Опубликована версия Тулбокса'), ...object(entry.target) };
    case 'suspector-release:publish':
      return { what: t('Опубликована версия SUSpector'), ...object(entry.target) };
    case 'audit:release.current':
      return { what: t('Текущая версия Тулбокса'), ...object(entry.target) };
    case 'audit:suspector-release.current':
      return { what: t('Текущая версия SUSpector'), ...object(entry.target) };
    case 'audit:key.create':
      return { what: t('Создан ключ'), ...object(keyName(d.label, d, entry.actor)) };
    case 'audit:key.revoke':
      return { what: t('Отозван ключ'), ...object(keyName(d.label, d, entry.actor)) };
    case 'audit:key.rename': {
      const [before, after] = Array.isArray(d.label) ? d.label : ['', ''];
      return { what: t('Переименован ключ'), ...object(keyName(`${before}» → «${after}`, d, entry.actor)) };
    }
    case 'audit:user.create':
      return { what: t('Добавлен пользователь'), ...object(`${d.name ?? ''} (${ROLE_LABELS[d.role] ?? d.role}), ${entry.target}`) };
    case 'audit:user.update':
      return { what: t('Изменён пользователь'), ...object(`${entry.target}: ${userChanges(d)}`) };
    case 'audit:access.reject':
      return { what: t('Отклонён запрос доступа'), ...object(entry.target) };
    case 'audit:person.create':
      return { what: t('Добавлен человек'), ...object(`${d.name ?? ''} · #${entry.target}`, '#/people/' + entry.target) };
    case 'audit:person.update': {
      const parts = [];
      if (d.name) parts.push(`${d.name[0]} → ${d.name[1]}`);
      if (d.note) parts.push(f('примечание «{0}» → «{1}»', d.note[0], d.note[1]));
      return { what: t('Изменён человек'), ...object(`#${entry.target}: ${parts.join(', ')}`, '#/people/' + entry.target) };
    }
    case 'audit:person.delete':
      return { what: t('Удалён человек'), ...object(`${d.name ?? ''} · #${entry.target}`) };
    case 'audit:item.create':
      return { what: t('Добавлено майно'), ...object(itemName(d), '#/items') };
    case 'audit:item.update':
      return { what: t('Изменено майно'), ...object(`${d.type ?? ''}: ${itemChangeText(d)}`, '#/items') };
    case 'audit:item.delete':
      return { what: t('Удалено майно'), ...object(itemName(d)) };
    default:
      return { what: `${entry.kind} ${entry.action}`, ...object(entry.target) };
  }
}

// «Монітор Dell P2217H, CN-0RH81R»
function itemName(details) {
  const text = [details.type, details.model].filter((v) => !isBlank(v)).join(' ');
  return `${text}${details.serial ? ', ' + details.serial : ''}`;
}

const ITEM_FIELD_NAMES = { type: t('тип'), model: t('модель'), serial: t('серийный номер'), location: t('место'), ownerId: 'МВО', note: t('примечание') };

// "МВО Лукашик → —, примечание «» → «під сходами»"
function itemChangeText(details) {
  return Object.entries(ITEM_FIELD_NAMES)
    .filter(([field]) => Array.isArray(details[field]))
    .map(([field, label]) => {
      const [before, after] = details[field];
      if (field === 'ownerId') return `${label} ${personName(before) || '—'} → ${personName(after) || '—'}`;
      return `${label} «${before ?? ''}» → «${after ?? ''}»`;
    })
    .join(', ');
}

// «Флешка», and whose it is when an admin changed a key of another user.
function keyName(label, details, actor) {
  const text = `«${label ?? ''}»`;
  return details.owner && details.owner !== actor ? `${text}, ${details.owner}` : text;
}

// "роль Просмотр → Редактор, отключён"
function userChanges(details) {
  const parts = [];
  if (details.name) parts.push(f('имя {0} → {1}', details.name[0], details.name[1]));
  if (details.role) parts.push(f('роль {0} → {1}', ROLE_LABELS[details.role[0]] ?? details.role[0], ROLE_LABELS[details.role[1]] ?? details.role[1]));
  if (details.active) parts.push(ACTIVE_LABELS[String(details.active[1])]);
  return parts.join(', ');
}

// Who did it: 🔑 the console key, 🌐 the nickname on the site, or GitHub Actions for published releases.
export function journalActor(entry) {
  return authorLabel(entry.actorName, entry.actor, entry.actorKey, entry.source);
}

export function journalActorTitle(entry) {
  if (entry.kind === 'release' || entry.kind === 'suspector-release') return '';
  return authorTitle(entry.actorName, entry.actor, entry.actorKey, entry.source);
}
