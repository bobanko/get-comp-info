// Pure helpers for the network settings page; covered by test/web-logic.test.ts.
// Form values may be strings (while typing) or numbers (as saved).

const IPV4 = /^(25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)(\.(25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)){3}$/;

export function emptyLocation() {
  return { name: '', thirdOctet: '', prefixLength: 25, gatewayOctet: '', firstHostOctet: '', lastHostOctet: '', dnsServers: [], segments: [] };
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
  if (servers.length === 0 || servers.length > 4) return `${where}: от 1 до 4 адресов.`;
  if (servers.some((server) => !IPV4.test(server))) return `${where}: нужны IPv4-адреса вида 10.0.0.1.`;
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
  if (segments.length === 0 || segments.length > 16) return `${where}: от 1 до 16 чисел через запятую.`;
  if (segments.some((segment) => !Number.isInteger(segment) || segment > 255)) return `${where}: числа от 0 до 255.`;
  if (new Set(segments).size !== segments.length) return `${where}: числа повторяются.`;
  return null;
}

// Same rules as validateNetworkSettings on the server, as messages for the form.
export function generalProblems(draft) {
  const problems = [];
  if (parseOctet(draft.firstOctet) === null) problems.push('Первый октет: число от 0 до 255.');
  const segments = segmentProblem(draft.segments, 'Сегменты');
  if (segments) problems.push(segments);
  const dns = dnsProblem(draft.dnsServers, 'DNS');
  if (dns) problems.push(dns);
  return problems;
}

export function locationProblems(location, allNames = []) {
  const problems = [];
  const name = String(location.name ?? '').trim();
  if (name === '') problems.push('Укажите название.');
  else if (name.length > 60) problems.push('Название длиннее 60 символов.');
  else if (allNames.filter((other) => String(other ?? '').trim().toLowerCase() === name.toLowerCase()).length > 1) {
    problems.push('Такое название уже есть.');
  }

  if (parseOctet(location.thirdOctet) === null) problems.push('Третий октет подсети: число от 0 до 255.');

  const info = subnetInfo(location.gatewayOctet, location.prefixLength);
  const gateway = parseOctet(location.gatewayOctet);
  if (gateway === null) problems.push('Шлюз: число от 0 до 255.');
  else if (info && (gateway === info.network || gateway === info.broadcast)) {
    problems.push(`Шлюз не может быть адресом сети или широковещательным (.${info.network}, .${info.broadcast}).`);
  }

  const first = parseOctet(location.firstHostOctet);
  const last = parseOctet(location.lastHostOctet);
  if (first === null || last === null) problems.push('Адреса компьютеров: числа от 0 до 255.');
  else if (first > last) problems.push('Первый адрес больше последнего.');
  else if (info && (first <= info.network || last >= info.broadcast)) {
    problems.push(`Адреса компьютеров должны быть внутри подсети шлюза: от .${info.network + 1} до .${info.broadcast - 1}.`);
  } else if (gateway !== null && gateway >= first && gateway <= last) {
    problems.push('Шлюз попадает в диапазон адресов компьютеров.');
  }

  // Empty means the shared DNS, checked with the shared part of the address.
  if (String(location.dnsServers ?? '').trim() !== '') {
    const dns = dnsProblem(location.dnsServers, 'Свои DNS');
    if (dns) problems.push(dns);
  }

  if (String(location.segments ?? '').trim() !== '') {
    const segments = segmentProblem(location.segments, 'Свои сегменты');
    if (segments) problems.push(segments);
  }
  return problems;
}
