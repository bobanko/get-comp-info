// A person field of a form: pick «ПІБ · #12» from the list, or type a new name that is added to the list on save.
import { api } from './api.js';
import { h } from './dom.js';
import { f, t } from './i18n.js';
import { findPerson, isBlank, parsePerson, personOption } from './logic.js';

export const PEOPLE_LIST_ID = 'people-options';

// One <datalist> per form: the live people as «ПІБ · #12».
export function peopleDatalist(people) {
  return h(
    'datalist',
    { id: PEOPLE_LIST_ID },
    (people ?? []).filter((person) => !person.deleted).map((person) => h('option', { value: personOption(person) })),
  );
}

// The text a person field starts with for a person number.
export function personInputText(id) {
  if (isBlank(id)) return '';
  const person = findPerson(id);
  return person ? personOption(person) : `#${id}`;
}

export function personInput(name, value, props = {}) {
  return h('input', { name, value, list: PEOPLE_LIST_ID, autocomplete: 'off', placeholder: t('ПІБ из списка или новый'), ...props });
}

// Texts of person fields ({ field: { label, text } }) -> { field: number | null }. New names are added to the people
// list (О/С) after one confirmation. Returns null when the user declines; throws an Error with a message to show.
export async function resolvePeople(fields, people) {
  const result = {};
  const newNames = {};
  for (const [field, { label, text }] of Object.entries(fields)) {
    const parsed = parsePerson(text, people);
    if (parsed.error) throw new Error(`${label}: ${parsed.error}`);
    if (parsed.newName) newNames[field] = parsed.newName;
    else result[field] = parsed.id;
  }
  const added = [...new Set(Object.values(newNames))];
  if (added.length === 0) return result;
  if (!confirm(f('Добавить в особовий склад: {0}?', added.join(', ')))) return null;
  const ids = {};
  for (const name of added) ids[name] = (await api('/people', { method: 'POST', body: { name } })).person.id;
  for (const [field, name] of Object.entries(newNames)) result[field] = ids[name];
  return result;
}
