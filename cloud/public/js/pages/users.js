import { api } from '../api.js';
import { errorBox, h, replace, toast } from '../dom.js';
import { f, t } from '../i18n.js';
import { formatDate, ROLE_LABELS } from '../logic.js';

const ROLE_HINTS = {
  viewer: t('только просмотр в вебе'),
  harvester: t('сбор и синхронизация с флешки'),
  editor: t('плюс правка записей в вебе'),
  admin: t('плюс пользователи, роли и удаление'),
};

export async function renderUsers(context) {
  const { main } = context;
  replace(main, h('p', { class: 'muted' }, t('Загрузка пользователей…')));
  let users;
  let requests;
  try {
    [{ users }, { requests }] = await Promise.all([api('/users'), api('/access-requests')]);
  } catch (error) {
    replace(main, h('h1', {}, t('Пользователи')), errorBox(error, () => renderUsers(context)));
    return;
  }

  replace(
    main,
    h('h1', {}, t('Пользователи')),
    requests.length > 0 &&
      h('section', { class: 'panel requests' }, h('h2', {}, f('Запросы на доступ ({0})', requests.length)), requestsTable(context, requests)),
    h('section', { class: 'panel' }, h('h2', {}, t('Добавить пользователя')), addForm(context)),
    h('section', { class: 'panel' }, h('h2', {}, f('Все пользователи ({0})', users.length)), usersTable(context, users)),
    h(
      'section',
      { class: 'panel muted small' },
      h('h2', {}, t('Роли')),
      h('ul', {}, Object.entries(ROLE_LABELS).map(([role, label]) => h('li', {}, h('b', {}, label), ` — ${ROLE_HINTS[role]}`))),
    ),
  );
}

function roleSelect(value, props = {}) {
  return h(
    'select',
    props,
    Object.entries(ROLE_LABELS).map(([role, label]) => h('option', { value: role, selected: role === value }, label)),
  );
}

// People who signed in with an unknown email: approve with a name and role, or reject.
function requestsTable(context, requests) {
  return h(
    'div',
    { class: 'table-wrap' },
    h(
      'table',
      {},
      h('thead', {}, h('tr', {}, h('th', {}, t('Почта')), h('th', {}, t('Запрошено')), h('th', {}, t('Имя')), h('th', {}, t('Роль')), h('th', {}))),
      h('tbody', {}, requests.map((request) => requestRow(context, request))),
    ),
  );
}

function requestRow(context, request) {
  const name = h('input', { value: request.email.split('@')[0], maxlength: '100', 'aria-label': f('Имя {0}', request.email) });
  const role = roleSelect('viewer', { 'aria-label': f('Роль {0}', request.email) });
  const buttons = [];
  // Runs one action with both buttons disabled, then refreshes the page and the nav counter.
  const act = async (action, done) => {
    buttons.forEach((button) => (button.disabled = true));
    try {
      await action();
      toast(done);
    } catch (error) {
      buttons.forEach((button) => (button.disabled = false));
      toast(error.message, 'error');
      return;
    }
    await context.onRequestsChanged?.();
    await renderUsers(context);
  };
  buttons.push(
    h(
      'button',
      {
        class: 'button small primary',
        onclick: () =>
          act(
            () => api('/users', { method: 'POST', body: { email: request.email, name: name.value, role: role.value } }),
            f('Доступ для {0} открыт.', request.email),
          ),
      },
      t('Пустить'),
    ),
    h(
      'button',
      {
        class: 'button small danger',
        onclick: () => {
          if (!confirm(f('Отклонить запрос {0}? Пустить его можно будет позже, добавив вручную.', request.email))) return;
          act(() => api('/access-requests/' + encodeURIComponent(request.email), { method: 'DELETE' }), t('Запрос отклонён.'));
        },
      },
      t('Отклонить'),
    ),
  );
  return h(
    'tr',
    {},
    h('td', {}, request.email),
    h('td', { class: 'nowrap', title: f('Последний вход: {0}', formatDate(request.lastSeenAt)) }, formatDate(request.requestedAt)),
    h('td', {}, name),
    h('td', {}, role),
    h('td', { class: 'nowrap' }, h('div', { class: 'actions' }, buttons)),
  );
}

function addForm(context) {
  const email = h('input', { type: 'email', placeholder: t('почта@пример.com'), required: true, 'aria-label': t('Почта') });
  const name = h('input', { placeholder: t('Имя'), required: true, maxlength: '100', 'aria-label': t('Имя') });
  const role = roleSelect('harvester', { 'aria-label': t('Роль') });
  const submit = h('button', { class: 'button primary', type: 'submit' }, t('Добавить'));
  return h(
    'form',
    {
      class: 'inline-form',
      onsubmit: async (event) => {
        event.preventDefault();
        submit.disabled = true;
        try {
          const created = await api('/users', { method: 'POST', body: { email: email.value, name: name.value, role: role.value } });
          toast(f('Пользователь {0} добавлен.', created.email));
          await context.onRequestsChanged?.();
          await renderUsers(context);
        } catch (error) {
          submit.disabled = false;
          toast(error.message, 'error');
        }
      },
    },
    email,
    name,
    role,
    submit,
  );
}

async function updateUser(context, user, changes) {
  try {
    await api('/users/' + encodeURIComponent(user.email), { method: 'PATCH', body: changes });
    toast(t('Сохранено.'));
  } catch (error) {
    toast(error.message, 'error');
  }
  await renderUsers(context);
}

function usersTable(context, users) {
  const self = context.me.email;
  return h(
    'div',
    { class: 'table-wrap' },
    h(
      'table',
      {},
      h('thead', {}, h('tr', {}, h('th', {}, t('Почта')), h('th', {}, t('Имя')), h('th', {}, t('Роль')), h('th', {}, t('Доступ')), h('th', {}, t('Добавлен')))),
      h(
        'tbody',
        {},
        users.map((user) => {
          const isSelf = user.email === self;
          return h(
            'tr',
            { class: user.active ? undefined : 'inactive' },
            h('td', {}, user.email, isSelf && h('span', { class: 'badge badge-muted' }, t('это вы'))),
            h(
              'td',
              {},
              h('input', {
                value: user.name,
                maxlength: '100',
                'aria-label': f('Имя {0}', user.email),
                onchange: (event) => updateUser(context, user, { name: event.target.value }),
              }),
            ),
            h(
              'td',
              {},
              roleSelect(user.role, {
                disabled: isSelf,
                title: isSelf ? t('Нельзя менять собственную роль') : undefined,
                'aria-label': f('Роль {0}', user.email),
                onchange: (event) => updateUser(context, user, { role: event.target.value }),
              }),
            ),
            h(
              'td',
              {},
              h(
                'label',
                { class: 'check' },
                h('input', {
                  type: 'checkbox',
                  checked: user.active,
                  disabled: isSelf,
                  onchange: (event) => updateUser(context, user, { active: event.target.checked }),
                }),
                user.active ? t('включён') : t('отключён'),
              ),
            ),
            h('td', { class: 'nowrap' }, formatDate(user.createdAt)),
          );
        }),
      ),
    ),
  );
}
