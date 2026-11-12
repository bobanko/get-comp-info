// Calls to /api/web. The server answers errors in English; the page shows its own text by error code
// and falls back to the server's message for codes it does not know (or without the details a text needs).

const MESSAGES = {
  unauthorized: () => 'Требуется вход через Cloudflare Access.',
  invalid_token: () => 'Сессия входа недействительна. Войдите заново.',
  access_not_configured: () => 'Cloudflare Access не настроен.',
  user_disabled: (e) => (e.email ? `Доступ для ${e.email} отключён администратором.` : 'Пользователь отключён.'),
  access_denied: (e) => e.email && `Администратор отклонил запрос на доступ для ${e.email}.`,
  access_requests_full: (e) =>
    e.email && `Нет доступа для ${e.email}. Запросов на доступ слишком много: попросите администратора добавить вас вручную.`,
  access_pending: (e) =>
    e.email && `Запрос на доступ для ${e.email} отправлен администратору. Когда он подтвердит, обновите страницу.`,
  forbidden: () => 'Недостаточно прав для этого действия.',
  not_found: () => 'Не найдено: возможно, это уже удалили. Обновите страницу.',
  busy: () => 'Запись одновременно изменяется. Повторите попытку.',
  readonly_field: () => 'Поле computerId изменить нельзя.',
  conflict: () => 'Данные уже изменили. Обновите страницу.',
  invalid_label: (e) => e.maxLength && `Название ключа обязательно, не длиннее ${e.maxLength} символов.`,
  invalid_name: (e) => e.maxLength && `Имя обязательно, не длиннее ${e.maxLength} символов.`,
  invalid_email: () => 'Укажите корректный адрес почты.',
  invalid_role: () => 'Роль должна быть viewer, harvester, editor или admin.',
  user_exists: () => 'Пользователь с такой почтой уже есть.',
  self_lockout: () => 'Нельзя снять с себя роль admin или отключить себя.',
  internal: () => 'Внутренняя ошибка сервера.',
};

// Text of an error answer { error: { code, message }, ...details } for the page.
export function errorText(code, data, status) {
  const translate = MESSAGES[code];
  const details = { ...(data ?? {}), ...(data?.error ?? {}) };
  return (translate && translate(details)) || data?.error?.message || `Облако ответило ошибкой ${status}.`;
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
    throw new ApiError(0, 'network', 'Нет связи с облаком. Проверьте интернет и повторите.');
  }
  if (response.type === 'opaqueredirect' || response.status === 0) {
    throw new ApiError(401, 'session_expired', 'Сессия входа истекла. Обновите страницу, чтобы войти снова.');
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
