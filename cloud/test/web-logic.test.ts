import { describe, expect, it } from 'vitest';
import {
  authorLabel,
  authorTitle,
  changedFields,
  csvFileName,
  deviceAuthor,
  devicesToCsv,
  diffRecords,
  duplicateValues,
  fieldChanges,
  filterDevices,
  formatDate,
  hasRole,
  hostnameIssue,
  ipSortValue,
  isInventoried,
  isRecent,
  journalActor,
  journalLine,
  filterItems,
  itemTypes,
  parsePerson,
  personLinks,
  personName,
  setPeople,
  shortAge,
  sortDevices,
} from '../public/js/logic.js';
import {
  commonDns,
  formatReserved,
  generalProblems,
  isAutoRange,
  locationProblems,
  parseNumberList,
  parseOctet,
  parseReserved,
  reservedProblems,
  subnetInfo,
  subnetMask,
  suggestHostRange,
} from '../public/js/network-logic.js';
import { errorText } from '../public/js/api.js';

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

describe('hostname policy', () => {
  it('flags Windows default names and names off the AA-000-… pattern', () => {
    expect(hostnameIssue('AB-123-45')).toBeNull();
    expect(hostnameIssue('ab-123-buh')).toBeNull();
    expect(hostnameIssue('')).toBeNull();
    expect(hostnameIssue(null)).toBeNull();
    expect(hostnameIssue('DESKTOP-7QK2M1L')).toBe('default');
    expect(hostnameIssue('laptop-abc')).toBe('default');
    expect(hostnameIssue('AB--123-45')).toBe('pattern');
    expect(hostnameIssue('AB-12-45')).toBe('pattern');
    expect(hostnameIssue('AB-123-')).toBe('pattern');
    expect(hostnameIssue('BUH-PC')).toBe('pattern');
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

  it('reads octets typed in the form and finds the gateway subnet', () => {
    expect(parseOctet(' 42 ')).toBe(42);
    expect(parseOctet(7)).toBe(7);
    expect(parseOctet('256')).toBeNull();
    expect(parseOctet('')).toBeNull();
    expect(parseOctet('1a')).toBeNull();
    expect(subnetInfo('129', 25)).toEqual({ network: 128, broadcast: 255, hosts: 126 });
    expect(subnetInfo(97, '28')).toEqual({ network: 96, broadcast: 111, hosts: 14 });
    expect(subnetInfo('', 25)).toBeNull();
  });

  it('knows when the host range is the automatic one', () => {
    const location = { prefixLength: 25, gatewayOctet: '129', firstHostOctet: 130, lastHostOctet: '254' };
    expect(isAutoRange(location)).toBe(true);
    expect(isAutoRange({ ...location, lastHostOctet: 200 })).toBe(false);
    expect(isAutoRange({ ...location, gatewayOctet: 140 })).toBe(false);
  });

  it('checks a location with the same rules as the server', () => {
    const good = { name: 'Главный', thirdOctet: '5', prefixLength: 25, gatewayOctet: '129', firstHostOctet: '130', lastHostOctet: '254', dnsServers: '10.0.0.1, 10.0.0.2', segments: '' };
    expect(locationProblems(good, ['Главный'])).toEqual([]);
    expect(locationProblems({ ...good, name: ' ' })).toContain('Укажите название.');
    expect(locationProblems(good, ['Главный', 'главный'])).toContain('Такое название уже есть.');
    expect(locationProblems({ ...good, thirdOctet: '300' }).join()).toContain('Третий октет');
    expect(locationProblems({ ...good, gatewayOctet: '128' }).join()).toContain('адресом сети');
    expect(locationProblems({ ...good, firstHostOctet: '100' }).join()).toContain('внутри подсети');
    expect(locationProblems({ ...good, firstHostOctet: '200', lastHostOctet: '150' })).toContain('Первый адрес больше последнего.');
    expect(locationProblems({ ...good, gatewayOctet: '200' })).toContain('Шлюз попадает в диапазон адресов компьютеров.');
    // Empty DNS means the shared one.
    expect(locationProblems({ ...good, dnsServers: '' }, ['Главный'])).toEqual([]);
    expect(locationProblems({ ...good, dnsServers: '10.0.0.300' }).join()).toContain('Свои DNS');
    expect(locationProblems({ ...good, dnsServers: '1.1.1.1 2.2.2.2 3.3.3.3 4.4.4.4 5.5.5.5' }).join()).toContain('от 1 до 4');
    expect(locationProblems({ ...good, segments: '30, 30' }).join()).toContain('повторяются');
  });

  it('checks the shared part of the address', () => {
    const general = { firstOctet: '10', segments: '20, 21', dnsServers: '10.0.0.1' };
    expect(generalProblems(general)).toEqual([]);
    expect(generalProblems({ ...general, firstOctet: '' })).toHaveLength(1);
    expect(generalProblems({ ...general, segments: '' })).toHaveLength(1);
    expect(generalProblems({ ...general, segments: '20, x' })).toHaveLength(1);
    expect(generalProblems({ ...general, dnsServers: '' })).toEqual(['DNS: от 1 до 4 адресов.']);
  });

  it('finds the DNS most locations share', () => {
    const at = (...dnsServers: string[]) => ({ dnsServers });
    expect(commonDns([at('10.0.0.1', '10.0.0.2'), at('10.0.0.9'), at('10.0.0.1', '10.0.0.2')])).toBe('10.0.0.1, 10.0.0.2');
    expect(commonDns([at('10.0.0.9')])).toBe('10.0.0.9');
    expect(commonDns([])).toBe('');
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

describe('error texts', () => {
  it('shows Russian by error code and the server message for unknown codes', () => {
    const pending = { error: { code: 'access_pending', message: 'The access request…' }, email: 'a@b.cd' };
    expect(errorText('access_pending', pending, 403)).toBe(
      'Запрос на доступ для a@b.cd отправлен администратору. Когда он подтвердит, обновите страницу.',
    );
    expect(errorText('forbidden', { error: { code: 'forbidden', message: 'Not enough rights' } }, 403)).toBe('Недостаточно прав для этого действия.');
    expect(errorText('invalid_label', { error: { code: 'invalid_label', message: 'x' }, maxLength: 100 }, 400)).toContain('не длиннее 100');
    // Without the details a text needs, and for unknown codes, the server's message is shown.
    expect(errorText('access_denied', { error: { code: 'access_denied', message: 'Rejected.' } }, 403)).toBe('Rejected.');
    expect(errorText('invalid_settings', { error: { code: 'invalid_settings', message: 'Location 1: bad.' } }, 400)).toBe('Location 1: bad.');
    expect(errorText('http_502', null, 502)).toBe('Облако ответило ошибкой 502.');
  });
});

describe('change authors', () => {
  it('shows the console key, otherwise the nickname', () => {
    expect(authorLabel('bob', 'b@example.com', 'kostya')).toBe('ключ «kostya»');
    expect(authorLabel('bob', 'b@example.com', null)).toBe('bob');
    expect(authorLabel('', 'b@example.com', null)).toBe('b@example.com');
    expect(authorTitle('bob', 'b@example.com', 'kostya')).toBe('Ключ пользователя bob (b@example.com)');
    expect(authorTitle('bob', 'b@example.com', null)).toBe('b@example.com');
    expect(deviceAuthor({ updatedBy: 'b@example.com', updatedByName: 'bob', updatedByKey: null })).toBe('bob');
  });

  it('finds devices by the key name', () => {
    const devices = [device('A', {}, { updatedByKey: 'kostya' }), device('B', {})];
    expect(filterDevices(devices, { query: 'kostya' }).map((d: { id: string }) => d.id)).toEqual(['A']);
  });
});

describe('field change dates', () => {
  it('formats a compact age', () => {
    const now = new Date('2026-11-19T12:00:00Z');
    expect(shortAge('2026-11-19T11:59:50Z', now)).toBe('1 мин');
    expect(shortAge('2026-11-19T11:15:00Z', now)).toBe('45 мин');
    expect(shortAge('2026-11-19T07:00:00Z', now)).toBe('5 ч');
    expect(shortAge('2026-11-16T12:00:00Z', now)).toBe('3 д');
    expect(shortAge('2026-09-01T12:00:00Z', now)).toBe('2 мес');
    expect(shortAge('2025-08-01T12:00:00Z', now)).toBe('1 г');
    expect(shortAge('', now)).toBe('');
  });

  it('finds the revision that set each current value', () => {
    // Newest first, as the API returns the history.
    const history = [
      { revision: 4, action: 'restore', changedAt: 'd', data: { hostname: 'B', ip: '2', comment: 'x' } },
      { revision: 3, action: 'delete', changedAt: 'c', data: { hostname: 'B', ip: '2', comment: 'x' } },
      { revision: 2, action: 'update', changedAt: 'b', data: { hostname: 'B', ip: '2', comment: 'x' } },
      { revision: 1, action: 'create', changedAt: 'a', data: { hostname: 'A', ip: '2', user: 'u' } },
    ];
    const changes: Record<string, { revision: number }> = fieldChanges(history);
    expect(changes.hostname.revision).toBe(2);
    expect(changes.comment.revision).toBe(2);
    expect(changes.ip.revision).toBe(1);
    expect(changes).not.toHaveProperty('user');
    expect(fieldChanges([])).toEqual({});
  });
});

describe('inventory mark', () => {
  it('filters, sorts and compares the mark', () => {
    const list = [device('A', { inventoried: true }), device('B', {}), device('C', { inventoried: false })];
    expect(isInventoried(list[0])).toBe(true);
    expect(isInventoried(list[1])).toBe(false);
    expect(filterDevices(list, { inventory: 'yes' }).map((d: { id: string }) => d.id)).toEqual(['A']);
    expect(filterDevices(list, { inventory: 'no' }).map((d: { id: string }) => d.id)).toEqual(['B', 'C']);
    expect(sortDevices(list, 'inventoried', 'desc')[0].id).toBe('A');
    expect(changedFields({}, { inventoried: false })).toEqual({});
    expect(changedFields({}, { inventoried: true })).toEqual({ inventoried: true });
    expect(changedFields({ inventoried: true }, { inventoried: true })).toEqual({});
    expect(changedFields({ inventoried: true }, { inventoried: false })).toEqual({ inventoried: false });
  });
});

describe('search in any alphabet', () => {
  it('finds Latin by Cyrillic and back', async () => {
    const { textMatches } = await import('../public/js/search-logic.js');
    expect(textMatches('IF-042-BOB', 'боб')).toBe(true);
    expect(textMatches('Бобров', 'bob')).toBe(true);
    expect(textMatches('vmyshko komp', 'мишко')).toBe(true);
    expect(textMatches('Мишко', 'mishko')).toBe(true);
    expect(textMatches('влад бункер', 'bunker')).toBe(true);
    expect(textMatches('Дмитрий', 'dmitriy')).toBe(true);
    expect(textMatches('Київ', 'kyiv')).toBe(true);
    expect(textMatches('Kharkiv', 'харків')).toBe(true);
    expect(textMatches('Tsentr', 'центр')).toBe(true);
    expect(textMatches('лука миша', 'kompik')).toBe(false);
  });

  it('keeps digits exact, so addresses do not match by accident', async () => {
    const { textMatches } = await import('../public/js/search-logic.js');
    expect(textMatches('10.100.22.131', '10.100')).toBe(true);
    expect(textMatches('10.10.22.131', '10.100')).toBe(false);
  });

  it('highlights the original text of a match', async () => {
    const { matchRanges, splitMatches } = await import('../public/js/search-logic.js');
    expect(matchRanges('IF-042-BOB', ['боб'])).toEqual([[7, 10]]);
    expect(matchRanges('vmyshko komp', ['мишко'])).toEqual([[1, 7]]);
    expect(matchRanges('Харків', ['kharkiv'])).toEqual([[0, 6]]);
    expect(matchRanges('Bob bob', ['bob'])).toEqual([[0, 3], [4, 7]]);
    expect(splitMatches('melnik s4 vaxa', ['мельник'])).toEqual([
      { text: 'melnik', match: true },
      { text: ' s4 vaxa', match: false },
    ]);
    expect(splitMatches('abc', [])).toEqual([{ text: 'abc', match: false }]);
  });

  it('filters the device list by a word in another alphabet', () => {
    const list = [device('A', { hostname: 'IF-042-BOB' }), device('B', { comment: 'юристка учбовий' })];
    expect(filterDevices(list, { query: 'боб' }).map((d: { id: string }) => d.id)).toEqual(['A']);
    expect(filterDevices(list, { query: 'yurystka' }).map((d: { id: string }) => d.id)).toEqual(['B']);
  });
});

describe('other spellings of ж, х, щ', () => {
  it('finds informal transliteration both ways', async () => {
    const { textMatches, matchRanges, wordKeys } = await import('../public/js/search-logic.js');
    expect(textMatches('juristka mujichekk sprava', 'мужичек')).toBe(true);
    expect(textMatches('юристка мужичек', 'mujichek')).toBe(true);
    expect(textMatches('melnik s4 vaxa comp', 'ваха')).toBe(true);
    expect(textMatches('ваха', 'vaxa')).toBe(true);
    expect(textMatches('Zhytomyr', 'житомир')).toBe(true);
    expect(textMatches('borsch', 'борщ')).toBe(true);
    expect(textMatches('лука миша', 'kompik')).toBe(false);
    expect(matchRanges('juristka mujichekk', ['мужичек'])).toEqual([[9, 18]]);
    // Many such letters do not explode the number of variants.
    expect(wordKeys('жжжжжжжжжжжж').length).toBeLessThanOrEqual(32);
  });
});

describe('journal lines', () => {
  const base = { at: '2026-11-21T10:00:00.000Z', actor: 'a@example.com', actorName: 'Аня', actorKey: null, source: 'web', details: null };

  it('describes device changes with a link to the card', () => {
    const line = journalLine({ ...base, kind: 'device', action: 'delete', target: 'X/1', details: { revision: 3, hostname: 'PC-7' } });
    expect(line).toEqual({ what: 'Запись удалена', object: 'PC-7', href: '#/devices/X%2F1' });
    expect(journalLine({ ...base, kind: 'device', action: 'create', target: 'X1', details: { revision: 1, hostname: null } }).object).toBe('X1');
  });

  it('describes keys, users and versions', () => {
    expect(journalLine({ ...base, kind: 'audit', action: 'key.revoke', target: 'k1', details: { label: 'Флешка', owner: 'b@example.com' } }))
      .toMatchObject({ what: 'Отозван ключ', object: '«Флешка», b@example.com' });
    expect(journalLine({ ...base, kind: 'audit', action: 'key.create', target: 'k1', details: { label: 'Флешка', owner: 'a@example.com' } }).object)
      .toBe('«Флешка»');
    expect(journalLine({ ...base, kind: 'audit', action: 'user.update', target: 'b@example.com', details: { role: ['viewer', 'editor'], active: [true, false] } }).object)
      .toBe('b@example.com: роль Просмотр → Редактор, отключён');
    expect(journalLine({ ...base, kind: 'suspector-release', action: 'publish', target: '2026.11.21.2', actor: 'GitHub Actions', actorName: null }))
      .toMatchObject({ what: 'Опубликована версия SUSpector', object: '2026.11.21.2' });
    expect(journalLine({ ...base, kind: 'settings', action: 'update', target: 'network' }).object).toBe('Сеть');
  });

  it('names the console key before the person', () => {
    expect(journalActor({ ...base, actorKey: 'Флешка' })).toBe('ключ «Флешка»');
    expect(journalActor(base)).toBe('Аня');
    expect(journalActor({ ...base, actor: 'GitHub Actions', actorName: null })).toBe('GitHub Actions');
  });
});

describe('reserved addresses', () => {
  it('are one per line with an optional note', () => {
    const list = parseReserved('10.0.0.5 — сервер\n\n 10.0.0.6\n10.0.0.7: камера - двор\n10.0.0.8 - принтер');
    expect(list).toEqual([
      { address: '10.0.0.5', note: 'сервер' },
      { address: '10.0.0.6', note: '' },
      { address: '10.0.0.7', note: 'камера - двор' },
      { address: '10.0.0.8', note: 'принтер' },
    ]);
    expect(parseReserved(formatReserved(list))).toEqual(list);
    expect(formatReserved(undefined)).toBe('');
  });

  it('show the same mistakes as the server', () => {
    expect(reservedProblems('10.0.0.5 — a\n10.0.0.6')).toEqual([]);
    expect(reservedProblems('10.0.0.256')).toHaveLength(1);
    expect(reservedProblems('10.0.0.5\n10.0.0.5')).toHaveLength(1);
    expect(reservedProblems('10.0.0.5 ' + 'x'.repeat(201))).toHaveLength(1);
  });
});

describe('people', () => {
  const people = [
    { id: 1, name: 'Іваненко Іван', note: 'S1', deleted: false },
    { id: 2, name: 'Петренко Петро', note: '', deleted: false },
    { id: 3, name: 'Петренко Петро', note: 'другий', deleted: false },
    { id: 4, name: 'Сидоренко Сидір', note: '', deleted: true },
  ];

  it('are picked by name, by number or added as new', () => {
    expect(parsePerson('', people)).toEqual({ id: null });
    expect(parsePerson('  іваненко   іван ', people)).toEqual({ id: 1 });
    expect(parsePerson('Петренко Петро · #3', people)).toEqual({ id: 3 });
    expect(parsePerson('Петренко Петро', people).error).toBeTruthy();
    expect(parsePerson('#4', people).error).toBeTruthy();
    expect(parsePerson('Сидоренко  Сидір', people)).toEqual({ newName: 'Сидоренко Сидір' });
  });

  it('are compared, sorted and found by name', () => {
    expect(changedFields({ userId: 1 }, { userId: 1, ownerId: null })).toEqual({});
    expect(changedFields({ userId: 1 }, { userId: null, ownerId: 2 })).toEqual({ userId: null, ownerId: 2 });

    setPeople(people);
    expect(personName(2)).toBe('Петренко Петро');
    expect(personName(99)).toBe('#99');
    expect(personName(null)).toBe('');
    const list = [device('A', { userId: 2 }), device('B', { userId: 1 }), device('C', {})];
    expect(sortDevices(list, 'userId').map((d: { id: string }) => d.id)).toEqual(['B', 'A', 'C']);
    expect(filterDevices(list, { query: 'іваненко' }).map((d: { id: string }) => d.id)).toEqual(['B']);
    setPeople([]);
    expect(filterDevices(list, { query: 'іваненко' })).toEqual([]);
  });

  it('know the devices that link them', () => {
    const links = personLinks([device('A', { userId: 1, ownerId: 2 }), device('B', { ownerId: 2 }), device('C', { userId: 1 }, { deleted: true })]);
    expect(links.get(1).userId.map((d: { id: string }) => d.id)).toEqual(['A']);
    expect(links.get(2).ownerId.map((d: { id: string }) => d.id)).toEqual(['A', 'B']);
    expect(links.has(3)).toBe(false);
  });

  it('show up in the journal', () => {
    const entry = { kind: 'audit', category: 'devices', actor: 'a@example.com', actorName: 'Аня', actorKey: null, source: 'web', at: '' };
    expect(journalLine({ ...entry, action: 'person.create', target: '7', details: { name: 'Іваненко Іван' } })).toMatchObject({ what: 'Добавлен человек', object: 'Іваненко Іван · #7', href: '#/people/7' });
    expect(journalLine({ ...entry, action: 'person.update', target: '7', details: { note: ['', 'S1'] } }).object).toBe('#7: примечание «» → «S1»');
  });
});

describe('property items', () => {
  const item = (id: number, extra: Record<string, unknown>) => ({ id, type: 'Монітор', model: '', serial: '', location: '', ownerId: null, note: '', ...extra });
  const items = [
    item(1, { model: 'Dell P2217H', serial: 'CN-0RH81R', ownerId: 1 }),
    item(2, { type: 'Флешка', model: 'Goodram 32GB', ownerId: 2 }),
    item(3, { type: 'Флешка', note: 'сервер тоц' }),
  ];

  it('are filtered by type, person and words, and counted by type', () => {
    setPeople([{ id: 1, name: 'Лукашик', note: '', deleted: false }, { id: 2, name: 'Маркелов', note: '', deleted: false }]);
    expect(itemTypes(items)).toEqual([['Флешка', 2], ['Монітор', 1]]);
    const ids = (list: { id: number }[]) => list.map((i) => i.id);
    expect(ids(filterItems(items, { type: 'Флешка' }))).toEqual([2, 3]);
    expect(ids(filterItems(items, { ownerId: 2 }))).toEqual([2]);
    expect(ids(filterItems(items, { query: 'лукашик dell' }))).toEqual([1]);
    expect(ids(filterItems(items, { query: 'тоц' }))).toEqual([3]);
    expect(ids(filterItems(items, { query: '#2' }))).toEqual([2]);
  });

  it('show up in the journal', () => {
    const entry = { kind: 'audit', category: 'devices', actor: 'a@example.com', actorName: 'Аня', actorKey: null, source: 'web', at: '' };
    expect(journalLine({ ...entry, action: 'item.create', target: '7', details: { type: 'Монітор', model: 'Dell', serial: 'CN1' } }).object).toBe('Монітор Dell, CN1 · #7');
    expect(journalLine({ ...entry, action: 'item.update', target: '7', details: { type: 'Монітор', ownerId: [1, null], note: ['', 'x'] } }).object)
      .toBe('Монітор #7: МОЛ Лукашик → —, примечание «» → «x»');
    setPeople([]);
  });
});
