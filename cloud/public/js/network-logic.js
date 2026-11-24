// Pure helpers for the network settings page; covered by test/web-logic.test.ts.
// Form values may be strings (while typing) or numbers (as saved).
import { f, t } from './i18n.js';

const IPV4 = /^(25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)(\.(25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)){3}$/;

export function emptyLocation() {
  return { name: '', tk: '', thirdOctet: '', prefixLength: 25, gatewayOctet: '', firstHostOctet: '', lastHostOctet: '', dnsServers: [], segments: [] };
}

// ---------- Reserved addresses ----------
// The page edits them as table rows { address, lastAddress, note } of strings; lastAddress is the end of a range
// (empty for one address) and may be only its last octet: "10.0.0.20" or "20".

export const MAX_RESERVED = 1000;
export const MAX_RESERVED_RANGE = 1024;

// IPv4 as a number, or null.
export function ipNumber(address) {
  const text = String(address ?? '').trim();
  if (!IPV4.test(text)) return null;
  return text.split('.').reduce((sum, part) => sum * 256 + Number(part), 0);
}

// The full end address of a row: "20" becomes "10.0.0.20" after the address 10.0.0.5; '' when there is none.
export function rangeEnd(address, last) {
  const end = String(last ?? '').trim();
  const start = String(address ?? '').trim();
  if (/^\d{1,3}$/.test(end) && IPV4.test(start)) return start.replace(/\d+$/, end);
  return end;
}

// The saved list as table rows, by address.
export function reservedRows(list) {
  return [...(list ?? [])]
    .sort((a, b) => (ipNumber(a.address) ?? Infinity) - (ipNumber(b.address) ?? Infinity))
    .map((item) => ({ address: item.address, lastAddress: item.lastAddress ?? '', note: item.note ?? '' }));
}

// The rows to save: empty rows left out, a range end written in full, a one-address range as one address.
export function reservedValue(rows) {
  return (rows ?? [])
    .filter((row) => String(row.address ?? '').trim() !== '' || String(row.note ?? '').trim() !== '')
    .map((row) => {
      const address = String(row.address ?? '').trim();
      const last = rangeEnd(address, row.lastAddress);
      const note = String(row.note ?? '').trim();
      return last !== '' && last !== address ? { address, lastAddress: last, note } : { address, note };
    });
}

// How many addresses a row takes; null while it is not valid.
export function rangeSize(row) {
  const from = ipNumber(row.address);
  const last = rangeEnd(row.address, row.lastAddress);
  const to = last === '' ? from : ipNumber(last);
  if (from === null || to === null || to < from) return null;
  return to - from + 1;
}

// Pasted lines as rows: "10.0.0.5 — сервер", "10.0.0.20-10.0.0.29 камеры", "10.0.0.40 - 49: принтеры".
export function parseReservedLines(text) {
  return String(text ?? '')
    .split(/\r?\n/)
    .map((line) => line.trim())
    .filter(Boolean)
    .map((line) => {
      const match = /^([\d.]+)(?:\s*[-–]\s*([\d.]+)(?![\d.]))?\s*(?:[—–:-]\s*)?(.*)$/.exec(line);
      if (!match) return { address: line, lastAddress: '', note: '' };
      return { address: match[1], lastAddress: match[2] ?? '', note: match[3].trim() };
    });
}

// Same rules as validateNetworkSettings on the server: { marks: Map(row index → text), problems: [text] }.
export function checkReserved(rows) {
  const marks = new Map();
  const spans = [];
  let count = 0;
  (rows ?? []).forEach((row, index) => {
    const address = String(row.address ?? '').trim();
    const last = rangeEnd(address, row.lastAddress);
    const note = String(row.note ?? '').trim();
    if (address === '' && note === '' && last === '') return;
    count++;
    const from = ipNumber(address);
    const to = last === '' ? from : ipNumber(last);
    let mark = null;
    if (from === null) mark = address === '' ? t('Укажите адрес.') : f('«{0}» — не IPv4-адрес.', address);
    else if (to === null) mark = f('Конец диапазона «{0}» — не IPv4-адрес и не последний октет.', last);
    else if (to < from) mark = t('Конец диапазона меньше начала.');
    else if (to - from + 1 > MAX_RESERVED_RANGE) mark = f('Диапазон больше {0} адресов.', MAX_RESERVED_RANGE);
    else {
      const other = spans.find((span) => from <= span.to && to >= span.from);
      if (other) mark = f('Пересекается со строкой {0}.', other.index + 1);
      else spans.push({ from, to, index });
    }
    if (!mark && note.length > 200) mark = t('Примечание длиннее 200 символов.');
    if (mark) marks.set(index, mark);
  });
  const problems = [...marks].map(([index, text]) => f('Строка {0}: {1}', index + 1, text));
  if (count > MAX_RESERVED) problems.push(f('Не больше {0} строк.', MAX_RESERVED));
  return { marks, problems };
}

// "111, 112 113" -> [111, 112, 113]; anything that is not a number becomes NaN for the server to reject.
export function parseNumberList(text) {
  return String(text ?? '')
    .split(/[\s,;]+/)
    .filter(Boolean)
    .map((item) => (/^\d+$/.test(item) ? Number(item) : Number.NaN));
}

// "10.0.0.1, 10.0.0.2" -> ['10.0.0.1', '10.0.0.2'].
export function parseAddressList(text) {
  return String(text ?? '')
    .split(/[\s,;]+/)
    .filter(Boolean);
}

// An octet typed in a form: 0-255, or null when empty or invalid.
export function parseOctet(value) {
  const text = String(value ?? '').trim();
  if (!/^\d{1,3}$/.test(text)) return null;
  const number = Number(text);
  return number <= 255 ? number : null;
}

export function subnetMask(prefixLength) {
  const bits = Number(prefixLength);
  if (!Number.isInteger(bits) || bits < 0 || bits > 32) return '';
  const mask = bits === 0 ? 0 : (0xffffffff << (32 - bits)) >>> 0;
  return [24, 16, 8, 0].map((shift) => (mask >>> shift) & 255).join('.');
}

// The gateway's subnet inside the last octet: network and broadcast octets, usable hosts.
export function subnetInfo(gatewayOctet, prefixLength) {
  const gateway = parseOctet(gatewayOctet);
  const bits = Number(prefixLength);
  if (gateway === null || !Number.isInteger(bits) || bits < 24 || bits > 30) return null;
  const size = 2 ** (32 - bits);
  const network = gateway - (gateway % size);
  return { network, broadcast: network + size - 1, hosts: size - 2 };
}

// All usable addresses of the gateway's subnet except the gateway itself.
export function suggestHostRange(gatewayOctet, prefixLength) {
  if (!Number.isInteger(gatewayOctet) || !Number.isInteger(prefixLength) || prefixLength < 24 || prefixLength > 30) return null;
  if (gatewayOctet < 0 || gatewayOctet > 255) return null;
  const size = 2 ** (32 - prefixLength);
  const network = gatewayOctet - (gatewayOctet % size);
  const first = network + 1;
  const last = network + size - 2;
  if (gatewayOctet === first) return { firstHostOctet: first + 1, lastHostOctet: last };
  if (gatewayOctet === last) return { firstHostOctet: first, lastHostOctet: last - 1 };
  return null;
}

// The range the page would fill in by itself, for form values.
export function autoHostRange(location) {
  const gateway = parseOctet(location.gatewayOctet);
  return gateway === null ? null : suggestHostRange(gateway, Number(location.prefixLength));
}

// True when the saved range is exactly the automatic one, so the page can keep it in step.
export function isAutoRange(location) {
  const range = autoHostRange(location);
  return (
    range !== null &&
    parseOctet(location.firstHostOctet) === range.firstHostOctet &&
    parseOctet(location.lastHostOctet) === range.lastHostOctet
  );
}

function dnsProblem(text, where) {
  const servers = parseAddressList(text);
  if (servers.length === 0 || servers.length > 4) return f('{0}: от 1 до 4 адресов.', where);
  if (servers.some((server) => !IPV4.test(server))) return f('{0}: нужны IPv4-адреса вида 10.0.0.1.', where);
  return null;
}

// DNS shared by most locations: the page shows it once and leaves those locations empty.
export function commonDns(locations) {
  const counts = new Map();
  for (const location of locations) {
    const key = (location.dnsServers ?? []).join(', ');
    if (key) counts.set(key, (counts.get(key) ?? 0) + 1);
  }
  let best = '';
  for (const [key, count] of counts) if (count > (counts.get(best) ?? 0)) best = key;
  return best;
}

function segmentProblem(text, where) {
  const segments = parseNumberList(text);
  if (segments.length === 0 || segments.length > 16) return f('{0}: от 1 до 16 чисел через запятую.', where);
  if (segments.some((segment) => !Number.isInteger(segment) || segment > 255)) return f('{0}: числа от 0 до 255.', where);
  if (new Set(segments).size !== segments.length) return f('{0}: числа повторяются.', where);
  return null;
}

// Same rules as validateNetworkSettings on the server, as messages for the form.
export function generalProblems(draft) {
  const problems = [];
  if (parseOctet(draft.firstOctet) === null) problems.push(t('Первый октет: число от 0 до 255.'));
  const segments = segmentProblem(draft.segments, t('Сегменты'));
  if (segments) problems.push(segments);
  const dns = dnsProblem(draft.dnsServers, 'DNS');
  if (dns) problems.push(dns);
  return problems;
}

export function locationProblems(location, allNames = []) {
  const problems = [];
  const name = String(location.name ?? '').trim();
  if (name === '') problems.push(t('Укажите название.'));
  else if (name.length > 60) problems.push(t('Название длиннее 60 символов.'));
  else if (allNames.filter((other) => String(other ?? '').trim().toLowerCase() === name.toLowerCase()).length > 1) {
    problems.push(t('Такое название уже есть.'));
  }

  if (String(location.tk ?? '').trim().length > 20) problems.push(t('Номер ТК длиннее 20 символов.'));
  if (parseOctet(location.thirdOctet) === null) problems.push(t('Третий октет подсети: число от 0 до 255.'));

  const info = subnetInfo(location.gatewayOctet, location.prefixLength);
  const gateway = parseOctet(location.gatewayOctet);
  if (gateway === null) problems.push(t('Шлюз: число от 0 до 255.'));
  else if (info && (gateway === info.network || gateway === info.broadcast)) {
    problems.push(f('Шлюз не может быть адресом сети или широковещательным (.{0}, .{1}).', info.network, info.broadcast));
  }

  const first = parseOctet(location.firstHostOctet);
  const last = parseOctet(location.lastHostOctet);
  if (first === null || last === null) problems.push(t('Адреса компьютеров: числа от 0 до 255.'));
  else if (first > last) problems.push(t('Первый адрес больше последнего.'));
  else if (info && (first <= info.network || last >= info.broadcast)) {
    problems.push(f('Адреса компьютеров должны быть внутри подсети шлюза: от .{0} до .{1}.', info.network + 1, info.broadcast - 1));
  } else if (gateway !== null && gateway >= first && gateway <= last) {
    problems.push(t('Шлюз попадает в диапазон адресов компьютеров.'));
  }

  // Empty means the shared DNS, checked with the shared part of the address.
  if (String(location.dnsServers ?? '').trim() !== '') {
    const dns = dnsProblem(location.dnsServers, t('Свои DNS'));
    if (dns) problems.push(dns);
  }

  if (String(location.segments ?? '').trim() !== '') {
    const segments = segmentProblem(location.segments, t('Свои сегменты'));
    if (segments) problems.push(segments);
  }
  return problems;
}
