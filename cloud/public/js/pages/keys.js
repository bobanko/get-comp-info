import { api } from '../api.js';
import { copyText, errorBox, h, replace, toast } from '../dom.js';
import { f, t } from '../i18n.js';
import { formatDate, hasRole } from '../logic.js';

// renaming: the id of the key whose name is being edited.
const state = { all: false, created: null, renaming: null };

export async function renderKeys(context) {
  const { me, main } = context;
  replace(main, h('p', { class: 'muted' }, t('Загрузка ключей…')));
  let keys;
  try {
    keys = (await api(state.all ? '/keys?all=1' : '/keys')).keys;
  } catch (error) {
    replace(main, h('h1', {}, t('Ключи')), errorBox(error, () => renderKeys(context)));
    return;
  }

  const label = h('input', { name: 'label', placeholder: t('Например: Флешка Сергея'), maxlength: '100', required: true, 'aria-label': t('Название ключа') });
  const createButton = h('button', { class: 'button primary', type: 'submit' }, t('Создать ключ'));
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
    h('h1', {}, t('Ключи')),
    h(
      'p',
      { class: 'lead' },
      t('Личный ключ нужен программе для синхронизации с облаком. Создайте отдельный ключ для каждой флешки или компьютера, чтобы при потере можно было отозвать только его. Название ключа видно в журнале и истории записей — его можно поменять.'),
    ),
    state.created && newKeyPanel(state.created),
    h('section', { class: 'panel' }, h('h2', {}, t('Новый ключ')), form),
    h(
      'section',
      { class: 'panel' },
      h(
        'div',
        { class: 'panel-head' },
        h('h2', {}, state.all ? t('Ключи всех пользователей') : t('Мои ключи')),
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
                state.renaming = null;
                renderKeys(context);
              },
            }),
            t('Все пользователи'),
          ),
      ),
      keysTable(context, keys),
    ),
  );
  // The new key is shown only once: leaving the page forgets it.
  state.created = null;
  main.querySelector('input.rename')?.focus();
}

function newKeyPanel(created) {
  const copyButton = h(
    'button',
    {
      class: 'button',
      type: 'button',
      onclick: async () => {
        toast((await copyText(created.key)) ? t('Ключ скопирован.') : t('Не удалось скопировать, выделите ключ вручную.'), 'ok');
      },
    },
    t('Копировать'),
  );
  return h(
    'section',
    { class: 'panel new-key', role: 'status' },
    h('h2', {}, f('Ключ «{0}» создан', created.label)),
    h('div', { class: 'key-row' }, h('code', { class: 'key' }, created.key), copyButton),
    h(
      'p',
      {},
      t('Скопируйте его сейчас: ключ показывается только один раз. Введите ключ при синхронизации — программа предложит сохранить его в файл '),
      h('code', {}, 'cloud-key.txt'),
      t('. Не пересылайте ключ в мессенджерах.'),
    ),
  );
}

// The name of a key, or the rename form for the key being renamed: Enter saves, Escape cancels.
function nameCell(context, key) {
  if (state.renaming !== key.id) return h('td', { class: 'key-name' }, key.label);
  const cancel = () => {
    state.renaming = null;
    renderKeys(context);
  };
  const input = h('input', { class: 'rename', value: key.label, maxlength: '100', required: true, 'aria-label': t('Новое название ключа') });
  return h(
    'td',
    { class: 'key-name' },
    h(
      'form',
      {
        class: 'inline-form rename-form',
        onkeydown: (event) => {
          if (event.key !== 'Escape') return;
          event.preventDefault();
          cancel();
        },
        onsubmit: async (event) => {
          event.preventDefault();
          try {
            await api('/keys/' + encodeURIComponent(key.id), { method: 'PATCH', body: { label: input.value } });
            toast(t('Ключ переименован.'));
            state.renaming = null;
            await renderKeys(context);
          } catch (error) {
            toast(error.message, 'error');
          }
        },
      },
      input,
      h('button', { class: 'button small primary', type: 'submit' }, t('Сохранить')),
      h('button', { class: 'button small', type: 'button', title: t('Отмена (Esc)'), onclick: cancel }, t('Отмена')),
    ),
  );
}

function keysTable(context, keys) {
  if (keys.length === 0) return h('p', { class: 'empty' }, t('Ключей пока нет.'));
  return h(
    'div',
    { class: 'table-wrap' },
    h(
      'table',
      { class: 'keys' },
      h(
        'thead',
        {},
        h(
          'tr',
          {},
          state.all && h('th', {}, t('Пользователь')),
          h('th', {}, t('Название')),
          h('th', {}, t('Создан')),
          h('th', {}, t('Последнее использование')),
          h('th', {}, t('Статус')),
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
            state.all && h('td', { class: 'key-owner' }, key.userEmail),
            nameCell(context, key),
            h('td', { class: 'nowrap', 'data-label': t('Создан') }, formatDate(key.createdAt)),
            h('td', { class: 'nowrap', 'data-label': t('Использован') }, key.lastUsedAt ? formatDate(key.lastUsedAt) : h('span', { class: 'muted' }, t('не использовался'))),
            h(
              'td',
              {},
              key.revokedAt
                ? h('span', { class: 'badge badge-muted' }, f('отозван {0}', formatDate(key.revokedAt)))
                : h('span', { class: 'badge badge-ok' }, t('активен')),
            ),
            h(
              'td',
              { class: 'row-actions' },
              state.renaming !== key.id &&
                h(
                  'button',
                  {
                    class: 'button small',
                    onclick: () => {
                      state.renaming = key.id;
                      renderKeys(context);
                    },
                  },
                  t('Переименовать'),
                ),
              !key.revokedAt &&
                h(
                  'button',
                  {
                    class: 'button danger small',
                    onclick: async () => {
                      if (!confirm(f('Отозвать ключ «{0}»? Программа с этим ключом больше не сможет синхронизироваться.', key.label))) return;
                      try {
                        await api('/keys/' + encodeURIComponent(key.id), { method: 'DELETE' });
                        toast(t('Ключ отозван.'));
                        await renderKeys(context);
                      } catch (error) {
                        toast(error.message, 'error');
                      }
                    },
                  },
                  t('Отозвать'),
                ),
            ),
          ),
        ),
      ),
    ),
  );
}
