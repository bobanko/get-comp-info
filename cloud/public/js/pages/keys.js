import { api } from '../api.js';
import { copyText, errorBox, h, replace, toast } from '../dom.js';
import { formatDate, hasRole } from '../logic.js';

const state = { all: false, created: null };

export async function renderKeys(context) {
  const { me, main } = context;
  replace(main, h('p', { class: 'muted' }, 'Загрузка ключей…'));
  let keys;
  try {
    keys = (await api(state.all ? '/keys?all=1' : '/keys')).keys;
  } catch (error) {
    replace(main, h('h1', {}, 'Ключи доступа'), errorBox(error, () => renderKeys(context)));
    return;
  }

  const label = h('input', { name: 'label', placeholder: 'Например: Флешка Сергея', maxlength: '100', required: true, 'aria-label': 'Название ключа' });
  const createButton = h('button', { class: 'button primary', type: 'submit' }, 'Создать ключ');
  const form = h(
    'form',
    {
      class: 'inline-form',
      onsubmit: async (event) => {
        event.preventDefault();
        createButton.disabled = true;
        try {
          state.created = await api('/keys', { method: 'POST', body: { label: label.value } });
          await renderKeys(context);
        } catch (error) {
          createButton.disabled = false;
          toast(error.message, 'error');
        }
      },
    },
    label,
    createButton,
  );

  replace(
    main,
    h('h1', {}, 'Ключи доступа'),
    h(
      'p',
      { class: 'lead' },
      'Личный ключ нужен консольной программе для синхронизации с облаком. Создайте отдельный ключ для каждой флешки или компьютера, чтобы при потере можно было отозвать только его.',
    ),
    state.created && newKeyPanel(state.created),
    h('section', { class: 'panel' }, h('h2', {}, 'Новый ключ'), form),
    h(
      'section',
      { class: 'panel' },
      h(
        'div',
        { class: 'panel-head' },
        h('h2', {}, state.all ? 'Ключи всех пользователей' : 'Мои ключи'),
        hasRole(me, 'admin') &&
          h(
            'label',
            { class: 'check' },
            h('input', {
              type: 'checkbox',
              checked: state.all,
              onchange: (event) => {
                state.all = event.target.checked;
                state.created = null;
                renderKeys(context);
              },
            }),
            'Все пользователи',
          ),
      ),
      keysTable(context, keys),
    ),
  );
  // The new key is shown only once: leaving the page forgets it.
  state.created = null;
}

function newKeyPanel(created) {
  const copyButton = h(
    'button',
    {
      class: 'button',
      type: 'button',
      onclick: async () => {
        toast((await copyText(created.key)) ? 'Ключ скопирован.' : 'Не удалось скопировать, выделите ключ вручную.', 'ok');
      },
    },
    'Копировать',
  );
  return h(
    'section',
    { class: 'panel new-key', role: 'status' },
    h('h2', {}, `Ключ «${created.label}» создан`),
    h('div', { class: 'key-row' }, h('code', { class: 'key' }, created.key), copyButton),
    h(
      'p',
      {},
      'Скопируйте его сейчас: ключ показывается только один раз. Введите ключ при синхронизации — программа предложит сохранить его в файл ',
      h('code', {}, 'cloud-key.txt'),
      '. Не пересылайте ключ в мессенджерах.',
    ),
  );
}

function keysTable(context, keys) {
  if (keys.length === 0) return h('p', { class: 'empty' }, 'Ключей пока нет.');
  return h(
    'div',
    { class: 'table-wrap' },
    h(
      'table',
      {},
      h(
        'thead',
        {},
        h(
          'tr',
          {},
          state.all && h('th', {}, 'Пользователь'),
          h('th', {}, 'Название'),
          h('th', {}, 'Создан'),
          h('th', {}, 'Последнее использование'),
          h('th', {}, 'Статус'),
          h('th', {}),
        ),
      ),
      h(
        'tbody',
        {},
        keys.map((key) =>
          h(
            'tr',
            {},
            state.all && h('td', {}, key.userEmail),
            h('td', {}, key.label),
            h('td', { class: 'nowrap' }, formatDate(key.createdAt)),
            h('td', { class: 'nowrap' }, key.lastUsedAt ? formatDate(key.lastUsedAt) : h('span', { class: 'muted' }, 'не использовался')),
            h(
              'td',
              {},
              key.revokedAt
                ? h('span', { class: 'badge badge-muted' }, `отозван ${formatDate(key.revokedAt)}`)
                : h('span', { class: 'badge badge-ok' }, 'активен'),
            ),
            h(
              'td',
              { class: 'right' },
              !key.revokedAt &&
                h(
                  'button',
                  {
                    class: 'button danger small',
                    onclick: async () => {
                      if (!confirm(`Отозвать ключ «${key.label}»? Программа с этим ключом больше не сможет синхронизироваться.`)) return;
                      try {
                        await api('/keys/' + encodeURIComponent(key.id), { method: 'DELETE' });
                        toast('Ключ отозван.');
                        await renderKeys(context);
                      } catch (error) {
                        toast(error.message, 'error');
                      }
                    },
                  },
                  'Отозвать',
                ),
            ),
          ),
        ),
      ),
    ),
  );
}
