import { Hono, type Context } from 'hono';
import { consoleAuth } from '../auth';
import { listChanges, normalizeData, putDevice, validateDeviceId, type WriteResult } from '../devices';
import { fail, readJsonObject } from '../http';
import type { AppEnv } from '../types';

const DEFAULT_PAGE = 500;
const MAX_PAGE = 1000;

export const consoleApi = new Hono<AppEnv>();

consoleApi.use('*', consoleAuth);

consoleApi.get('/me', (c) => {
  const user = c.get('user');
  return c.json({ email: user.email, name: user.name, role: user.role, keyLabel: c.get('apiKey')?.label });
});

consoleApi.get('/devices', async (c) => {
  const since = parseNonNegativeInt(c.req.query('since') ?? '0', 'since');
  const limit = Math.min(parseNonNegativeInt(c.req.query('limit') ?? String(DEFAULT_PAGE), 'limit') || DEFAULT_PAGE, MAX_PAGE);
  return c.json(await listChanges(c.env.DB, since, limit));
});

consoleApi.put('/devices/:id', async (c) => {
  const id = validateDeviceId(c.req.param('id'));
  const body = await readJsonObject(c);
  const baseRevision = body.baseRevision ?? null;
  if (baseRevision !== null && !(Number.isInteger(baseRevision) && (baseRevision as number) >= 1)) {
    fail(400, 'invalid_revision', 'baseRevision должен быть null или целым числом от 1.');
  }
  if (body.restore !== undefined && typeof body.restore !== 'boolean') {
    fail(400, 'invalid_restore', 'restore должен быть true или false.');
  }
  const data = normalizeData(id, body.data);
  const result = await putDevice(c.env.DB, id, baseRevision as number | null, data, {
    restore: body.restore === true,
    user: c.get('user'),
    source: 'console',
  });
  return writeResponse(c, result);
});

export function writeResponse(c: Context<AppEnv>, result: WriteResult): Response {
  switch (result.status) {
    case 'ok':
      return c.json({ revision: result.device.revision, changed: result.changed, device: result.device });
    case 'conflict':
      return c.json(
        { error: { code: 'conflict', message: 'Запись уже изменили в облаке.' }, current: result.current },
        409,
      );
    case 'deleted':
      return c.json(
        { error: { code: 'deleted', message: 'Запись удалена в облаке.' }, current: result.current },
        410,
      );
  }
}

function parseNonNegativeInt(value: string, name: string): number {
  if (!/^\d{1,15}$/.test(value)) {
    fail(400, 'invalid_query', `Параметр ${name} должен быть неотрицательным целым числом.`);
  }
  return Number(value);
}
