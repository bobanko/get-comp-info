// Shared settings (currently the network profiles used by the LAN adapter setup and quick ping).
// Stored in the cloud only, so the public repository holds no internal addresses.
import { fail, isPlainObject, nowIso } from './http';
import type { User } from './types';

export const NETWORK_KEY = 'network';

export interface NetworkLocation {
  name: string;
  thirdOctet: number;
  prefixLength: number;
  gatewayOctet: number;
  firstHostOctet: number;
  lastHostOctet: number;
  dnsServers: string[];
  // Second-octet options for this location only; when absent the common `segments` apply.
  segments?: number[];
}

export interface NetworkSettings {
  firstOctet: number;
  // Second-octet options; the first one is offered as the default.
  segments: number[];
  // Host pinged by quick ping when the computer is in one of the networks; null skips the check.
  domainCheckHost: string | null;
  locations: NetworkLocation[];
}

export interface SettingRecord<T> {
  key: string;
  revision: number;
  updatedAt: string;
  updatedBy: string;
  value: T;
}

const IPV4 = /^(25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)(\.(25[0-5]|2[0-4]\d|1\d\d|[1-9]?\d)){3}$/;
const HOST = /^(?=.{1,253}$)[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?(\.[a-z0-9]([a-z0-9-]{0,61}[a-z0-9])?)*$/i;

function octet(value: unknown, where: string): number {
  if (!Number.isInteger(value) || (value as number) < 0 || (value as number) > 255) {
    fail(400, 'invalid_settings', `${where}: нужно целое число от 0 до 255.`);
  }
  return value as number;
}

function segmentList(value: unknown, where: string): number[] {
  if (!Array.isArray(value) || value.length === 0 || value.length > 16) {
    fail(400, 'invalid_settings', `${where}: нужен список от 1 до 16 сегментов.`);
  }
  const segments = value.map((item, index) => octet(item, `${where}, сегмент ${index + 1}`));
  if (new Set(segments).size !== segments.length) fail(400, 'invalid_settings', `${where}: сегменты повторяются.`);
  return segments;
}

// Checks the whole document and returns it with only the known fields.
export function validateNetworkSettings(value: unknown): NetworkSettings {
  if (!isPlainObject(value)) fail(400, 'invalid_settings', 'Настройки сети должны быть JSON-объектом.');
  const firstOctet = octet(value.firstOctet, 'Первый октет');
  const segments = segmentList(value.segments, 'Сегменты сети');

  let domainCheckHost: string | null = null;
  if (value.domainCheckHost !== null && value.domainCheckHost !== undefined && value.domainCheckHost !== '') {
    const host = typeof value.domainCheckHost === 'string' ? value.domainCheckHost.trim() : '';
    if (!HOST.test(host) && !IPV4.test(host)) fail(400, 'invalid_settings', 'Адрес для проверки домена: нужно имя узла или IPv4-адрес.');
    domainCheckHost = host;
  }

  if (!Array.isArray(value.locations) || value.locations.length === 0 || value.locations.length > 50) {
    fail(400, 'invalid_settings', 'Нужен список от 1 до 50 корпусов.');
  }
  const names = new Set<string>();
  const locations = value.locations.map((raw: unknown, index: number): NetworkLocation => {
    if (!isPlainObject(raw)) fail(400, 'invalid_settings', `Корпус ${index + 1}: нужен JSON-объект.`);
    const name = typeof raw.name === 'string' ? raw.name.trim() : '';
    const where = `Корпус «${name || index + 1}»`;
    if (name === '' || name.length > 60) fail(400, 'invalid_settings', `Корпус ${index + 1}: название обязательно, не длиннее 60 символов.`);
    if (names.has(name.toLowerCase())) fail(400, 'invalid_settings', `${where}: такое название уже есть.`);
    names.add(name.toLowerCase());

    const prefixLength = raw.prefixLength;
    if (!Number.isInteger(prefixLength) || (prefixLength as number) < 24 || (prefixLength as number) > 30) {
      fail(400, 'invalid_settings', `${where}: длина префикса должна быть от 24 до 30.`);
    }
    const thirdOctet = octet(raw.thirdOctet, `${where}, третий октет`);
    const gatewayOctet = octet(raw.gatewayOctet, `${where}, шлюз`);
    const firstHostOctet = octet(raw.firstHostOctet, `${where}, первый адрес`);
    const lastHostOctet = octet(raw.lastHostOctet, `${where}, последний адрес`);

    // The host range must lie inside the gateway's subnet, without the network and broadcast addresses.
    const size = 2 ** (32 - (prefixLength as number));
    const network = gatewayOctet - (gatewayOctet % size);
    const broadcast = network + size - 1;
    if (gatewayOctet === network || gatewayOctet === broadcast) {
      fail(400, 'invalid_settings', `${where}: шлюз не может быть адресом сети или широковещательным адресом (${network}–${broadcast}).`);
    }
    if (firstHostOctet > lastHostOctet) fail(400, 'invalid_settings', `${where}: первый адрес больше последнего.`);
    if (firstHostOctet <= network || lastHostOctet >= broadcast) {
      fail(400, 'invalid_settings', `${where}: адреса должны быть внутри подсети шлюза, от ${network + 1} до ${broadcast - 1}.`);
    }
    if (gatewayOctet >= firstHostOctet && gatewayOctet <= lastHostOctet) {
      fail(400, 'invalid_settings', `${where}: шлюз не должен входить в диапазон адресов компьютеров.`);
    }

    if (!Array.isArray(raw.dnsServers) || raw.dnsServers.length === 0 || raw.dnsServers.length > 4) {
      fail(400, 'invalid_settings', `${where}: нужно от 1 до 4 DNS-серверов.`);
    }
    const dnsServers = raw.dnsServers.map((server: unknown) => {
      const text = typeof server === 'string' ? server.trim() : '';
      if (!IPV4.test(text)) fail(400, 'invalid_settings', `${where}: DNS-сервер «${String(server)}» не является IPv4-адресом.`);
      return text;
    });

    const location: NetworkLocation = { name, thirdOctet, prefixLength: prefixLength as number, gatewayOctet, firstHostOctet, lastHostOctet, dnsServers };
    if (raw.segments !== undefined && raw.segments !== null && !(Array.isArray(raw.segments) && raw.segments.length === 0)) {
      location.segments = segmentList(raw.segments, `${where}, свои сегменты`);
    }
    return location;
  });
  return { firstOctet, segments, domainCheckHost, locations };
}

interface SettingRow {
  key: string;
  value: string;
  revision: number;
  updated_at: string;
  updated_by: string;
}

export async function getSetting<T>(db: D1Database, key: string): Promise<SettingRecord<T> | null> {
  const row = await db.prepare('SELECT * FROM settings WHERE key = ?').bind(key).first<SettingRow>();
  if (!row) return null;
  return { key: row.key, revision: row.revision, updatedAt: row.updated_at, updatedBy: row.updated_by, value: JSON.parse(row.value) as T };
}

export async function getSettingHistory(db: D1Database, key: string) {
  const { results } = await db
    .prepare('SELECT revision, value, changed_at, changed_by FROM settings_history WHERE key = ? ORDER BY revision DESC LIMIT 50')
    .bind(key)
    .all<{ revision: number; value: string; changed_at: string; changed_by: string }>();
  return results.map((row) => ({ revision: row.revision, changedAt: row.changed_at, changedBy: row.changed_by, value: JSON.parse(row.value) }));
}

// Saves a new revision; baseRevision must be the revision the editor started from (null when creating).
export async function putSetting<T>(
  db: D1Database,
  key: string,
  baseRevision: number | null,
  value: T,
  user: User,
): Promise<{ status: 'ok'; record: SettingRecord<T> } | { status: 'conflict'; current: SettingRecord<T> | null }> {
  const current = await getSetting<T>(db, key);
  if ((current?.revision ?? null) !== baseRevision) return { status: 'conflict', current };
  const revision = (current?.revision ?? 0) + 1;
  const json = JSON.stringify(value);
  const now = nowIso();
  try {
    await db.batch([
      db
        .prepare('INSERT INTO settings_history (key, revision, value, changed_at, changed_by) VALUES (?, ?, ?, ?, ?)')
        .bind(key, revision, json, now, user.email),
      db
        .prepare(
          `INSERT INTO settings (key, value, revision, updated_at, updated_by) VALUES (?1, ?2, ?3, ?4, ?5)
           ON CONFLICT(key) DO UPDATE SET value = ?2, revision = ?3, updated_at = ?4, updated_by = ?5`,
        )
        .bind(key, json, revision, now, user.email),
    ]);
  } catch (error) {
    // Someone saved the same revision first.
    if (String(error).includes('UNIQUE')) return { status: 'conflict', current: await getSetting<T>(db, key) };
    throw error;
  }
  return { status: 'ok', record: { key, revision, updatedAt: now, updatedBy: user.email, value } };
}
