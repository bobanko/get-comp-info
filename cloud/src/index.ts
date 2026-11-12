import { Hono } from 'hono';
import { HTTPException } from 'hono/http-exception';
import { fail } from './http';
import { releaseApi, suspectorApi, suspectorReleaseApi } from './releases';
import { consoleApi } from './routes/console';
import { webApi } from './routes/web';
import type { AppEnv } from './types';

const app = new Hono<AppEnv>();

app.route('/api/client', consoleApi);
app.route('/api/web', webApi);
app.route('/api/release', releaseApi);
app.route('/api/suspector', suspectorApi);
app.route('/api/suspector-release', suspectorReleaseApi);

app.all('/api/*', () => fail(404, 'not_found', 'Unknown API path.'));

// Everything outside /api is the web interface from ./public.
app.all('*', (c) => c.env.ASSETS.fetch(c.req.raw));

app.onError((error, c) => {
  if (error instanceof HTTPException) {
    return error.getResponse();
  }
  console.error(error);
  return c.json({ error: { code: 'internal', message: 'Internal server error.' } }, 500);
});

export default app;
