import { describe, expect, it } from 'vitest';
import { addUser, webRequest } from './helpers';

describe('property items', () => {
  it('are added and changed by editors, seen by viewers and written to the journal', async () => {
    const editor = await addUser('editor');
    const viewer = await addUser('viewer');
    const owner = (await webRequest(editor, 'POST', '/people', { name: 'Лукашик' })).body.person;
    const created = await webRequest(editor, 'POST', '/items', { type: ' Монітор ', model: 'Dell  P2217H', serial: 'CN-0RH81R', location: 's2', ownerId: owner.id });
    expect(created.status).toBe(201);
    expect(created.body.item).toMatchObject({ type: 'Монітор', model: 'Dell P2217H', serial: 'CN-0RH81R', location: 's2', ownerId: owner.id, note: '', deleted: false });
    const id = created.body.item.id;

    const changed = await webRequest(editor, 'PATCH', `/items/${id}`, { note: 'під сходами', ownerId: null });
    expect(changed.body.item).toMatchObject({ type: 'Монітор', note: 'під сходами', ownerId: null });

    const list = await webRequest(viewer, 'GET', '/items');
    expect(list.body.items.find((i: { id: number }) => i.id === id).note).toBe('під сходами');
    expect((await webRequest(viewer, 'POST', '/items', { type: 'Флешка' })).status).toBe(403);

    const admin = await addUser('admin');
    const journal = await webRequest(admin, 'GET', '/audit?category=devices');
    const mine = journal.body.entries.filter((e: { target: string; action: string }) => e.target === String(id) && e.action.startsWith('item.'));
    expect(mine.map((e: { action: string }) => e.action)).toEqual(expect.arrayContaining(['item.create', 'item.update']));
    expect(mine.find((e: { action: string }) => e.action === 'item.update').details).toMatchObject({ type: 'Монітор', note: ['', 'під сходами'], ownerId: [owner.id, null] });
  });

  it('check the fields and the person', async () => {
    const editor = await addUser('editor');
    expect((await webRequest(editor, 'POST', '/items', { model: 'x' })).body.error.code).toBe('invalid_item');
    expect((await webRequest(editor, 'POST', '/items', { type: 'Флешка', note: 5 })).body.error.code).toBe('invalid_item');
    expect((await webRequest(editor, 'POST', '/items', { type: 'Флешка', serial: 'x'.repeat(201) })).body.error.code).toBe('invalid_item');
    expect((await webRequest(editor, 'POST', '/items', { type: 'Флешка', ownerId: 'Сук' })).body.error.code).toBe('invalid_person_ref');
    expect((await webRequest(editor, 'POST', '/items', { type: 'Флешка', ownerId: 999999 })).body.error.code).toBe('unknown_person');
    const item = (await webRequest(editor, 'POST', '/items', { type: 'Флешка' })).body.item;
    expect((await webRequest(editor, 'PATCH', `/items/${item.id}`, { type: '' })).body.error.code).toBe('invalid_item');
    expect((await webRequest(editor, 'PATCH', '/items/999999', { note: 'x' })).status).toBe(404);
  });

  it('are deleted by admins; a person responsible for an item is not deleted', async () => {
    const admin = await addUser('admin');
    const editor = await addUser('editor');
    const owner = (await webRequest(admin, 'POST', '/people', { name: 'Урсул' })).body.person;
    const item = (await webRequest(admin, 'POST', '/items', { type: 'SIM-карта', serial: '0969069265', ownerId: owner.id })).body.item;

    const refused = await webRequest(admin, 'DELETE', `/people/${owner.id}`);
    expect(refused.status).toBe(409);
    expect(refused.body.error.code).toBe('person_in_use');

    expect((await webRequest(editor, 'DELETE', `/items/${item.id}`)).status).toBe(403);
    expect((await webRequest(admin, 'DELETE', `/items/${item.id}`)).body.item.deleted).toBe(true);
    expect((await webRequest(editor, 'GET', '/items')).body.items.some((i: { id: number }) => i.id === item.id)).toBe(false);
    expect((await webRequest(admin, 'GET', '/items?deleted=1')).body.items.some((i: { id: number }) => i.id === item.id)).toBe(true);
    expect((await webRequest(admin, 'DELETE', `/people/${owner.id}`)).status).toBe(200);
  });
});
