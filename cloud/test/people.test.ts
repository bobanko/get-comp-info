import { describe, expect, it } from 'vitest';
import { addUser, consoleRequest, harvesterKey, webRequest } from './helpers';

function deviceData(id: string, extra: Record<string, unknown> = {}) {
  return { computerId: id, computerIdSource: 'serial', hostname: 'PC-' + id, serial: id, ...extra };
}

describe('people', () => {
  it('are added and changed by editors, seen by viewers and written to the journal', async () => {
    const editor = await addUser('editor');
    const viewer = await addUser('viewer');
    const created = await webRequest(editor, 'POST', '/people', { name: '  Іваненко   Іван ', note: ' S1 ' });
    expect(created.status).toBe(201);
    expect(created.body.person).toMatchObject({ name: 'Іваненко Іван', note: 'S1', createdBy: editor, deleted: false });
    const id = created.body.person.id;
    expect(Number.isInteger(id)).toBe(true);

    const changed = await webRequest(editor, 'PATCH', `/people/${id}`, { note: 'S3' });
    expect(changed.body.person).toMatchObject({ name: 'Іваненко Іван', note: 'S3' });

    const list = await webRequest(viewer, 'GET', '/people');
    expect(list.body.people.find((p: { id: number }) => p.id === id).note).toBe('S3');
    expect((await webRequest(viewer, 'POST', '/people', { name: 'X' })).status).toBe(403);
    expect((await webRequest(viewer, 'GET', '/people?deleted=1')).status).toBe(403);

    const admin = await addUser('admin');
    const journal = await webRequest(admin, 'GET', '/audit?category=devices');
    const actions = journal.body.entries.filter((e: { target: string }) => e.target === String(id)).map((e: { action: string }) => e.action);
    expect(actions).toEqual(expect.arrayContaining(['person.create', 'person.update']));
  });

  it('reject an empty or too long name', async () => {
    const editor = await addUser('editor');
    expect((await webRequest(editor, 'POST', '/people', { name: ' ' })).body.error.code).toBe('invalid_person');
    expect((await webRequest(editor, 'POST', '/people', { name: 'x'.repeat(201) })).body.error.code).toBe('invalid_person');
    expect((await webRequest(editor, 'POST', '/people', { name: 'Ok', note: 5 })).body.error.code).toBe('invalid_person');
    expect((await webRequest(editor, 'PATCH', '/people/abc', { name: 'Ok' })).status).toBe(400);
    expect((await webRequest(editor, 'PATCH', '/people/999999', { name: 'Ok' })).status).toBe(404);
  });

  it('are deleted by admins only when no device links them', async () => {
    const admin = await addUser('admin');
    const editor = await addUser('editor');
    const { key } = await harvesterKey();
    const person = (await webRequest(admin, 'POST', '/people', { name: 'Петренко Петро' })).body.person;
    const id = 'people-' + person.id;
    await consoleRequest(key, 'PUT', `/devices/${id}`, { baseRevision: null, data: deviceData(id, { ownerId: person.id }) });

    const devices = await webRequest(editor, 'GET', `/people/${person.id}/devices`);
    expect(devices.body.devices).toEqual([{ id, hostname: 'PC-' + id }]);
    expect((await webRequest(editor, 'DELETE', `/people/${person.id}`)).status).toBe(403);
    const refused = await webRequest(admin, 'DELETE', `/people/${person.id}`);
    expect(refused.status).toBe(409);
    expect(refused.body.error.code).toBe('person_in_use');

    await webRequest(admin, 'PATCH', `/devices/${id}`, { baseRevision: 1, changes: { ownerId: null } });
    const deleted = await webRequest(admin, 'DELETE', `/people/${person.id}`);
    expect(deleted.body.person.deleted).toBe(true);
    const live = await webRequest(editor, 'GET', '/people');
    expect(live.body.people.some((p: { id: number }) => p.id === person.id)).toBe(false);
    const all = await webRequest(admin, 'GET', '/people?deleted=1');
    expect(all.body.people.some((p: { id: number }) => p.id === person.id)).toBe(true);
    // A deleted person cannot be linked again.
    expect((await webRequest(admin, 'PATCH', `/devices/${id}`, { baseRevision: 2, changes: { userId: person.id } })).body.error.code).toBe('unknown_person');
  });
});
