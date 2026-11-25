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

// Users and access requests as cards: a table of five columns did not fit a phone.
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
    requests.length > 0 && [
      h('h2', { class: 'section-title first' }, f('Запросы на доступ ({0})', requests.length)),
      h('div', { class: 'user-cards' }, requests.map((request) => requestCard(context, request))),
    ],
    h('section', { class: 'panel' }, h('h2', {}, t('Добавить пользователя')), addForm(context)),
    h('h2', { class: 'section-title' }, f('Все пользователи ({0})', users.length)),
    h('div', { class: 'user-cards' }, users.map((user) => userCard(context, user))),
    h(
      'section',
      { class: 'panel muted small roles' },
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

// A field of a card: a small caption over the control.
function field(caption, control) {
  return h('label', { class: 'card-field' }, h('span', { class: 'muted small' }, caption), control);
}

// Someone who signed in with an unknown email: approve with a name and role, or reject.
function requestCard(context, request) {
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
        class: 'button primary',
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
        class: 'button danger',
        onclick: () => {
          if (!confirm(f('Отклонить запрос {0}? Пустить его можно будет позже, добавив вручную.', request.email))) return;
          act(() => api('/access-requests/' + encodeURIComponent(request.email), { method: 'DELETE' }), t('Запрос отклонён.'));
        },
      },
      t('Отклонить'),
    ),
  );
  const seenLater = request.lastSeenAt !== request.requestedAt;
  return h(
    'article',
    { class: 'panel user-card request' },
    h('div', { class: 'user-card-title' }, request.email),
    h(
      'p',
      { class: 'muted small' },
      f('Запрошено {0}', formatDate(request.requestedAt)),
      seenLater && ` · ${f('Последний вход: {0}', formatDate(request.lastSeenAt))}`,
    ),
    h('div', { class: 'card-fields' }, field(t('Имя'), name), field(t('Роль'), role)),
    h('div', { class: 'actions' }, buttons),
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

// One user: the name (edited in place), the email, the role and the access switch.
function userCard(context, user) {
  const isSelf = user.email === context.me.email;
  return h(
    'article',
    { class: user.active ? 'panel user-card' : 'panel user-card inactive' },
    h(
      'div',
      { class: 'user-card-title' },
      user.email,
      isSelf && h('span', { class: 'badge badge-muted' }, t('это вы')),
      !user.active && h('span', { class: 'badge badge-danger' }, t('отключён')),
    ),
    h(
      'div',
      { class: 'card-fields' },
      field(
        t('Имя'),
        h('input', {
          value: user.name,
          maxlength: '100',
          'aria-label': f('Имя {0}', user.email),
          onchange: (event) => updateUser(context, user, { name: event.target.value }),
        }),
      ),
      field(
        t('Роль'),
        roleSelect(user.role, {
          disabled: isSelf,
          title: isSelf ? t('Нельзя менять собственную роль') : undefined,
          'aria-label': f('Роль {0}', user.email),
          onchange: (event) => updateUser(context, user, { role: event.target.value }),
        }),
      ),
    ),
    h(
      'div',
      { class: 'user-card-foot' },
      h(
        'label',
        { class: 'check', title: isSelf ? t('Нельзя снять с себя роль admin или отключить себя.') : undefined },
        h('input', {
          type: 'checkbox',
          checked: user.active,
          disabled: isSelf,
          onchange: (event) => updateUser(context, user, { active: event.target.checked }),
        }),
        t('Доступ включён'),
      ),
      h('span', { class: 'muted small' }, f('Добавлен {0}', formatDate(user.createdAt))),
    ),
  );
}
