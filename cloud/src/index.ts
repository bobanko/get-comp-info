import { Hono } from 'hono';
import { HTTPException } from 'hono/http-exception';
import { fail } from './http';
import { consoleApi } from './routes/console';
import { webApi } from './routes/web';
import type { AppEnv } from './types';

const app = new Hono<AppEnv>();

app.route('/api/client', consoleApi);
app.route('/api/web', webApi);

app.all('/api/*', () => fail(404, 'not_found', 'Неизвестный адрес API.'));

// Everything outside /api is the web interface from ./public.
app.all('*', (c) => c.env.ASSETS.fetch(c.req.raw));

app.onError((error, c) => {
  if (error instanceof HTTPException) {
    return error.getResponse();
  }
  console.error(error);
  return c.json({ error: { code: 'internal', message: 'Внутренняя ошибка сервера.' } }, 500);
});

export default app;
