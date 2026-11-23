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
import { normalizeUuid, sameComputer } from '../src/devices';

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

  it('keeps the inventory mark set in the web when the console sends a record without it', async () => {
    const { key } = await harvesterKey();
    const id = unique('dev');
    await consoleRequest(key, 'PUT', `/devices/${id}`, { baseRevision: null, data: deviceData(id) });
    const editor = await addUser('editor');
    const marked = await webRequest(editor, 'PATCH', `/devices/${id}`, { baseRevision: 1, changes: { inventoried: true } });
    expect(marked.body.device.data.inventoried).toBe(true);

    // An older program drops fields it does not know.
    const upload = await consoleRequest(key, 'PUT', `/devices/${id}`, { baseRevision: 2, data: deviceData(id, { comment: 'new' }) });
    expect(upload.status).toBe(200);
    expect(upload.body.device.data).toMatchObject({ comment: 'new', inventoried: true });

    // The console may still send the value explicitly.
    const unmarked = await consoleRequest(key, 'PUT', `/devices/${id}`, { baseRevision: 3, data: deviceData(id, { comment: 'new', inventoried: false }) });
    expect(unmarked.body.device.data.inventoried).toBe(false);

    const wrong = await webRequest(editor, 'PATCH', `/devices/${id}`, { baseRevision: 4, changes: { inventoried: 'yes' } });
    expect(wrong.status).toBe(400);
    expect(wrong.body.error.code).toBe('invalid_inventoried');
  });

  it('keeps the people linked in the web when the console sends a record without them', async () => {
    const { key } = await harvesterKey();
    const id = unique('dev');
    await consoleRequest(key, 'PUT', `/devices/${id}`, { baseRevision: null, data: deviceData(id) });
    const editor = await addUser('editor');
    const user = (await webRequest(editor, 'POST', '/people', { name: 'Іваненко Іван' })).body.person;
    const owner = (await webRequest(editor, 'POST', '/people', { name: 'Петренко Петро', note: 'МВО' })).body.person;
    const linked = await webRequest(editor, 'PATCH', `/devices/${id}`, { baseRevision: 1, changes: { userId: user.id, ownerId: owner.id } });
    expect(linked.body.device.data).toMatchObject({ userId: user.id, ownerId: owner.id });

    const upload = await consoleRequest(key, 'PUT', `/devices/${id}`, { baseRevision: 2, data: deviceData(id, { comment: 'new' }) });
    expect(upload.body.device.data).toMatchObject({ comment: 'new', userId: user.id, ownerId: owner.id });

    const wrong = await consoleRequest(key, 'PUT', `/devices/${id}`, { baseRevision: 3, data: deviceData(id, { userId: 'Іваненко' }) });
    expect(wrong.status).toBe(400);
    expect(wrong.body.error.code).toBe('invalid_person_ref');
    const unknown = await webRequest(editor, 'PATCH', `/devices/${id}`, { baseRevision: 3, changes: { ownerId: 999999 } });
    expect(unknown.status).toBe(400);
    expect(unknown.body.error.code).toBe('unknown_person');

    const names = await consoleRequest(key, 'GET', '/people');
    expect(names.body.people).toEqual(expect.arrayContaining([{ id: owner.id, name: 'Петренко Петро', note: 'МВО' }]));
  });

  it('remembers which key made the change', async () => {
    const email = await addUser('harvester');
    const flash = await addKey(email, 'Флешка Кости');
    const id = unique('dev');
    const response = await consoleRequest(flash, 'PUT', `/devices/${id}`, { baseRevision: null, data: deviceData(id) });
    expect(response.body.device).toMatchObject({ updatedBy: email, updatedByName: 'Test harvester', updatedByKey: 'Флешка Кости' });

    const editor = await addUser('editor');
    expect((await webRequest(editor, 'GET', `/devices/${id}`)).body).toMatchObject({ updatedByKey: 'Флешка Кости' });
    const list = await webRequest(editor, 'GET', '/devices');
    expect(list.body.devices.find((device: { id: string }) => device.id === id)).toMatchObject({ updatedByKey: 'Флешка Кости' });
    const history = await webRequest(editor, 'GET', `/devices/${id}/history`);
    expect(history.body.history[0]).toMatchObject({ changedBy: email, changedByName: 'Test harvester', changedByKey: 'Флешка Кости' });

    // A web change after it is the user's own, without a key.
    await webRequest(editor, 'PATCH', `/devices/${id}`, { baseRevision: 1, changes: { comment: 'x' } });
    const edited = await webRequest(editor, 'GET', `/devices/${id}`);
    expect(edited.body).toMatchObject({ updatedBy: editor, updatedByName: 'Test editor', updatedByKey: null });
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

describe('one computer, one record', () => {
  it('knows one computer by its UUID, or by serial or Windows installation when a UUID is missing', () => {
    const uuid = '03341FE8-9B79-4CC9-8CBD-A82BDD18C277';
    expect(sameComputer({ smbiosUuid: uuid }, { smbiosUuid: `{${uuid.toLowerCase()}}` })).toBe(true);
    // two valid UUIDs decide alone: the same serial on another board is another computer
    expect(sameComputer({ smbiosUuid: uuid, serial: 'SN1' }, { smbiosUuid: 'A3341FE8-9B79-4CC9-8CBD-A82BDD18C277', serial: 'SN1' })).toBe(false);
    expect(sameComputer({ smbiosUuid: uuid, serial: 'SN1' }, { serial: ' sn1 ' })).toBe(true);
    expect(sameComputer({ smbiosUuid: 'FFFFFFFF-FFFF-FFFF-FFFF-FFFFFFFFFFFF', serial: 'SN1' }, { serial: 'SN1' })).toBe(true);
    expect(sameComputer({ serial: 'Default string' }, { serial: 'default string' })).toBe(false);
    expect(sameComputer({ windowsMachineGuid: uuid }, { windowsMachineGuid: uuid.toLowerCase() })).toBe(true);
    expect(normalizeUuid('00000000-0000-0000-0000-000000000000')).toBeNull();
  });

  it('refuses a new record of a computer that has a live record under another id, when the program asks', async () => {
    const { key } = await harvesterKey();
    const admin = await addUser('admin');
    const uuid = crypto.randomUUID().toUpperCase();
    const old = unique('legacy');
    await consoleRequest(key, 'PUT', `/devices/${old}`, { baseRevision: null, data: deviceData(old, { smbiosUuid: uuid }) });

    const fresh = unique('dev');
    const data = deviceData(fresh, { smbiosUuid: uuid.toLowerCase() });
    const refused = await consoleRequest(key, 'PUT', `/devices/${fresh}`, { baseRevision: null, data, duplicateCheck: true });
    expect(refused.status).toBe(409);
    expect(refused.body.error.code).toBe('duplicate');
    expect(refused.body.existing).toMatchObject({ id: old, revision: 1, data: { smbiosUuid: uuid } });
    expect((await consoleRequest(key, 'PUT', `/devices/${fresh}`, { baseRevision: null, data, duplicateCheck: 'yes' })).status).toBe(400);

    // Older programs do not ask, and the user may keep the second record on purpose.
    const separate = await consoleRequest(key, 'PUT', `/devices/${fresh}`, { baseRevision: null, data });
    expect(separate.status).toBe(200);
    // Only new records are checked.
    const update = await consoleRequest(key, 'PUT', `/devices/${fresh}`, { baseRevision: 1, data: { ...data, comment: 'x' }, duplicateCheck: true });
    expect(update.status).toBe(200);

    // A deleted record does not count.
    await webRequest(admin, 'DELETE', `/devices/${old}`);
    await webRequest(admin, 'DELETE', `/devices/${fresh}`);
    const third = unique('dev');
    const accepted = await consoleRequest(key, 'PUT', `/devices/${third}`, { baseRevision: null, data: deviceData(third, { smbiosUuid: uuid }), duplicateCheck: true });
    expect(accepted.status).toBe(200);
  });
});
