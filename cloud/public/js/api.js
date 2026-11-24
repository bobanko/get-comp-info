// Calls to /api/web. The server answers errors in English; the page shows its own text by error code
// and falls back to the server's message for codes it does not know (or without the details a text needs).

import { f, t } from './i18n.js';

const MESSAGES = {
  unauthorized: () => t('Требуется вход через Cloudflare Access.'),
  invalid_token: () => t('Сессия входа недействительна. Войдите заново.'),
  access_not_configured: () => t('Cloudflare Access не настроен.'),
  user_disabled: (e) => (e.email ? f('Доступ для {0} отключён администратором.', e.email) : t('Пользователь отключён.')),
  access_denied: (e) => e.email && f('Администратор отклонил запрос на доступ для {0}.', e.email),
  access_requests_full: (e) =>
    e.email && f('Нет доступа для {0}. Запросов на доступ слишком много: попросите администратора добавить вас вручную.', e.email),
  access_pending: (e) =>
    e.email && f('Запрос на доступ для {0} отправлен администратору. Когда он подтвердит, обновите страницу.', e.email),
  forbidden: () => t('Недостаточно прав для этого действия.'),
  not_found: () => t('Не найдено: возможно, это уже удалили. Обновите страницу.'),
  busy: () => t('Запись одновременно изменяется. Повторите попытку.'),
  readonly_field: () => t('Поле computerId изменить нельзя.'),
  conflict: () => t('Данные уже изменили. Обновите страницу.'),
  invalid_label: (e) => e.maxLength && f('Название ключа обязательно, не длиннее {0} символов.', e.maxLength),
  invalid_name: (e) => e.maxLength && f('Имя обязательно, не длиннее {0} символов.', e.maxLength),
  invalid_email: () => t('Укажите корректный адрес почты.'),
  invalid_role: () => t('Роль должна быть viewer, harvester, editor или admin.'),
  user_exists: () => t('Пользователь с такой почтой уже есть.'),
  self_lockout: () => t('Нельзя снять с себя роль admin или отключить себя.'),
  invalid_trigger_words: () => t('В списке слов есть ошибки: исправьте их и сохраните снова.'),
  internal: () => t('Внутренняя ошибка сервера.'),
};

// Text of an error answer { error: { code, message }, ...details } for the page.
export function errorText(code, data, status) {
  const translate = MESSAGES[code];
  const details = { ...(data ?? {}), ...(data?.error ?? {}) };
  return (translate && translate(details)) || data?.error?.message || f('Облако ответило ошибкой {0}.', status);
}

export class ApiError extends Error {
  constructor(status, code, message, body = null) {
    super(message);
    this.status = status;
    this.code = code;
    this.body = body;
  }
}

export async function api(path, { method = 'GET', body } = {}) {
  let response;
  try {
    response = await fetch('/api/web' + path, {
      method,
      headers: body === undefined ? {} : { 'Content-Type': 'application/json' },
      body: body === undefined ? undefined : JSON.stringify(body),
      credentials: 'same-origin',
      // An expired Cloudflare Access session answers with a redirect to the login page.
      redirect: 'manual',
    });
  } catch {
    throw new ApiError(0, 'network', t('Нет связи с облаком. Проверьте интернет и повторите.'));
  }
  if (response.type === 'opaqueredirect' || response.status === 0) {
    throw new ApiError(401, 'session_expired', t('Сессия входа истекла. Обновите страницу, чтобы войти снова.'));
  }
  const text = await response.text();
  let data = null;
  try {
    data = text ? JSON.parse(text) : null;
  } catch {
    data = null;
  }
  if (!response.ok) {
    const code = data?.error?.code ?? `http_${response.status}`;
    throw new ApiError(response.status, code, errorText(code, data, response.status), data);
  }
  return data;
}
