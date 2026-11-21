import { env } from 'cloudflare:test';
import { describe, expect, it } from 'vitest';
import { addUser, consoleRequest, deviceData, harvesterKey, unique, webRequest } from './helpers';

async function journal(admin: string, query = '') {
  const response = await webRequest(admin, 'GET', '/audit' + query);
  expect(response.status).toBe(200);
  return response.body as { entries: any[]; next: string | null };
}

describe('journal', () => {
  it('is for admins only', async () => {
    for (const role of ['viewer', 'harvester', 'editor'] as const) {
      expect((await webRequest(await addUser(role), 'GET', '/audit')).status).toBe(403);
    }
    expect((await webRequest(await addUser('admin'), 'GET', '/audit')).status).toBe(200);
  });

  it('records keys, users and access requests with who did it', async () => {
    const admin = await addUser('admin');
    const created = await webRequest(admin, 'POST', '/keys', { label: 'Флешка журнала' });
    await webRequest(admin, 'DELETE', `/keys/${created.body.id}`);

    const email = `${unique('new')}@example.com`;
    await webRequest(admin, 'POST', '/users', { email, name: 'Новый', role: 'viewer' });
    await webRequest(admin, 'PATCH', `/users/${email}`, { role: 'editor', name: 'Новый' });
    // a PATCH that changes nothing leaves no entry
    await webRequest(admin, 'PATCH', `/users/${email}`, { role: 'editor' });

    const stranger = `${unique('stranger')}@example.com`;
    expect((await webRequest(stranger, 'GET', '/me')).status).toBe(403);
    await webRequest(admin, 'DELETE', `/access-requests/${stranger}`);

    const { entries } = await journal(admin, '?category=access');
    const mine = entries.filter((e) => e.actor === admin);
    expect(mine.map((e) => e.action)).toEqual(['access.reject', 'user.update', 'user.create', 'key.revoke', 'key.create']);
    expect(mine.every((e) => e.actorName === 'Test admin' && e.category === 'access')).toBe(true);
    expect(mine[0].target).toBe(stranger);
    expect(mine[1]).toMatchObject({ target: email, details: { role: ['viewer', 'editor'] } });
    expect(mine[1].details).not.toHaveProperty('name');
    expect(mine[2].details).toEqual({ name: 'Новый', role: 'viewer' });
    expect(mine[4]).toMatchObject({ target: created.body.id, details: { label: 'Флешка журнала', owner: admin } });
  });

  it('shows device changes with the hostname and the console key', async () => {
    const admin = await addUser('admin');
    const { email, key } = await harvesterKey();
    const id = unique('dev');
    await consoleRequest(key, 'PUT', `/devices/${id}`, { baseRevision: null, data: deviceData(id, { hostname: 'PC-JOURNAL' }) });
    await webRequest(admin, 'DELETE', `/devices/${id}`);

    const { entries } = await journal(admin, '?category=devices');
    const device = entries.filter((e) => e.target === id);
    expect(device).toHaveLength(2);
    expect(device[0]).toMatchObject({ kind: 'device', action: 'delete', actor: admin, source: 'web' });
    expect(device[1]).toMatchObject({
      action: 'create',
      actor: email,
      actorKey: 'Test key',
      source: 'console',
      details: { revision: 1, hostname: 'PC-JOURNAL' },
    });
    expect(entries.every((e) => e.category === 'devices')).toBe(true);
  });

  it('rejects an unknown category or a broken cursor', async () => {
    const admin = await addUser('admin');
    expect((await webRequest(admin, 'GET', '/audit?category=other')).status).toBe(400);
    expect((await webRequest(admin, 'GET', '/audit?cursor=broken')).status).toBe(400);
  });

  it('pages through entries of the same moment without losing or repeating any', async () => {
    const admin = await addUser('admin');
    const at = '2999-01-01T00:00:00.000Z';
    const insert = env.DB.prepare('INSERT INTO audit_log (at, actor, action, target) VALUES (?, ?, ?, ?)');
    await env.DB.batch(Array.from({ length: 150 }, (_, i) => insert.bind(at, admin, 'key.create', `page-${i}`)));
    try {
      const first = await journal(admin);
      expect(first.entries).toHaveLength(100);
      expect(first.next).not.toBeNull();
      const second = await journal(admin, '?cursor=' + first.next);
      const targets = [...first.entries, ...second.entries].filter((e) => e.at === at).map((e) => e.target);
      expect(targets).toHaveLength(150);
      expect(new Set(targets).size).toBe(150);
    } finally {
      await env.DB.prepare('DELETE FROM audit_log WHERE at = ?').bind(at).run();
    }
  });
});
