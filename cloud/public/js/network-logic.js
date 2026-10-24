// Pure helpers for the network settings page; covered by test/web-logic.test.ts.

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

export function subnetMask(prefixLength) {
  const bits = Number(prefixLength);
  if (!Number.isInteger(bits) || bits < 0 || bits > 32) return '';
  const mask = bits === 0 ? 0 : (0xffffffff << (32 - bits)) >>> 0;
  return [24, 16, 8, 0].map((shift) => (mask >>> shift) & 255).join('.');
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

// One-line example of what the console will configure, e.g. "10.20.5.130–10.20.5.254, шлюз 10.20.5.129, маска 255.255.255.128".
export function describeLocation(firstOctet, segment, location) {
  const prefix = `${Number.isFinite(firstOctet) ? firstOctet : '?'}.${Number.isFinite(segment) ? segment : '?'}.${location.thirdOctet === '' ? '?' : location.thirdOctet}`;
  const value = (octet) => (octet === '' || octet === undefined || octet === null ? '?' : octet);
  return `${prefix}.${value(location.firstHostOctet)}–${prefix}.${value(location.lastHostOctet)}, шлюз ${prefix}.${value(location.gatewayOctet)}, маска ${subnetMask(location.prefixLength) || '?'}`;
}
