import { Hono, type Context } from 'hono';
import { consoleAuth } from '../auth';
import { listChanges, normalizeData, putDevice, validateDeviceId, type WriteResult } from '../devices';
import { fail, readJsonObject } from '../http';
import { getCurrentRelease, getReleasePackage } from '../releases';
import { getSetting, NETWORK_KEY, type NetworkSettings } from '../settings';
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

consoleApi.get('/program/latest', async (c) => {
  const release = await getCurrentRelease(c.env.DB);
  if (!release) fail(404, 'no_release', 'В облаке ещё нет опубликованных версий программы.');
  return c.json(release);
});

// Network profiles cached by the console for setting up LAN adapters offline.
consoleApi.get('/settings/network', async (c) => {
  const record = await getSetting<NetworkSettings>(c.env.DB, NETWORK_KEY);
  if (!record) fail(404, 'no_settings', 'Настройки сети в облаке ещё не заданы.');
  return c.json(record);
});

consoleApi.get('/program/:version/package', async (c) => {
  const found = await getReleasePackage(c.env.DB, c.req.param('version'));
  if (!found) fail(404, 'not_found', 'Такой версии программы нет.');
  return new Response(found.bytes, {
    headers: {
      'Content-Type': 'application/zip',
      'Content-Length': String(found.bytes.byteLength),
      'X-Content-SHA256': found.sha256,
    },
  });
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
