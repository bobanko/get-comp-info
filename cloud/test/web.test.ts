import { exportJWK, generateKeyPair } from 'jose';
import { describe, expect, it } from 'vitest';
import {
  accessToken,
  addKey,
  addUser,
  consoleRequest,
  deviceData,
  harvesterKey,
  unique,
  webRequest,
  webRequestWithToken,
} from './helpers';

async function createDevice(key: string, extra: Record<string, unknown> = {}): Promise<string> {
  const id = unique('dev');
  await consoleRequest(key, 'PUT', `/devices/${id}`, { baseRevision: null, data: deviceData(id, extra) });
  return id;
}

describe('web authentication', () => {
  it('requires a valid Cloudflare Access token', async () => {
    const email = await addUser('viewer');
    expect((await webRequest(null, 'GET', '/me')).status).toBe(401);
    expect((await webRequestWithToken('garbage', 'GET', '/me')).status).toBe(401);
    expect((await webRequestWithToken(await accessToken(email, { audience: 'other' }), 'GET', '/me')).status).toBe(401);
    expect(
      (await webRequestWithToken(await accessToken(email, { issuer: 'https://evil.example.com' }), 'GET', '/me')).status,
    ).toBe(401);

    const { privateKey } = await generateKeyPair('RS256', { extractable: true });
    const foreignJwk = { ...(await exportJWK(privateKey)), kid: 'test' };
    expect((await webRequestWithToken(await accessToken(email, { privateJwk: foreignJwk }), 'GET', '/me')).status).toBe(401);
  });

  it('accepts the CF_Authorization cookie', async () => {
    const email = await addUser('viewer');
    const { exports } = await import('cloudflare:workers');
    const response = await exports.default.fetch(
      new Request('https://comp-info.test/api/web/me', {
        headers: { Cookie: `CF_Authorization=${await accessToken(email)}` },
      }),
    );
    expect(response.status).toBe(200);
  });

  it('uses the development identity only for requests to localhost', async () => {
    const { exports } = await import('cloudflare:workers');
    // DEV_AUTH_EMAIL is set in the test environment, yet the real domain still requires Access.
    const remote = await exports.default.fetch(new Request('https://comp-info.test/api/web/me'));
    expect(remote.status).toBe(401);
    await addUser('viewer', { email: 'dev@example.com' });
    const local = await exports.default.fetch(new Request('http://localhost:8787/api/web/me'));
    expect(local.status).toBe(200);
    expect(await local.json()).toMatchObject({ email: 'dev@example.com' });
  });

  it('rejects unknown and disabled users', async () => {
    expect((await webRequest('stranger@example.com', 'GET', '/me')).status).toBe(403);
    const disabled = await addUser('admin', { active: false });
    expect((await webRequest(disabled, 'GET', '/me')).status).toBe(403);
  });

  it('matches the email case-insensitively', async () => {
    const email = await addUser('viewer');
    const response = await webRequest(email.toUpperCase(), 'GET', '/me');
    expect(response.status).toBe(200);
    expect(response.body).toMatchObject({ email, role: 'viewer' });
  });
});

describe('web devices', () => {
  it('lets every role read devices and history', async () => {
    const { key } = await harvesterKey();
    const id = await createDevice(key);
    const viewer = await addUser('viewer');
    const list = await webRequest(viewer, 'GET', '/devices');
    expect(list.body.devices.some((d: { id: string }) => d.id === id)).toBe(true);
    expect((await webRequest(viewer, 'GET', `/devices/${id}`)).body).toMatchObject({ id, revision: 1 });
    expect((await webRequest(viewer, 'GET', `/devices/${unique('missing')}`)).status).toBe(404);
  });

  it('allows editing only for editors and admins', async () => {
    const { key } = await harvesterKey();
    const id = await createDevice(key);
    const harvester = await addUser('harvester');
    const editor = await addUser('editor');
    const patch = { baseRevision: 1, changes: { comment: 'Проверено' } };

    expect((await webRequest(harvester, 'PATCH', `/devices/${id}`, patch)).status).toBe(403);

    const edited = await webRequest(editor, 'PATCH', `/devices/${id}`, patch);
    expect(edited.status).toBe(200);
    expect(edited.body).toMatchObject({ revision: 2, changed: true, device: { data: { comment: 'Проверено' } } });

    const stale = await webRequest(editor, 'PATCH', `/devices/${id}`, { baseRevision: 1, changes: { comment: 'x' } });
    expect(stale.status).toBe(409);
    expect(stale.body.current.revision).toBe(2);

    const history = await webRequest(editor, 'GET', `/devices/${id}/history`);
    expect(history.body.history[0]).toMatchObject({ revision: 2, action: 'update', source: 'web', changedBy: editor });
  });

  it('protects computerId and validates patches', async () => {
    const { key } = await harvesterKey();
    const id = await createDevice(key);
    const editor = await addUser('editor');
    expect((await webRequest(editor, 'PATCH', `/devices/${id}`, { baseRevision: 1, changes: { computerId: 'x' } })).status).toBe(400);
    expect((await webRequest(editor, 'PATCH', `/devices/${id}`, { baseRevision: 1, changes: 'x' })).status).toBe(400);
    expect((await webRequest(editor, 'PATCH', `/devices/${id}`, { changes: {} })).status).toBe(400);
  });

  it('lets only admins delete, see deleted devices and restore them', async () => {
    const { key } = await harvesterKey();
    const id = await createDevice(key);
    const editor = await addUser('editor');
    const admin = await addUser('admin');

    expect((await webRequest(editor, 'DELETE', `/devices/${id}`)).status).toBe(403);
    expect((await webRequest(admin, 'DELETE', `/devices/${id}`)).body.deleted).toBe(true);
    // Deleting again is harmless.
    expect((await webRequest(admin, 'DELETE', `/devices/${id}`)).body.revision).toBe(2);

    expect((await webRequest(editor, 'GET', `/devices/${id}`)).status).toBe(404);
    expect((await webRequest(editor, 'GET', '/devices?deleted=1')).status).toBe(403);
    const all = await webRequest(admin, 'GET', '/devices?deleted=1');
    expect(all.body.devices.some((d: { id: string; deleted: boolean }) => d.id === id && d.deleted)).toBe(true);
    const active = await webRequest(admin, 'GET', '/devices');
    expect(active.body.devices.some((d: { id: string }) => d.id === id)).toBe(false);

    const restored = await webRequest(admin, 'POST', `/devices/${id}/restore`);
    expect(restored.body).toMatchObject({ deleted: false, revision: 3 });
    const actions = (await webRequest(admin, 'GET', `/devices/${id}/history`)).body.history.map(
      (h: { action: string }) => h.action,
    );
    expect(actions).toEqual(['restore', 'delete', 'create']);
  });
});

describe('personal keys', () => {
  it('is not available to viewers', async () => {
    const viewer = await addUser('viewer');
    expect((await webRequest(viewer, 'POST', '/keys', { label: 'x' })).status).toBe(403);
    expect((await webRequest(viewer, 'GET', '/keys')).status).toBe(403);
  });

  it('creates a working key shown once, lists and revokes it', async () => {
    const harvester = await addUser('harvester');
    const created = await webRequest(harvester, 'POST', '/keys', { label: '  Флешка Сергея ' });
    expect(created.status).toBe(201);
    expect(created.body.key).toMatch(/^ci_[A-Za-z0-9_-]{43}$/);
    expect(created.body.label).toBe('Флешка Сергея');

    const me = await consoleRequest(created.body.key, 'GET', '/me');
    expect(me.body).toMatchObject({ email: harvester, keyLabel: 'Флешка Сергея' });

    const list = await webRequest(harvester, 'GET', '/keys');
    expect(list.body.keys).toHaveLength(1);
    expect(list.body.keys[0]).not.toHaveProperty('key');
    expect(list.body.keys[0].lastUsedAt).not.toBeNull();

    const revoked = await webRequest(harvester, 'DELETE', `/keys/${created.body.id}`);
    expect(revoked.body.revokedAt).not.toBeNull();
    expect((await consoleRequest(created.body.key, 'GET', '/me')).status).toBe(401);
  });

  it('validates the label', async () => {
    const harvester = await addUser('harvester');
    expect((await webRequest(harvester, 'POST', '/keys', { label: '  ' })).status).toBe(400);
    expect((await webRequest(harvester, 'POST', '/keys', { label: 'x'.repeat(101) })).status).toBe(400);
  });

  it("does not let users touch other users' keys, except admins", async () => {
    const owner = await addUser('harvester');
    const other = await addUser('editor');
    const admin = await addUser('admin');
    const created = (await webRequest(owner, 'POST', '/keys', { label: 'Owner key' })).body;

    expect((await webRequest(other, 'DELETE', `/keys/${created.id}`)).status).toBe(404);
    expect((await webRequest(other, 'GET', '/keys?all=1')).status).toBe(403);

    const all = await webRequest(admin, 'GET', '/keys?all=1');
    expect(all.body.keys.some((k: { id: string }) => k.id === created.id)).toBe(true);
    expect((await webRequest(admin, 'DELETE', `/keys/${created.id}`)).body.revokedAt).not.toBeNull();
  });
});

describe('users', () => {
  it('is managed by admins only', async () => {
    const editor = await addUser('editor');
    expect((await webRequest(editor, 'GET', '/users')).status).toBe(403);
    expect((await webRequest(editor, 'POST', '/users', { email: 'a@b.cd', name: 'A', role: 'viewer' })).status).toBe(403);
  });

  it('creates users and rejects duplicates and bad input', async () => {
    const admin = await addUser('admin');
    const email = `${unique('new')}@Example.com`;
    const created = await webRequest(admin, 'POST', '/users', { email, name: 'Новый', role: 'harvester' });
    expect(created.status).toBe(201);
    expect(created.body).toMatchObject({ email: email.toLowerCase(), role: 'harvester', active: true });

    expect((await webRequest(admin, 'POST', '/users', { email, name: 'Again', role: 'viewer' })).status).toBe(409);
    expect((await webRequest(admin, 'POST', '/users', { email: 'bad', name: 'X', role: 'viewer' })).status).toBe(400);
    expect((await webRequest(admin, 'POST', '/users', { email: 'x@y.zz', name: 'X', role: 'owner' })).status).toBe(400);

    // The new user can sign in right away.
    expect((await webRequest(email, 'GET', '/me')).body.role).toBe('harvester');
  });

  it('applies role changes to existing keys immediately', async () => {
    const admin = await addUser('admin');
    const { email, key } = await harvesterKey();
    expect((await consoleRequest(key, 'GET', '/me')).status).toBe(200);

    const demoted = await webRequest(admin, 'PATCH', `/users/${encodeURIComponent(email)}`, { role: 'viewer' });
    expect(demoted.body.role).toBe('viewer');
    expect((await consoleRequest(key, 'GET', '/me')).status).toBe(403);

    await webRequest(admin, 'PATCH', `/users/${encodeURIComponent(email)}`, { role: 'editor' });
    expect((await consoleRequest(key, 'GET', '/me')).body.role).toBe('editor');

    await webRequest(admin, 'PATCH', `/users/${encodeURIComponent(email)}`, { active: false });
    expect((await consoleRequest(key, 'GET', '/me')).status).toBe(403);
    expect((await webRequest(email, 'GET', '/me')).status).toBe(403);
  });

  it('does not let an admin lock themselves out', async () => {
    const admin = await addUser('admin');
    const path = `/users/${encodeURIComponent(admin)}`;
    expect((await webRequest(admin, 'PATCH', path, { role: 'editor' })).body.error.code).toBe('self_lockout');
    expect((await webRequest(admin, 'PATCH', path, { active: false })).status).toBe(400);
    expect((await webRequest(admin, 'PATCH', path, { name: 'Главный' })).body.name).toBe('Главный');
  });
});

describe('routing', () => {
  it('returns JSON 404 for unknown API paths', async () => {
    const { key } = await harvesterKey();
    const response = await consoleRequest(key, 'GET', '/nothing');
    expect(response.status).toBe(404);
    const unknownRoot = await webRequest(null, 'GET', '/../../api/unknown');
    expect([401, 404]).toContain(unknownRoot.status);
  });
});
