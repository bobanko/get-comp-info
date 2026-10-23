import { api } from '../api.js';
import { errorBox, h, replace, toast } from '../dom.js';
import { formatDate, ROLE_LABELS } from '../logic.js';

const ROLE_HINTS = {
  viewer: 'только просмотр в вебе',
  harvester: 'сбор и синхронизация с флешки',
  editor: 'плюс правка записей в вебе',
  admin: 'плюс пользователи, роли и удаление',
};

// Shown until the page is left when Cloudflare Access could not be updated automatically.
let accessWarning = null;

export async function renderUsers(context) {
  const { main } = context;
  replace(main, h('p', { class: 'muted' }, 'Загрузка пользователей…'));
  let users;
  try {
    users = (await api('/users')).users;
  } catch (error) {
    replace(main, h('h1', {}, 'Пользователи'), errorBox(error, () => renderUsers(context)));
    return;
  }

  replace(
    main,
    h('h1', {}, 'Пользователи'),
    accessWarning && accessNotice(accessWarning),
    h('section', { class: 'panel' }, h('h2', {}, 'Добавить пользователя'), addForm(context)),
    h('section', { class: 'panel' }, h('h2', {}, `Все пользователи (${users.length})`), usersTable(context, users)),
    h(
      'section',
      { class: 'panel muted small' },
      h('h2', {}, 'Роли'),
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

// Reports how the Access allow list was updated after a change that affects sign-in.
function reportAccess(access, email) {
  if (!access) return;
  if (access.synced) {
    accessWarning = null;
    toast('Список входа в Cloudflare Access обновлён.');
    return;
  }
  accessWarning = { email, reason: access.reason };
}

function accessNotice(warning) {
  const manual = warning.reason === 'not_configured';
  return h(
    'div',
    { class: manual ? 'notice' : 'notice notice-error' },
    manual
      ? h(
          'p',
          {},
          `Добавьте или уберите ${warning.email} в политике Cloudflare Access «Comp Info team» (Zero Trust → Access → Policies). Автоматическое обновление списка входа не настроено.`,
        )
      : h('p', {}, `Список входа в Cloudflare Access не обновился: ${warning.reason}. Проверьте политику «Comp Info team» вручную.`),
  );
}

function addForm(context) {
  const email = h('input', { type: 'email', placeholder: 'почта@пример.com', required: true, 'aria-label': 'Почта' });
  const name = h('input', { placeholder: 'Имя', required: true, maxlength: '100', 'aria-label': 'Имя' });
  const role = roleSelect('harvester', { 'aria-label': 'Роль' });
  const submit = h('button', { class: 'button primary', type: 'submit' }, 'Добавить');
  return h(
    'form',
    {
      class: 'inline-form',
      onsubmit: async (event) => {
        event.preventDefault();
        submit.disabled = true;
        try {
          const created = await api('/users', { method: 'POST', body: { email: email.value, name: name.value, role: role.value } });
          toast(`Пользователь ${created.email} добавлен.`);
          reportAccess(created.access, created.email);
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
    const updated = await api('/users/' + encodeURIComponent(user.email), { method: 'PATCH', body: changes });
    toast('Сохранено.');
    reportAccess(updated.access, updated.email);
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
      h('thead', {}, h('tr', {}, h('th', {}, 'Почта'), h('th', {}, 'Имя'), h('th', {}, 'Роль'), h('th', {}, 'Доступ'), h('th', {}, 'Добавлен'))),
      h(
        'tbody',
        {},
        users.map((user) => {
          const isSelf = user.email === self;
          return h(
            'tr',
            { class: user.active ? undefined : 'inactive' },
            h('td', {}, user.email, isSelf && h('span', { class: 'badge badge-muted' }, 'это вы')),
            h(
              'td',
              {},
              h('input', {
                value: user.name,
                maxlength: '100',
                'aria-label': `Имя ${user.email}`,
                onchange: (event) => updateUser(context, user, { name: event.target.value }),
              }),
            ),
            h(
              'td',
              {},
              roleSelect(user.role, {
                disabled: isSelf,
                title: isSelf ? 'Нельзя менять собственную роль' : undefined,
                'aria-label': `Роль ${user.email}`,
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
                user.active ? 'включён' : 'отключён',
              ),
            ),
            h('td', { class: 'nowrap' }, formatDate(user.createdAt)),
          );
        }),
      ),
    ),
  );
}
