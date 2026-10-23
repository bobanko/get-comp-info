// Calls to /api/web. Errors carry the server's code and Russian message.

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
    throw new ApiError(
      response.status,
      data?.error?.code ?? `http_${response.status}`,
      data?.error?.message ?? `Облако ответило ошибкой ${response.status}.`,
      data,
    );
  }
  return data;
}
