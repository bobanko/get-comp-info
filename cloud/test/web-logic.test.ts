import { describe, expect, it } from 'vitest';
import {
  changedFields,
  csvFileName,
  devicesToCsv,
  diffRecords,
  duplicateValues,
  filterDevices,
  formatDate,
  hasRole,
  ipSortValue,
  isRecent,
  sortDevices,
} from '../public/js/logic.js';
import { describeLocation, parseNumberList, subnetMask, suggestHostRange } from '../public/js/network-logic.js';

function device(id: string, data: Record<string, unknown>, extra: Record<string, unknown> = {}) {
  return { id, revision: 1, deleted: false, createdAt: '', updatedAt: '2026-10-20T10:00:00Z', updatedBy: 'h@example.com', data, ...extra };
}

const ids = (list: { id: string }[]) => list.map((d) => d.id);

const devices = [
  device('a', { hostname: 'PC-10', ip: '10.0.0.10', username: 'Иванов', esetInstalled: true, comment: 'бухгалтерия' }),
  device('b', { hostname: 'pc-9', ip: '10.0.0.9', username: '', esetInstalled: false, comment: '' }),
  device('c', { hostname: 'PC-2', ip: '10.0.0.10', esetInstalled: null, serial: 'SN-77' }),
  device('d', { hostname: 'NOIP', ip: '' }),
];

describe('roles', () => {
  it('orders roles from viewer to admin', () => {
    expect(hasRole({ role: 'editor' }, 'harvester')).toBe(true);
    expect(hasRole({ role: 'harvester' }, 'editor')).toBe(false);
    expect(hasRole({ role: 'viewer' }, 'viewer')).toBe(true);
  });
});

describe('sorting', () => {
  it('sorts IPv4 numerically and puts invalid or empty addresses last', () => {
    expect(ipSortValue('10.0.0.9')).toBeLessThan(ipSortValue('10.0.0.10'));
    expect(ipSortValue('10.0.0.256')).toBe(Infinity);
    expect(sortDevices(devices, 'ip').map((d) => d.id)).toEqual(['b', 'c', 'a', 'd']);
    expect(sortDevices(devices, 'ip', 'desc').map((d) => d.id)).toEqual(['c', 'a', 'b', 'd']);
  });

  it('sorts names naturally and case-insensitively', () => {
    expect(sortDevices(devices, 'hostname').map((d) => d.data.hostname)).toEqual(['NOIP', 'PC-2', 'pc-9', 'PC-10']);
  });

  it('keeps empty values last in both directions', () => {
    expect(sortDevices(devices, 'username').map((d) => d.id).slice(0, 1)).toEqual(['a']);
    expect(sortDevices(devices, 'username', 'desc').map((d) => d.id)[0]).toBe('a');
  });

  it('puts devices without ESET first when sorting by ESET', () => {
    expect(sortDevices(devices, 'eset')[0].id).toBe('b');
  });
});

describe('filtering', () => {
  it('searches every field, all words must match', () => {
    expect(ids(filterDevices(devices, { query: 'бухгалт' }))).toEqual(['a']);
    expect(ids(filterDevices(devices, { query: 'sn-77' }))).toEqual(['c']);
    expect(ids(filterDevices(devices, { query: 'pc иванов' }))).toEqual(['a']);
    expect(filterDevices(devices, { query: 'h@example' })).toHaveLength(4);
  });

  it('filters by ESET state and duplicate IPs', () => {
    expect(ids(filterDevices(devices, { eset: 'missing' }))).toEqual(['b']);
    expect(ids(filterDevices(devices, { eset: 'unknown' }))).toEqual(['c', 'd']);
    expect([...duplicateValues(devices, 'ip')]).toEqual(['10.0.0.10']);
    expect(ids(filterDevices(devices, { duplicatesOf: 'ip' }))).toEqual(['a', 'c']);
  });

  it('finds repeated names and serials, ignoring case, placeholders and deleted devices', () => {
    const list = [
      device('a', { hostname: 'PC-1', serial: 'abc123' }),
      device('b', { hostname: 'pc-1 ', serial: 'ABC123' }),
      device('c', { hostname: 'PC-2', serial: 'To be filled by O.E.M.' }),
      device('d', { hostname: 'PC-3', serial: 'To Be Filled By O.E.M.' }),
      device('e', { hostname: 'PC-2', serial: 'XYZ' }, { deleted: true }),
      device('f', { hostname: '', serial: '' }),
      device('g', { hostname: '', serial: '' }),
    ];
    expect([...duplicateValues(list, 'hostname')]).toEqual(['PC-1']);
    expect([...duplicateValues(list, 'serial')]).toEqual(['ABC123']);
    expect(ids(filterDevices(list, { duplicatesOf: 'hostname' }))).toEqual(['a', 'b']);
    expect(ids(filterDevices(list, { duplicatesOf: 'serial' }))).toEqual(['a', 'b']);
  });
});

describe('dates', () => {
  it('formats dates like the console viewer', () => {
    expect(formatDate('2026-10-23T14:05:00')).toBe('2026-10-23 14:05');
    expect(formatDate('not a date')).toBe('');
    expect(formatDate(null)).toBe('');
  });

  it('marks records collected within the last 7 days', () => {
    const now = new Date('2026-10-24T12:00:00Z');
    expect(isRecent('2026-10-20T12:00:00Z', now)).toBe(true);
    expect(isRecent('2026-10-10T12:00:00Z', now)).toBe(false);
    expect(isRecent('', now)).toBe(false);
  });
});

describe('differences', () => {
  it('lists added, removed and changed fields in the usual field order', () => {
    const changes = diffRecords({ hostname: 'A', comment: 'x', extra: 1 }, { hostname: 'B', comment: 'x', ip: '1.2.3.4' });
    expect(changes).toEqual([
      { name: 'hostname', kind: 'changed', before: 'A', after: 'B' },
      { name: 'ip', kind: 'added', before: undefined, after: '1.2.3.4' },
      { name: 'extra', kind: 'removed', before: 1, after: undefined },
    ]);
  });

  it('treats a first revision as all fields added and ignores key order in objects', () => {
    expect(diffRecords(null, { hostname: 'A' })).toEqual([{ name: 'hostname', kind: 'added', before: undefined, after: 'A' }]);
    expect(diffRecords({ mac: { a: 1, b: 2 } }, { mac: { b: 2, a: 1 } })).toEqual([]);
  });

  it('sends only fields whose text was changed in the form', () => {
    const original = { hostname: 'PC', mac: {}, comment: null, username: 'u' };
    expect(changedFields(original, { hostname: 'PC', mac: '{}', comment: '', username: 'u' })).toEqual({});
    expect(changedFields(original, { hostname: 'PC-2', mac: '{}', comment: 'новое', username: 'u' })).toEqual({
      hostname: 'PC-2',
      comment: 'новое',
    });
  });
});

describe('network settings page', () => {
  it('parses number lists and marks junk for the server to reject', () => {
    expect(parseNumberList('111, 112 113;114')).toEqual([111, 112, 113, 114]);
    expect(parseNumberList('')).toEqual([]);
    expect(Number.isNaN(parseNumberList('1, x')[1])).toBe(true);
  });

  it('computes masks and host ranges from the gateway', () => {
    expect(subnetMask(25)).toBe('255.255.255.128');
    expect(subnetMask(30)).toBe('255.255.255.252');
    expect(suggestHostRange(129, 25)).toEqual({ firstHostOctet: 130, lastHostOctet: 254 });
    expect(suggestHostRange(97, 28)).toEqual({ firstHostOctet: 98, lastHostOctet: 110 });
    expect(suggestHostRange(110, 28)).toEqual({ firstHostOctet: 97, lastHostOctet: 109 });
    expect(suggestHostRange(100, 28)).toBeNull();
    expect(suggestHostRange(129, 16)).toBeNull();
  });

  it('describes what the console will configure', () => {
    const location = { thirdOctet: 5, prefixLength: 25, gatewayOctet: 129, firstHostOctet: 130, lastHostOctet: 254 };
    expect(describeLocation(10, 20, location)).toBe('10.20.5.130–10.20.5.254, шлюз 10.20.5.129, маска 255.255.255.128');
    expect(describeLocation(10, Number.NaN, { ...location, gatewayOctet: '' })).toContain('шлюз 10.?.5.?');
  });
});

describe('CSV export', () => {
  it('matches the console format: all fields, quoted, semicolons, sorted by host name', () => {
    const csv = devicesToCsv([
      device('2', { hostname: 'B', comment: 'say "hi"; bye', esetInstalled: false }),
      device('1', { hostname: 'A', mac: { x: 1 }, esetInstalled: true, extra: null }),
    ]);
    expect(csv.split('\r\n')).toEqual([
      '"hostname";"comment";"esetInstalled";"mac";"extra"',
      '"A";"";"True";"{""x"":1}";""',
      '"B";"say ""hi""; bye";"False";"";""',
      '',
    ]);
  });

  it('names files like the console export', () => {
    expect(csvFileName(new Date(2026, 9, 24, 9, 5, 7))).toBe('devices_2026-10-24_09-05-07.csv');
  });
});
