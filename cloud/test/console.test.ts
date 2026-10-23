import { describe, expect, it } from 'vitest';
import {
  addKey,
  addUser,
  consoleRequest,
  deviceData,
  harvesterKey,
  unique,
  webRequest,
} from './helpers';

describe('console authentication', () => {
  it('rejects requests without a key or with an unknown key', async () => {
    expect((await consoleRequest(null, 'GET', '/me')).status).toBe(401);
    const response = await consoleRequest('ci_unknown', 'GET', '/me');
    expect(response.status).toBe(401);
    expect(response.body.error.code).toBe('invalid_key');
  });

  it('rejects viewers and disabled users', async () => {
    const viewerKey = await addKey(await addUser('viewer'));
    expect((await consoleRequest(viewerKey, 'GET', '/me')).status).toBe(403);
    const disabledKey = await addKey(await addUser('editor', { active: false }));
    expect((await consoleRequest(disabledKey, 'GET', '/me')).body.error.code).toBe('user_disabled');
  });

  it('returns the user, role and key label', async () => {
    const email = await addUser('harvester');
    const key = await addKey(email, 'Флешка 1');
    const response = await consoleRequest(key, 'GET', '/me');
    expect(response.status).toBe(200);
    expect(response.body).toMatchObject({ email, role: 'harvester', keyLabel: 'Флешка 1' });
  });
});

describe('console upload', () => {
  it('creates a record, strips deprecated fields and writes history', async () => {
    const { key, email } = await harvesterKey();
    const id = unique('dev');
    const data = deviceData(id, { addedToExternalDatabase: true, addedToExternalDatabaseAt: '2026-08-28' });
    const response = await consoleRequest(key, 'PUT', `/devices/${id}`, { baseRevision: null, data });
    expect(response.status).toBe(200);
    expect(response.body).toMatchObject({ revision: 1, changed: true });
    expect(response.body.device.data).not.toHaveProperty('addedToExternalDatabase');
    expect(response.body.device.data).not.toHaveProperty('addedToExternalDatabaseAt');
    expect(response.body.device.updatedBy).toBe(email);

    const editor = await addUser('editor');
    const history = await webRequest(editor, 'GET', `/devices/${id}/history`);
    expect(history.body.history).toHaveLength(1);
    expect(history.body.history[0]).toMatchObject({ revision: 1, action: 'create', source: 'console', changedBy: email });
  });

  it('does not create a revision when only key order differs', async () => {
    const { key } = await harvesterKey();
    const id = unique('dev');
    const data = deviceData(id);
    await consoleRequest(key, 'PUT', `/devices/${id}`, { baseRevision: null, data });
    const reordered = Object.fromEntries(Object.entries(data).reverse());
    const response = await consoleRequest(key, 'PUT', `/devices/${id}`, { baseRevision: 1, data: reordered });
    expect(response.body).toMatchObject({ revision: 1, changed: false });
  });

  it('accepts identical content even with a stale base revision', async () => {
    const { key } = await harvesterKey();
    const id = unique('dev');
    await consoleRequest(key, 'PUT', `/devices/${id}`, { baseRevision: null, data: deviceData(id) });
    await consoleRequest(key, 'PUT', `/devices/${id}`, { baseRevision: 1, data: deviceData(id, { ip: '10.0.0.2' }) });
    const response = await consoleRequest(key, 'PUT', `/devices/${id}`, {
      baseRevision: 1,
      data: deviceData(id, { ip: '10.0.0.2' }),
    });
    expect(response.body).toMatchObject({ revision: 2, changed: false });
  });

  it('updates on a matching base revision and reports conflicts otherwise', async () => {
    const { key } = await harvesterKey();
    const id = unique('dev');
    await consoleRequest(key, 'PUT', `/devices/${id}`, { baseRevision: null, data: deviceData(id) });

    const update = await consoleRequest(key, 'PUT', `/devices/${id}`, {
      baseRevision: 1,
      data: deviceData(id, { comment: 'cloud' }),
    });
    expect(update.body).toMatchObject({ revision: 2, changed: true });

    const stale = await consoleRequest(key, 'PUT', `/devices/${id}`, {
      baseRevision: 1,
      data: deviceData(id, { comment: 'flash' }),
    });
    expect(stale.status).toBe(409);
    expect(stale.body.error.code).toBe('conflict');
    expect(stale.body.current).toMatchObject({ revision: 2, data: { comment: 'cloud' } });

    const recreated = await consoleRequest(key, 'PUT', `/devices/${id}`, {
      baseRevision: null,
      data: deviceData(id, { comment: 'other copy' }),
    });
    expect(recreated.status).toBe(409);
  });

  it('accepts legacy records without computerId and rejects mismatching ids', async () => {
    const { key } = await harvesterKey();
    const legacyId = unique('SERIAL');
    const legacy = await consoleRequest(key, 'PUT', `/devices/${legacyId}`, {
      baseRevision: null,
      data: { hostname: 'OLD-PC', serial: legacyId },
    });
    expect(legacy.status).toBe(200);

    const mismatch = await consoleRequest(key, 'PUT', `/devices/${unique('dev')}`, {
      baseRevision: null,
      data: deviceData('another-id'),
    });
    expect(mismatch.status).toBe(400);
    expect(mismatch.body.error.code).toBe('id_mismatch');
  });

  it('validates the request body', async () => {
    const { key } = await harvesterKey();
    const id = unique('dev');
    expect((await consoleRequest(key, 'PUT', `/devices/${id}`, '[1]')).status).toBe(400);
    expect((await consoleRequest(key, 'PUT', `/devices/${id}`, 'not json')).status).toBe(400);
    expect((await consoleRequest(key, 'PUT', `/devices/${id}`, { baseRevision: 0, data: {} })).status).toBe(400);
    expect((await consoleRequest(key, 'PUT', `/devices/${id}`, { baseRevision: null, data: [] })).status).toBe(400);
    const huge = deviceData(id, { comment: 'x'.repeat(70 * 1024) });
    expect((await consoleRequest(key, 'PUT', `/devices/${id}`, { baseRevision: null, data: huge })).status).toBe(413);
  });

  it('returns 410 for deleted records and restores them on request', async () => {
    const { key } = await harvesterKey();
    const admin = await addUser('admin');
    const id = unique('dev');
    await consoleRequest(key, 'PUT', `/devices/${id}`, { baseRevision: null, data: deviceData(id) });
    expect((await webRequest(admin, 'DELETE', `/devices/${id}`)).body).toMatchObject({ deleted: true, revision: 2 });

    const data = deviceData(id, { comment: 'collected offline' });
    const gone = await consoleRequest(key, 'PUT', `/devices/${id}`, { baseRevision: 1, data });
    expect(gone.status).toBe(410);
    expect(gone.body.current.deleted).toBe(true);

    const restored = await consoleRequest(key, 'PUT', `/devices/${id}`, { baseRevision: 1, data, restore: true });
    expect(restored.status).toBe(200);
    expect(restored.body).toMatchObject({ revision: 3, device: { deleted: false, data: { comment: 'collected offline' } } });
  });
});

describe('console download', () => {
  it('returns only changes after the cursor, including deletions', async () => {
    const { key } = await harvesterKey();
    const admin = await addUser('admin');
    const start = (await consoleRequest(key, 'GET', '/devices?since=0&limit=1000')).body;
    let cursor = start.cursor;
    while ((await consoleRequest(key, 'GET', `/devices?since=${cursor}`)).body.hasMore) {
      cursor = (await consoleRequest(key, 'GET', `/devices?since=${cursor}`)).body.cursor;
    }

    const first = unique('dev');
    const second = unique('dev');
    await consoleRequest(key, 'PUT', `/devices/${first}`, { baseRevision: null, data: deviceData(first) });
    await consoleRequest(key, 'PUT', `/devices/${second}`, { baseRevision: null, data: deviceData(second) });

    const page = await consoleRequest(key, 'GET', `/devices?since=${cursor}&limit=1`);
    expect(page.body.hasMore).toBe(true);
    expect(page.body.devices.map((d: { id: string }) => d.id)).toEqual([first]);

    const rest = await consoleRequest(key, 'GET', `/devices?since=${page.body.cursor}`);
    expect(rest.body.hasMore).toBe(false);
    expect(rest.body.devices.map((d: { id: string }) => d.id)).toEqual([second]);

    await webRequest(admin, 'DELETE', `/devices/${first}`);
    const afterDelete = await consoleRequest(key, 'GET', `/devices?since=${rest.body.cursor}`);
    expect(afterDelete.body.devices).toHaveLength(1);
    expect(afterDelete.body.devices[0]).toMatchObject({ id: first, deleted: true });

    const nothingNew = await consoleRequest(key, 'GET', `/devices?since=${afterDelete.body.cursor}`);
    expect(nothingNew.body).toMatchObject({ devices: [], hasMore: false, cursor: afterDelete.body.cursor });
  });

  it('rejects invalid query parameters', async () => {
    const { key } = await harvesterKey();
    expect((await consoleRequest(key, 'GET', '/devices?since=-1')).status).toBe(400);
    expect((await consoleRequest(key, 'GET', '/devices?limit=abc')).status).toBe(400);
  });
});
