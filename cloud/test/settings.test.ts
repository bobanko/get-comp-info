import { describe, expect, it } from 'vitest';
import { addUser, consoleRequest, harvesterKey, webRequest } from './helpers';

// Made-up addresses: real profiles live only in the production cloud.
function sampleSettings(overrides: Record<string, unknown> = {}) {
  return {
    firstOctet: 10,
    segments: [20, 21],
    domainCheckHost: 'dc.example.local',
    domainName: 'example.local',
    locations: [
      { name: 'Главный корпус', thirdOctet: 5, prefixLength: 25, gatewayOctet: 129, firstHostOctet: 130, lastHostOctet: 254, dnsServers: ['10.9.0.1', '10.9.0.2'] },
      { name: 'Склад', thirdOctet: 7, prefixLength: 28, gatewayOctet: 97, firstHostOctet: 98, lastHostOctet: 110, dnsServers: ['10.9.0.1'], segments: [20] },
    ],
    ...overrides,
  };
}

async function currentRevision(admin: string): Promise<number | null> {
  return (await webRequest(admin, 'GET', '/settings/network')).body.setting?.revision ?? null;
}

describe('network settings', () => {
  it('are edited by admins and downloaded by console keys', async () => {
    const admin = await addUser('admin');
    const { key } = await harvesterKey();
    const baseRevision = await currentRevision(admin);

    const saved = await webRequest(admin, 'PUT', '/settings/network', { baseRevision, value: sampleSettings() });
    expect(saved.status).toBe(200);
    expect(saved.body.setting).toMatchObject({ revision: (baseRevision ?? 0) + 1, updatedBy: admin });

    const downloaded = await consoleRequest(key, 'GET', '/settings/network');
    expect(downloaded.status).toBe(200);
    expect(downloaded.body.value.locations[1]).toEqual(sampleSettings().locations[1]);
    expect(downloaded.body.value.domainCheckHost).toBe('dc.example.local');
    expect(downloaded.body.value.domainName).toBe('example.local');

    const history = await webRequest(admin, 'GET', '/settings/network/history');
    expect(history.body.history[0]).toMatchObject({ revision: saved.body.setting.revision, changedBy: admin });
  });

  it('keep the network configuration away from everyone but admins in the web', async () => {
    for (const role of ['viewer', 'harvester', 'editor'] as const) {
      const user = await addUser(role);
      expect((await webRequest(user, 'GET', '/settings/network')).status).toBe(403);
      expect((await webRequest(user, 'PUT', '/settings/network', { baseRevision: null, value: sampleSettings() })).status).toBe(403);
    }
    expect((await consoleRequest(null, 'GET', '/settings/network')).status).toBe(401);
  });

  it('reject stale edits', async () => {
    const admin = await addUser('admin');
    const baseRevision = await currentRevision(admin);
    await webRequest(admin, 'PUT', '/settings/network', { baseRevision, value: sampleSettings() });
    const stale = await webRequest(admin, 'PUT', '/settings/network', { baseRevision, value: sampleSettings({ segments: [30] }) });
    expect(stale.status).toBe(409);
    expect(stale.body.current.revision).toBe((baseRevision ?? 0) + 1);
  });

  it('drop unknown fields and an empty domain check host', async () => {
    const admin = await addUser('admin');
    const value = { ...sampleSettings({ domainCheckHost: '', domainName: ' Corp.Example ' }), extra: 'x' };
    const saved = await webRequest(admin, 'PUT', '/settings/network', { baseRevision: await currentRevision(admin), value });
    expect(saved.body.setting.value.domainCheckHost).toBeNull();
    expect(saved.body.setting.value.domainName).toBe('corp.example');
    expect(saved.body.setting.value).not.toHaveProperty('extra');
  });

  it('keep the telecom room and the reserved addresses', async () => {
    const admin = await addUser('admin');
    const { key } = await harvesterKey();
    const [main, store] = sampleSettings().locations;
    const value = sampleSettings({
      locations: [{ ...main, tk: ' ТК100 ' }, { ...store, tk: '' }],
      reservedAddresses: [
        { address: ' 10.20.5.131 ', note: ' сервер ' },
        { address: '10.20.5.132' },
        { address: '10.20.5.140', lastAddress: ' 10.20.5.149 ', note: 'камеры' },
        { address: '10.20.5.150', lastAddress: '10.20.5.150', note: '' },
        { address: '10.20.5.151', lastAddress: '' },
      ],
    });
    const saved = await webRequest(admin, 'PUT', '/settings/network', { baseRevision: await currentRevision(admin), value });
    expect(saved.status).toBe(200);
    const downloaded = (await consoleRequest(key, 'GET', '/settings/network')).body.value;
    expect(downloaded.locations[0].tk).toBe('ТК100');
    expect(downloaded.locations[1]).not.toHaveProperty('tk');
    expect(downloaded.reservedAddresses).toEqual([
      { address: '10.20.5.131', note: 'сервер' },
      { address: '10.20.5.132', note: '' },
      { address: '10.20.5.140', lastAddress: '10.20.5.149', note: 'камеры' },
      { address: '10.20.5.150', note: '' },
      { address: '10.20.5.151', note: '' },
    ]);

    const without = await webRequest(admin, 'PUT', '/settings/network', { baseRevision: await currentRevision(admin), value: sampleSettings() });
    expect(without.body.setting.value.reservedAddresses).toEqual([]);
  });

  it('validate addresses and ranges', async () => {
    const admin = await addUser('admin');
    const location = sampleSettings().locations[0];
    const invalid: [string, Record<string, unknown>][] = [
      ['no locations', { locations: [] }],
      ['bad first octet', { firstOctet: 256 }],
      ['duplicate segments', { segments: [20, 20] }],
      ['bad domain', { domainCheckHost: 'not a host!' }],
      ['bad domain name', { domainName: 'nodot' }],
      ['address as domain name', { domainName: '10.0.0.1' }],
      ['duplicate names', { locations: [location, { ...location, name: 'главный КОРПУС' }] }],
      ['prefix too short', { locations: [{ ...location, prefixLength: 16 }] }],
      ['gateway is the network address', { locations: [{ ...location, gatewayOctet: 128 }] }],
      ['hosts outside the gateway subnet', { locations: [{ ...location, firstHostOctet: 100 }] }],
      ['broadcast in the host range', { locations: [{ ...location, lastHostOctet: 255 }] }],
      ['gateway inside the host range', { locations: [{ ...location, gatewayOctet: 200 }] }],
      ['first after last', { locations: [{ ...location, firstHostOctet: 200, lastHostOctet: 150 }] }],
      ['bad DNS', { locations: [{ ...location, dnsServers: ['10.9.0.300'] }] }],
      ['no DNS', { locations: [{ ...location, dnsServers: [] }] }],
      ['long telecom room', { locations: [{ ...location, tk: 'x'.repeat(21) }] }],
      ['telecom room not text', { locations: [{ ...location, tk: 5 }] }],
      ['reserved not a list', { reservedAddresses: '10.0.0.5' }],
      ['reserved bad address', { reservedAddresses: [{ address: '10.0.0.256', note: '' }] }],
      ['reserved repeats', { reservedAddresses: [{ address: '10.0.0.5', note: 'a' }, { address: '10.0.0.5', note: 'b' }] }],
      ['reserved long note', { reservedAddresses: [{ address: '10.0.0.5', note: 'x'.repeat(201) }] }],
      ['reserved range bad end', { reservedAddresses: [{ address: '10.0.0.5', lastAddress: '10.0.0', note: '' }] }],
      ['reserved range backwards', { reservedAddresses: [{ address: '10.0.0.9', lastAddress: '10.0.0.5', note: '' }] }],
      ['reserved range too long', { reservedAddresses: [{ address: '10.0.0.0', lastAddress: '10.0.4.0', note: '' }] }],
      ['reserved address inside a range', { reservedAddresses: [{ address: '10.0.0.5', lastAddress: '10.0.0.9', note: '' }, { address: '10.0.0.7', note: '' }] }],
      ['reserved ranges overlap', { reservedAddresses: [{ address: '10.0.0.5', lastAddress: '10.0.0.9', note: '' }, { address: '10.0.0.1', lastAddress: '10.0.0.5', note: '' }] }],
    ];
    for (const [label, overrides] of invalid) {
      const result = await webRequest(admin, 'PUT', '/settings/network', { baseRevision: await currentRevision(admin), value: sampleSettings(overrides) });
      expect(result.status, label).toBe(400);
      expect(result.body.error.code, label).toBe('invalid_settings');
    }
  });
});
