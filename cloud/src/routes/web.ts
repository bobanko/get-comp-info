import { Hono, type Context } from 'hono';
import { syncAccessPolicy } from '../access-policy';
import { webAuth } from '../auth';
import { generateApiKey, sha256Hex } from '../crypto';
import {
  getDeviceRow,
  getHistory,
  listDevices,
  patchDevice,
  setDeleted,
  toDevice,
  validateDeviceId,
} from '../devices';
import { fail, isPlainObject, nowIso, readJsonObject, requireRole } from '../http';
import { getCurrentRelease, listReleases, makeCurrent } from '../releases';
import { hasRole, isRole, toUser, type AppEnv, type UserRow } from '../types';
import { writeResponse } from './console';

const MAX_LABEL_LENGTH = 100;
const MAX_NAME_LENGTH = 100;
const EMAIL_PATTERN = /^[^\s@]+@[^\s@]+\.[^\s@]+$/;

export const webApi = new Hono<AppEnv>();

webApi.use('*', webAuth);

webApi.get('/me', (c) => c.json(c.get('user')));

// ---------- Devices ----------

webApi.get('/devices', async (c) => {
  const includeDeleted = c.req.query('deleted') === '1';
  if (includeDeleted) requireRole(c, 'admin');
  return c.json({ devices: await listDevices(c.env.DB, includeDeleted) });
});

async function visibleDevice(c: Context<AppEnv>, id: string) {
  const row = await getDeviceRow(c.env.DB, id);
  // Deleted devices are visible to admins only.
  if (!row || (row.deleted_at !== null && !hasRole(c.get('user'), 'admin'))) {
    fail(404, 'not_found', 'Устройство не найдено.');
  }
  return toDevice(row);
}

webApi.get('/devices/:id', async (c) => {
  return c.json(await visibleDevice(c, validateDeviceId(c.req.param('id'))));
});

webApi.get('/devices/:id/history', async (c) => {
  const id = validateDeviceId(c.req.param('id'));
  await visibleDevice(c, id);
  return c.json({ history: await getHistory(c.env.DB, id) });
});

webApi.patch('/devices/:id', async (c) => {
  requireRole(c, 'editor');
  const id = validateDeviceId(c.req.param('id'));
  const body = await readJsonObject(c);
  if (!Number.isInteger(body.baseRevision) || (body.baseRevision as number) < 1) {
    fail(400, 'invalid_revision', 'baseRevision должен быть целым числом от 1.');
  }
  if (!isPlainObject(body.changes)) {
    fail(400, 'invalid_changes', 'changes должен быть JSON-объектом с изменёнными полями.');
  }
  return writeResponse(c, await patchDevice(c.env.DB, id, body.baseRevision as number, body.changes, c.get('user')));
});

webApi.delete('/devices/:id', async (c) => {
  requireRole(c, 'admin');
  const id = validateDeviceId(c.req.param('id'));
  return c.json(await setDeleted(c.env.DB, id, true, c.get('user')));
});

webApi.post('/devices/:id/restore', async (c) => {
  requireRole(c, 'admin');
  const id = validateDeviceId(c.req.param('id'));
  return c.json(await setDeleted(c.env.DB, id, false, c.get('user')));
});

// ---------- Personal keys ----------

interface KeyRow {
  id: string;
  user_email: string;
  label: string;
  created_at: string;
  last_used_at: string | null;
  revoked_at: string | null;
}

function toKey(row: KeyRow) {
  return {
    id: row.id,
    userEmail: row.user_email,
    label: row.label,
    createdAt: row.created_at,
    lastUsedAt: row.last_used_at,
    revokedAt: row.revoked_at,
  };
}

webApi.get('/keys', async (c) => {
  requireRole(c, 'harvester');
  const all = c.req.query('all') === '1';
  if (all) requireRole(c, 'admin');
  const columns = 'id, user_email, label, created_at, last_used_at, revoked_at';
  const statement = all
    ? c.env.DB.prepare(`SELECT ${columns} FROM api_keys ORDER BY created_at DESC`)
    : c.env.DB.prepare(`SELECT ${columns} FROM api_keys WHERE user_email = ? ORDER BY created_at DESC`).bind(
        c.get('user').email,
      );
  const { results } = await statement.all<KeyRow>();
  return c.json({ keys: results.map(toKey) });
});

webApi.post('/keys', async (c) => {
  requireRole(c, 'harvester');
  const body = await readJsonObject(c);
  const label = typeof body.label === 'string' ? body.label.trim() : '';
  if (label === '' || label.length > MAX_LABEL_LENGTH) {
    fail(400, 'invalid_label', `Название ключа обязательно, не длиннее ${MAX_LABEL_LENGTH} символов.`);
  }
  const key = generateApiKey();
  const row: KeyRow = {
    id: crypto.randomUUID(),
    user_email: c.get('user').email,
    label,
    created_at: nowIso(),
    last_used_at: null,
    revoked_at: null,
  };
  await c.env.DB.prepare('INSERT INTO api_keys (id, user_email, label, key_hash, created_at) VALUES (?, ?, ?, ?, ?)')
    .bind(row.id, row.user_email, row.label, await sha256Hex(key), row.created_at)
    .run();
  // The key itself is returned only once and never stored.
  return c.json({ ...toKey(row), key }, 201);
});

webApi.delete('/keys/:id', async (c) => {
  requireRole(c, 'harvester');
  const user = c.get('user');
  const row = await c.env.DB.prepare(
    'SELECT id, user_email, label, created_at, last_used_at, revoked_at FROM api_keys WHERE id = ?',
  )
    .bind(c.req.param('id'))
    .first<KeyRow>();
  if (!row || (row.user_email !== user.email && !hasRole(user, 'admin'))) {
    fail(404, 'not_found', 'Ключ не найден.');
  }
  if (row.revoked_at === null) {
    row.revoked_at = nowIso();
    await c.env.DB.prepare('UPDATE api_keys SET revoked_at = ? WHERE id = ? AND revoked_at IS NULL')
      .bind(row.revoked_at, row.id)
      .run();
  }
  return c.json(toKey(row));
});

// ---------- Users ----------

webApi.get('/users', async (c) => {
  requireRole(c, 'admin');
  const { results } = await c.env.DB.prepare('SELECT * FROM users ORDER BY email').all<UserRow>();
  return c.json({ users: results.map(toUser) });
});

webApi.post('/users', async (c) => {
  requireRole(c, 'admin');
  const body = await readJsonObject(c);
  const email = typeof body.email === 'string' ? body.email.trim().toLowerCase() : '';
  if (!EMAIL_PATTERN.test(email)) {
    fail(400, 'invalid_email', 'Укажите корректный адрес почты.');
  }
  const name = validateName(body.name);
  if (!isRole(body.role)) {
    fail(400, 'invalid_role', 'Роль должна быть viewer, harvester, editor или admin.');
  }
  const existing = await c.env.DB.prepare('SELECT email FROM users WHERE email = ?').bind(email).first();
  if (existing) {
    fail(409, 'user_exists', 'Пользователь с такой почтой уже есть.');
  }
  const row: UserRow = { email, name, role: body.role, active: 1, created_at: nowIso() };
  await c.env.DB.prepare('INSERT INTO users (email, name, role, active, created_at) VALUES (?, ?, ?, ?, ?)')
    .bind(row.email, row.name, row.role, row.active, row.created_at)
    .run();
  return c.json({ ...toUser(row), access: await syncAccessPolicy(c.env) }, 201);
});

webApi.patch('/users/:email', async (c) => {
  requireRole(c, 'admin');
  const email = c.req.param('email').toLowerCase();
  const body = await readJsonObject(c);
  const row = await c.env.DB.prepare('SELECT * FROM users WHERE email = ?').bind(email).first<UserRow>();
  if (!row) {
    fail(404, 'not_found', 'Пользователь не найден.');
  }
  const wasActive = row.active;
  if (body.name !== undefined) {
    row.name = validateName(body.name);
  }
  if (body.role !== undefined) {
    if (!isRole(body.role)) fail(400, 'invalid_role', 'Роль должна быть viewer, harvester, editor или admin.');
    row.role = body.role;
  }
  if (body.active !== undefined) {
    if (typeof body.active !== 'boolean') fail(400, 'invalid_active', 'active должен быть true или false.');
    row.active = body.active ? 1 : 0;
  }
  // An admin cannot lock themselves out, so at least one active admin always remains.
  if (email === c.get('user').email && (row.role !== 'admin' || row.active !== 1)) {
    fail(400, 'self_lockout', 'Нельзя снять с себя роль admin или отключить себя.');
  }
  await c.env.DB.prepare('UPDATE users SET name = ?, role = ?, active = ? WHERE email = ?')
    .bind(row.name, row.role, row.active, email)
    .run();
  // Only enabling or disabling changes who may pass Cloudflare Access.
  const access = wasActive !== row.active ? await syncAccessPolicy(c.env) : undefined;
  return c.json({ ...toUser(row), access });
});

// ---------- Program releases ----------

webApi.get('/releases', async (c) => {
  requireRole(c, 'admin');
  return c.json({ releases: await listReleases(c.env.DB) });
});

webApi.post('/releases/:version/make-current', async (c) => {
  requireRole(c, 'admin');
  if (!(await makeCurrent(c.env.DB, c.req.param('version')))) fail(404, 'not_found', 'Такой версии программы нет.');
  return c.json({ current: await getCurrentRelease(c.env.DB) });
});

function validateName(value: unknown): string {
  const name = typeof value === 'string' ? value.trim() : '';
  if (name === '' || name.length > MAX_NAME_LENGTH) {
    fail(400, 'invalid_name', `Имя обязательно, не длиннее ${MAX_NAME_LENGTH} символов.`);
  }
  return name;
}
