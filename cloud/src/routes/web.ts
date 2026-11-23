import { Hono, type Context } from 'hono';
import { audit, readJournal } from '../audit';
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
import { buildDownload, describeDownloads, getCurrentRelease, getCurrentSuspectorRelease, isDownloadKind, listReleases, makeCurrent } from '../releases';
import { createItem, deleteItem, itemChanges, listItems, parseItemId, updateItem, validateItem } from '../items';
import { checkPersonRefs, createPerson, deletePerson, devicesOfPerson, listPeople, parsePersonId, updatePerson, validatePerson } from '../people';
import { getSetting, getSettingHistory, NETWORK_KEY, putSetting, validateNetworkSettings, type NetworkSettings } from '../settings';
import { TRIGGER_WORDS_KEY, validateTriggerWords, type TriggerWordsValue } from '../trigger-words';
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
    fail(404, 'not_found', 'Device not found.');
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
    fail(400, 'invalid_revision', 'baseRevision must be an integer from 1.');
  }
  if (!isPlainObject(body.changes)) {
    fail(400, 'invalid_changes', 'changes must be a JSON object with the changed fields.');
  }
  await checkPersonRefs(c.env.DB, body.changes);
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
    fail(400, 'invalid_label', `A key name is required, up to ${MAX_LABEL_LENGTH} characters.`, { maxLength: MAX_LABEL_LENGTH });
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
  await audit(c, 'key.create', row.id, { label: row.label, owner: row.user_email });
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
    fail(404, 'not_found', 'Key not found.');
  }
  if (row.revoked_at === null) {
    row.revoked_at = nowIso();
    await c.env.DB.prepare('UPDATE api_keys SET revoked_at = ? WHERE id = ? AND revoked_at IS NULL')
      .bind(row.revoked_at, row.id)
      .run();
    await audit(c, 'key.revoke', row.id, { label: row.label, owner: row.user_email });
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
    fail(400, 'invalid_email', 'Enter a valid email address.');
  }
  const name = validateName(body.name);
  if (!isRole(body.role)) {
    fail(400, 'invalid_role', 'The role must be viewer, harvester, editor or admin.');
  }
  const existing = await c.env.DB.prepare('SELECT email FROM users WHERE email = ?').bind(email).first();
  if (existing) {
    fail(409, 'user_exists', 'A user with this email already exists.');
  }
  const row: UserRow = { email, name, role: body.role, active: 1, created_at: nowIso() };
  // Adding a user also approves their access request, if there was one.
  await c.env.DB.batch([
    c.env.DB.prepare('INSERT INTO users (email, name, role, active, created_at) VALUES (?, ?, ?, ?, ?)').bind(
      row.email,
      row.name,
      row.role,
      row.active,
      row.created_at,
    ),
    c.env.DB.prepare('DELETE FROM access_requests WHERE email = ?').bind(row.email),
  ]);
  await audit(c, 'user.create', row.email, { name: row.name, role: row.role });
  return c.json(toUser(row), 201);
});

webApi.patch('/users/:email', async (c) => {
  requireRole(c, 'admin');
  const email = c.req.param('email').toLowerCase();
  const body = await readJsonObject(c);
  const row = await c.env.DB.prepare('SELECT * FROM users WHERE email = ?').bind(email).first<UserRow>();
  if (!row) {
    fail(404, 'not_found', 'User not found.');
  }
  const before = { name: row.name, role: row.role, active: row.active === 1 };
  if (body.name !== undefined) {
    row.name = validateName(body.name);
  }
  if (body.role !== undefined) {
    if (!isRole(body.role)) fail(400, 'invalid_role', 'The role must be viewer, harvester, editor or admin.');
    row.role = body.role;
  }
  if (body.active !== undefined) {
    if (typeof body.active !== 'boolean') fail(400, 'invalid_active', 'active must be true or false.');
    row.active = body.active ? 1 : 0;
  }
  // An admin cannot lock themselves out, so at least one active admin always remains.
  if (email === c.get('user').email && (row.role !== 'admin' || row.active !== 1)) {
    fail(400, 'self_lockout', 'You cannot remove your own admin role or disable yourself.');
  }
  await c.env.DB.prepare('UPDATE users SET name = ?, role = ?, active = ? WHERE email = ?')
    .bind(row.name, row.role, row.active, email)
    .run();
  // Only what changed, as [before, after].
  const after = { name: row.name, role: row.role, active: row.active === 1 };
  const changes: Record<string, unknown> = {};
  for (const field of ['name', 'role', 'active'] as const) {
    if (before[field] !== after[field]) changes[field] = [before[field], after[field]];
  }
  if (Object.keys(changes).length > 0) await audit(c, 'user.update', email, changes);
  return c.json(toUser(row));
});

// ---------- Access requests ----------
// People who signed in through Cloudflare Access but are not users yet.

interface AccessRequestRow {
  email: string;
  requested_at: string;
  last_seen_at: string;
}

webApi.get('/access-requests', async (c) => {
  requireRole(c, 'admin');
  const { results } = await c.env.DB.prepare(
    'SELECT email, requested_at, last_seen_at FROM access_requests WHERE rejected_at IS NULL ORDER BY requested_at DESC',
  ).all<AccessRequestRow>();
  return c.json({
    requests: results.map((row) => ({ email: row.email, requestedAt: row.requested_at, lastSeenAt: row.last_seen_at })),
  });
});

// Rejects a request: the person keeps seeing "access denied" until an admin adds them as a user.
webApi.delete('/access-requests/:email', async (c) => {
  requireRole(c, 'admin');
  const email = c.req.param('email').toLowerCase();
  const result = await c.env.DB.prepare(
    'UPDATE access_requests SET rejected_at = ?, rejected_by = ? WHERE email = ? AND rejected_at IS NULL',
  )
    .bind(nowIso(), c.get('user').email, email)
    .run();
  if (result.meta.changes === 0) fail(404, 'not_found', 'No such access request.');
  await audit(c, 'access.reject', email);
  return c.json({ email, rejected: true });
});

// ---------- People ----------
// Everyone who sees devices sees the people they are linked to; editors add and change them, admins delete.

webApi.get('/people', async (c) => {
  const includeDeleted = c.req.query('deleted') === '1';
  if (includeDeleted) requireRole(c, 'admin');
  return c.json({ people: await listPeople(c.env.DB, includeDeleted) });
});

webApi.post('/people', async (c) => {
  requireRole(c, 'editor');
  const value = validatePerson(await readJsonObject(c), false) as { name: string; note: string };
  const person = await createPerson(c.env.DB, value, c.get('user'));
  await audit(c, 'person.create', String(person.id), { name: person.name, note: person.note });
  return c.json({ person }, 201);
});

webApi.patch('/people/:id', async (c) => {
  requireRole(c, 'editor');
  const id = parsePersonId(c.req.param('id'));
  const { before, after } = await updatePerson(c.env.DB, id, validatePerson(await readJsonObject(c), true), c.get('user'));
  const changes: Record<string, [string, string]> = {};
  if (before.name !== after.name) changes.name = [before.name, after.name];
  if (before.note !== after.note) changes.note = [before.note, after.note];
  if (Object.keys(changes).length > 0) await audit(c, 'person.update', String(id), changes);
  return c.json({ person: after });
});

webApi.get('/people/:id/devices', async (c) => {
  return c.json({ devices: await devicesOfPerson(c.env.DB, parsePersonId(c.req.param('id'))) });
});

webApi.delete('/people/:id', async (c) => {
  requireRole(c, 'admin');
  const person = await deletePerson(c.env.DB, parsePersonId(c.req.param('id')), c.get('user'));
  await audit(c, 'person.delete', String(person.id), { name: person.name });
  return c.json({ person });
});

// ---------- Property (not computers) ----------

webApi.get('/items', async (c) => {
  const includeDeleted = c.req.query('deleted') === '1';
  if (includeDeleted) requireRole(c, 'admin');
  return c.json({ items: await listItems(c.env.DB, includeDeleted) });
});

webApi.post('/items', async (c) => {
  requireRole(c, 'editor');
  const item = await createItem(c.env.DB, await validateItem(c.env.DB, await readJsonObject(c), false), c.get('user'));
  await audit(c, 'item.create', String(item.id), { type: item.type, model: item.model, serial: item.serial, ownerId: item.ownerId });
  return c.json({ item }, 201);
});

webApi.patch('/items/:id', async (c) => {
  requireRole(c, 'editor');
  const id = parseItemId(c.req.param('id'));
  const value = await validateItem(c.env.DB, await readJsonObject(c), true);
  const { before, after } = await updateItem(c.env.DB, id, value, c.get('user'));
  const changes = itemChanges(before, after);
  if (Object.keys(changes).length > 0) await audit(c, 'item.update', String(id), { type: after.type, ...changes });
  return c.json({ item: after });
});

webApi.delete('/items/:id', async (c) => {
  requireRole(c, 'admin');
  const item = await deleteItem(c.env.DB, parseItemId(c.req.param('id')), c.get('user'));
  await audit(c, 'item.delete', String(item.id), { type: item.type, model: item.model, serial: item.serial });
  return c.json({ item });
});

// ---------- Network settings ----------

webApi.get('/settings/network', async (c) => {
  requireRole(c, 'admin');
  return c.json({ setting: await getSetting<NetworkSettings>(c.env.DB, NETWORK_KEY) });
});

webApi.get('/settings/network/history', async (c) => {
  requireRole(c, 'admin');
  return c.json({ history: await getSettingHistory(c.env.DB, NETWORK_KEY) });
});

webApi.put('/settings/network', async (c) => {
  requireRole(c, 'admin');
  const body = await readJsonObject(c);
  const baseRevision = body.baseRevision ?? null;
  if (baseRevision !== null && !(Number.isInteger(baseRevision) && (baseRevision as number) >= 1)) {
    fail(400, 'invalid_revision', 'baseRevision must be null or an integer from 1.');
  }
  const value = validateNetworkSettings(body.value);
  const result = await putSetting(c.env.DB, NETWORK_KEY, baseRevision as number | null, value, c.get('user'));
  if (result.status === 'conflict') {
    return c.json(
      { error: { code: 'conflict', message: 'The network settings were already changed. Reload the page.' }, current: result.current },
      409,
    );
  }
  return c.json({ setting: result.record });
});

// ---------- SUSpector trigger words ----------

webApi.get('/settings/trigger-words', async (c) => {
  requireRole(c, 'admin');
  return c.json({ setting: await getSetting<TriggerWordsValue>(c.env.DB, TRIGGER_WORDS_KEY) });
});

webApi.get('/settings/trigger-words/history', async (c) => {
  requireRole(c, 'admin');
  return c.json({ history: await getSettingHistory(c.env.DB, TRIGGER_WORDS_KEY) });
});

webApi.put('/settings/trigger-words', async (c) => {
  requireRole(c, 'admin');
  const body = await readJsonObject(c);
  const baseRevision = body.baseRevision ?? null;
  if (baseRevision !== null && !(Number.isInteger(baseRevision) && (baseRevision as number) >= 1)) {
    fail(400, 'invalid_revision', 'baseRevision must be null or an integer from 1.');
  }
  const value = validateTriggerWords(body.value);
  const result = await putSetting(c.env.DB, TRIGGER_WORDS_KEY, baseRevision as number | null, value, c.get('user'));
  if (result.status === 'conflict') {
    return c.json(
      { error: { code: 'conflict', message: 'The trigger words were already changed. Reload the page.' }, current: result.current },
      409,
    );
  }
  return c.json({ setting: result.record });
});

// ---------- Downloads: the current release for anyone signed in ----------

webApi.get('/downloads', async (c) => c.json(await describeDownloads(c.env.DB)));

webApi.get('/downloads/:kind', async (c) => {
  const kind = c.req.param('kind');
  if (!isDownloadKind(kind)) fail(404, 'not_found', 'Unknown download.');
  const download = await buildDownload(c.env.DB, kind);
  return new Response(download.bytes, {
    headers: {
      'Content-Type': 'application/zip',
      'Content-Length': String(download.bytes.byteLength),
      'Content-Disposition': `attachment; filename="${download.name}"`,
      'Cache-Control': 'no-store',
    },
  });
});

// ---------- Program releases ----------

webApi.get('/releases', async (c) => {
  requireRole(c, 'admin');
  return c.json({ releases: await listReleases(c.env.DB) });
});

webApi.post('/releases/:version/make-current', async (c) => {
  requireRole(c, 'admin');
  if (!(await makeCurrent(c.env.DB, c.req.param('version')))) fail(404, 'not_found', 'No such program version.');
  await audit(c, 'release.current', c.req.param('version'));
  return c.json({ current: await getCurrentRelease(c.env.DB) });
});

webApi.get('/suspector-releases', async (c) => {
  requireRole(c, 'admin');
  return c.json({ releases: await listReleases(c.env.DB, 'suspector_releases') });
});

webApi.post('/suspector-releases/:version/make-current', async (c) => {
  requireRole(c, 'admin');
  if (!(await makeCurrent(c.env.DB, c.req.param('version'), 'suspector_releases'))) fail(404, 'not_found', 'No such SUSpector version.');
  await audit(c, 'suspector-release.current', c.req.param('version'));
  return c.json({ current: await getCurrentSuspectorRelease(c.env.DB) });
});

// ---------- Journal: who did what, newest first ----------

webApi.get('/audit', async (c) => {
  requireRole(c, 'admin');
  return c.json(await readJournal(c.env.DB, { category: c.req.query('category'), cursor: c.req.query('cursor') }));
});

function validateName(value: unknown): string {
  const name = typeof value === 'string' ? value.trim() : '';
  if (name === '' || name.length > MAX_NAME_LENGTH) {
    fail(400, 'invalid_name', `A name is required, up to ${MAX_NAME_LENGTH} characters.`, { maxLength: MAX_NAME_LENGTH });
  }
  return name;
}
