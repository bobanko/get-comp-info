// The form of an item of property («Майно»), shared by the property list and the card of a person in О/С.
import { api } from './api.js';
import { h, toast } from './dom.js';
import { f, t } from './i18n.js';
import { isBlank, itemModels, itemTypes } from './logic.js';
import { peopleDatalist, personInput, personInputText, resolvePeople } from './person-input.js';

export const ITEM_FIELDS = [
  { key: 'type', label: t('Тип'), max: 100 },
  { key: 'model', label: t('Модель'), max: 200 },
  { key: 'serial', label: t('Серийный номер'), max: 200, mono: true },
  { key: 'location', label: t('Место / секция'), max: 200 },
];

const TYPES_LIST_ID = 'item-types';
const MODELS_LIST_ID = 'item-models';

// The suggestions of the form: types, models and people. One set per page, next to the form.
// items: the items of property typed before (not the computers).
export function itemDatalists(items, people) {
  return [
    h('datalist', { id: TYPES_LIST_ID }, itemTypes(items).map(([type]) => h('option', { value: type }))),
    h('datalist', { id: MODELS_LIST_ID }),
    peopleDatalist(people),
  ];
}

function fillModels(items, type) {
  const list = document.getElementById(MODELS_LIST_ID);
  list?.replaceChildren(...itemModels(items, type).map((model) => h('option', { value: model })));
}

// The fields of an item: { type, model, serial, location, owner, note } inputs. The model suggestions follow the type.
export function itemInputs(item, items) {
  const fields = Object.fromEntries(
    ITEM_FIELDS.map((field) => [
      field.key,
      h('input', {
        value: item[field.key] ?? '',
        maxlength: String(field.max),
        'aria-label': field.label,
        placeholder: field.label,
        class: field.mono ? 'mono' : undefined,
        list: field.key === 'type' ? TYPES_LIST_ID : field.key === 'model' ? MODELS_LIST_ID : undefined,
        autocomplete: field.key === 'type' || field.key === 'model' ? 'off' : undefined,
      }),
    ]),
  );
  fields.type.addEventListener('input', () => fillModels(items, fields.type.value));
  fields.model.addEventListener('focus', () => fillModels(items, fields.type.value));
  fields.owner = personInput('owner', personInputText(item.ownerId), { 'aria-label': 'МВО' });
  fields.note = h('input', { value: item.note ?? '', maxlength: '1000', 'aria-label': t('Примечание'), placeholder: t('Примечание') });
  return fields;
}

// The body of POST/PATCH /items from the fields; null when the person declined to add a new name to О/С.
export async function itemValues(fields, people) {
  const owner = await resolvePeople({ ownerId: { label: 'МВО', text: fields.owner.value } }, people);
  if (owner === null) return null;
  const result = { ownerId: owner.ownerId, note: fields.note.value };
  for (const field of ITEM_FIELDS) result[field.key] = fields[field.key].value;
  return result;
}

export function itemTitle(item) {
  return [item.type, item.model].filter((v) => !isBlank(v)).join(' ');
}

// Escape cancels the editing, as the «Отмена» button.
export const escapeTo = (cancel) => (event) => {
  if (event.key !== 'Escape') return;
  event.preventDefault();
  cancel();
};

// The «Добавить майно» panel. defaults: the fields it starts with ({ ownerId, type }); onAdded runs after the save.
export function addItemPanel({ items, people, defaults = {}, onAdded, onCancel }) {
  const fields = itemInputs(defaults, items);
  const add = h('button', { class: 'button primary', type: 'submit' }, t('Добавить'));
  return h(
    'section',
    { class: 'panel add-panel' },
    h('h2', {}, t('Добавить майно')),
    h(
      'form',
      {
        class: 'inline-form items-form',
        onkeydown: escapeTo(onCancel),
        onsubmit: async (event) => {
          event.preventDefault();
          add.disabled = true;
          try {
            const body = await itemValues(fields, people);
            if (body === null) {
              add.disabled = false;
              return;
            }
            const { item } = await api('/items', { method: 'POST', body });
            toast(f('Добавлено: {0}.', itemTitle(item)));
            await onAdded(item);
          } catch (error) {
            add.disabled = false;
            toast(error.message, 'error');
          }
        },
      },
      ITEM_FIELDS.map((field) => fields[field.key]),
      fields.owner,
      fields.note,
      add,
      h('button', { class: 'button', type: 'button', title: t('Отмена (Esc)'), onclick: onCancel }, t('Отмена')),
    ),
  );
}
