// Program releases: published by GitHub Actions, downloaded by the console, managed by admins.
import { Hono } from 'hono';
import { createMiddleware } from 'hono/factory';
import { createLocalJWKSet, createRemoteJWKSet, jwtVerify, type JSONWebKeySet, type JWTVerifyGetKey } from 'jose';
import { sha256Hex } from './crypto';
import { fail, nowIso } from './http';
import type { AppEnv } from './types';
import { readZip, ZipError } from './zip';

export const MAX_PACKAGE_BYTES = 1024 * 1024;
export const GITHUB_ISSUER = 'https://token.actions.githubusercontent.com';
export const RELEASE_AUDIENCE = 'comp-info-release';
const VERSION_PATTERN = /^\d{4}\.\d{2}\.\d{2}\.\d{1,4}$/;
// Only files from the program root: scripts, launchers, the window program and the cloud settings.
const PROGRAM_FILE = /^[^\\/:*?"<>|\u0000-\u001f]+\.(ps1|bat|cmd)$/i;

export interface ManifestFile {
  path: string;
  sha256: string;
  size: number;
}

export interface Manifest {
  version: string;
  commit: string;
  publishedAt?: string;
  notes: string[];
  files: ManifestFile[];
}

interface ReleaseRow {
  version: string;
  commit_sha: string;
  manifest: string;
  sha256: string;
  size: number;
  published_at: string;
  is_current: number;
}

export function isProgramFile(path: string): boolean {
  return path === 'cloud-config.json' || path.toLowerCase() === 'comp-info.exe' || PROGRAM_FILE.test(path);
}

export function isSuspectorFile(path: string): boolean {
  return path.toLowerCase() === 'suspector.exe';
}

function toRelease(row: ReleaseRow) {
  const manifest = JSON.parse(row.manifest) as Manifest;
  return {
    version: row.version,
    sha256: row.sha256,
    size: row.size,
    publishedAt: row.published_at,
    commit: row.commit_sha,
    notes: manifest.notes ?? [],
    isCurrent: row.is_current === 1,
  };
}

const RELEASE_COLUMNS = 'version, commit_sha, manifest, sha256, size, published_at, is_current';

// Checks the archive against its manifest.json and returns the manifest.
export async function validatePackage(bytes: Uint8Array, allowedFile: (path: string) => boolean = isProgramFile): Promise<Manifest> {
  if (bytes.byteLength > MAX_PACKAGE_BYTES) fail(413, 'too_large', 'The release archive is larger than 1 MB.');
  let entries;
  try {
    entries = await readZip(bytes);
  } catch (error) {
    if (error instanceof ZipError) fail(400, 'invalid_package', error.message);
    throw error;
  }
  const byName = new Map(entries.map((entry) => [entry.name, entry.data]));
  const manifestData = byName.get('manifest.json');
  if (!manifestData) fail(400, 'invalid_package', 'The archive has no manifest.json.');

  let manifest: Manifest;
  try {
    manifest = JSON.parse(new TextDecoder().decode(manifestData).replace(/^﻿/, '')) as Manifest;
  } catch {
    fail(400, 'invalid_manifest', 'manifest.json is not JSON.');
  }
  if (typeof manifest.version !== 'string' || !VERSION_PATTERN.test(manifest.version)) {
    fail(400, 'invalid_version', 'The version must look like YYYY.MM.DD.N.');
  }
  if (typeof manifest.commit !== 'string' || manifest.commit === '') fail(400, 'invalid_manifest', 'manifest.json has no commit.');
  if (manifest.notes !== undefined && !(Array.isArray(manifest.notes) && manifest.notes.every((note) => typeof note === 'string'))) {
    fail(400, 'invalid_manifest', 'notes in manifest.json must be a list of strings.');
  }
  if (!Array.isArray(manifest.files) || manifest.files.length === 0) fail(400, 'invalid_manifest', 'manifest.json lists no files.');

  const listed = new Set<string>();
  for (const file of manifest.files) {
    if (typeof file?.path !== 'string' || !allowedFile(file.path)) {
      fail(400, 'invalid_manifest', `File not allowed in manifest.json: ${String(file?.path)}`);
    }
    if (listed.has(file.path.toLowerCase())) fail(400, 'invalid_manifest', `File listed twice: ${file.path}`);
    listed.add(file.path.toLowerCase());
    const data = byName.get(file.path);
    if (!data) fail(400, 'invalid_package', `The archive has no file ${file.path}.`);
    if (data.byteLength !== file.size || (await sha256Hex(data)) !== String(file.sha256).toLowerCase()) {
      fail(400, 'invalid_package', `The file ${file.path} does not match manifest.json.`);
    }
  }
  for (const name of byName.keys()) {
    if (name !== 'manifest.json' && !listed.has(name.toLowerCase())) {
      fail(400, 'invalid_package', `The file ${name} is not in manifest.json.`);
    }
  }
  return { ...manifest, notes: manifest.notes ?? [] };
}

export async function getCurrentRelease(db: D1Database) {
  const row = await db.prepare(`SELECT ${RELEASE_COLUMNS} FROM program_releases WHERE is_current = 1`).first<ReleaseRow>();
  return row ? toRelease(row) : null;
}

// The first version whose updater accepts comp-info.exe in the archive; older ones reject such a release.
export const FIRST_EXE_UPDATER = '2026.11.06.1';

function compareVersions(a: string, b: string): number {
  const x = a.split('.').map(Number);
  const y = b.split('.').map(Number);
  for (let i = 0; i < Math.max(x.length, y.length); i++) {
    const d = (x[i] ?? 0) - (y[i] ?? 0);
    if (d !== 0) return d;
  }
  return 0;
}

function hasWindowsApp(row: ReleaseRow): boolean {
  const manifest = JSON.parse(row.manifest) as Manifest;
  return (manifest.files ?? []).some((file) => file.path.toLowerCase() === 'comp-info.exe');
}

// Version of the program that sent the request ("comp-info/2026.11.06.1" in User-Agent), or null.
export function clientVersion(userAgent: string | undefined): string | null {
  const match = /^comp-info\/(\d{4}\.\d{2}\.\d{2}\.\d{1,4})\b/.exec(userAgent ?? '');
  return match ? match[1] : null;
}

// The release a console should install. An updater older than FIRST_EXE_UPDATER gets the newest release
// (not newer than the current one) without comp-info.exe: it updates to that first and to the current one next.
export async function getReleaseFor(db: D1Database, version: string | null) {
  const current = await db.prepare(`SELECT ${RELEASE_COLUMNS} FROM program_releases WHERE is_current = 1`).first<ReleaseRow>();
  if (!current) return null;
  if ((version && compareVersions(version, FIRST_EXE_UPDATER) >= 0) || !hasWindowsApp(current)) return toRelease(current);
  const { results } = await db.prepare(`SELECT ${RELEASE_COLUMNS} FROM program_releases`).all<ReleaseRow>();
  const fallback = results
    .filter((row) => compareVersions(row.version, current.version) <= 0 && !hasWindowsApp(row))
    .sort((a, b) => compareVersions(b.version, a.version))[0];
  return toRelease(fallback ?? current);
}

type ReleaseTable = 'program_releases' | 'suspector_releases';

export async function listReleases(db: D1Database, table: ReleaseTable = 'program_releases') {
  const { results } = await db.prepare(`SELECT ${RELEASE_COLUMNS} FROM ${table} ORDER BY published_at DESC`).all<ReleaseRow>();
  return results.map(toRelease);
}

export async function getReleasePackage(db: D1Database, version: string): Promise<{ bytes: Uint8Array; sha256: string } | null> {
  const row = await db
    .prepare('SELECT package, sha256 FROM program_releases WHERE version = ?')
    .bind(version)
    .first<{ package: ArrayBuffer | number[]; sha256: string }>();
  if (!row) return null;
  // D1 returns BLOB columns as an array of bytes.
  return { bytes: new Uint8Array(row.package), sha256: row.sha256 };
}

export async function makeCurrent(db: D1Database, version: string, table: ReleaseTable = 'program_releases'): Promise<boolean> {
  const exists = await db.prepare(`SELECT version FROM ${table} WHERE version = ?`).bind(version).first();
  if (!exists) return false;
  await db.batch([
    db.prepare(`UPDATE ${table} SET is_current = 0 WHERE is_current = 1`),
    db.prepare(`UPDATE ${table} SET is_current = 1 WHERE version = ?`).bind(version),
  ]);
  return true;
}

// ---------- Publishing from GitHub Actions ----------

let githubKeys: JWTVerifyGetKey | null = null;

function githubJwks(env: Env): JWTVerifyGetKey {
  // GITHUB_OIDC_JWKS lets tests verify tokens without fetching GitHub's public keys.
  if (env.GITHUB_OIDC_JWKS) return createLocalJWKSet(JSON.parse(env.GITHUB_OIDC_JWKS) as JSONWebKeySet);
  githubKeys ??= createRemoteJWKSet(new URL(`${GITHUB_ISSUER}/.well-known/jwks`));
  return githubKeys;
}

// GitHub Actions OIDC token of a tag push in the release repository: no shared secret needed.
export const releaseAuth = createMiddleware<AppEnv>(async (c, next) => {
  const hostname = new URL(c.req.url).hostname;
  if (c.env.DEV_AUTH_EMAIL && (hostname === 'localhost' || hostname === '127.0.0.1')) {
    // Local development: publish without GitHub.
    c.set('releaseTag', null);
    await next();
    return;
  }
  const match = /^Bearer\s+(\S+)$/i.exec(c.req.header('Authorization') ?? '');
  if (!match) fail(401, 'unauthorized', 'A GitHub Actions OIDC token is required.');
  let claims: Record<string, unknown>;
  try {
    ({ payload: claims } = await jwtVerify(match[1], githubJwks(c.env), { issuer: GITHUB_ISSUER, audience: RELEASE_AUDIENCE }));
  } catch {
    fail(401, 'invalid_token', 'The GitHub OIDC token is invalid.');
  }
  if (String(claims.repository ?? '').toLowerCase() !== c.env.RELEASE_REPOSITORY.toLowerCase()) {
    fail(403, 'forbidden', 'Releases can be published only from the program repository.');
  }
  if (claims.ref_type !== 'tag' || typeof claims.ref !== 'string' || !claims.ref.startsWith('refs/tags/')) {
    fail(403, 'forbidden', 'Releases can be published only from a tag.');
  }
  c.set('releaseTag', claims.ref.slice('refs/tags/'.length));
  await next();
});

export const releaseApi = new Hono<AppEnv>();

releaseApi.use('*', releaseAuth);

releaseApi.post('/', async (c) => {
  const bytes = new Uint8Array(await c.req.arrayBuffer());
  return c.json(await storeRelease(c.env.DB, bytes, c.get('releaseTag') ?? null), 201);
});

// Validates and saves a release archive, making it current. With a tag (GitHub Actions)
// the tag must name the archive's version; admins publishing with a personal key pass null.
export async function storeRelease(db: D1Database, bytes: Uint8Array, tag: string | null) {
  const manifest = await validatePackage(bytes);
  if (tag !== null && tag !== `v${manifest.version}`) {
    fail(400, 'tag_mismatch', `The tag ${tag} does not match the version ${manifest.version} in manifest.json.`);
  }
  const existing = await db.prepare('SELECT version FROM program_releases WHERE version = ?').bind(manifest.version).first();
  if (existing) fail(409, 'release_exists', `Version ${manifest.version} is already published.`);

  const sha256 = await sha256Hex(bytes);
  const publishedAt = nowIso();
  await db.batch([
    db.prepare('UPDATE program_releases SET is_current = 0 WHERE is_current = 1'),
    db
      .prepare(
        `INSERT INTO program_releases (version, commit_sha, manifest, package, sha256, size, published_at, is_current)
         VALUES (?, ?, ?, ?, ?, ?, ?, 1)`,
      )
      .bind(manifest.version, manifest.commit, JSON.stringify(manifest), bytes, sha256, bytes.byteLength, publishedAt),
  ]);
  return { version: manifest.version, sha256, size: bytes.byteLength, publishedAt };
}

// ---------- Standalone SUSpector releases ----------

export async function getCurrentSuspectorRelease(db: D1Database) {
  const row = await db.prepare(`SELECT ${RELEASE_COLUMNS} FROM suspector_releases WHERE is_current = 1`).first<ReleaseRow>();
  return row ? toRelease(row) : null;
}

export async function getSuspectorPackage(db: D1Database, version: string): Promise<{ bytes: Uint8Array; sha256: string } | null> {
  const row = await db
    .prepare('SELECT package, sha256 FROM suspector_releases WHERE version = ?')
    .bind(version)
    .first<{ package: ArrayBuffer | number[]; sha256: string }>();
  return row ? { bytes: new Uint8Array(row.package), sha256: row.sha256 } : null;
}

export async function storeSuspectorRelease(db: D1Database, bytes: Uint8Array, tag: string | null) {
  const manifest = await validatePackage(bytes, isSuspectorFile);
  if (tag !== null && tag !== `v${manifest.version}`) {
    fail(400, 'tag_mismatch', `The tag ${tag} does not match the version ${manifest.version} in manifest.json.`);
  }
  const existing = await db.prepare('SELECT version FROM suspector_releases WHERE version = ?').bind(manifest.version).first();
  if (existing) fail(409, 'release_exists', `SUSpector version ${manifest.version} is already published.`);
  const sha256 = await sha256Hex(bytes);
  const publishedAt = nowIso();
  await db.batch([
    db.prepare('UPDATE suspector_releases SET is_current = 0 WHERE is_current = 1'),
    db
      .prepare(
        `INSERT INTO suspector_releases (version, commit_sha, manifest, package, sha256, size, published_at, is_current)
         VALUES (?, ?, ?, ?, ?, ?, ?, 1)`,
      )
      .bind(manifest.version, manifest.commit, JSON.stringify(manifest), bytes, sha256, bytes.byteLength, publishedAt),
  ]);
  return { version: manifest.version, sha256, size: bytes.byteLength, publishedAt };
}

// Public download API: SUSpector contains no database access and ordinary users have no Comp Info key.
export const suspectorApi = new Hono<AppEnv>();

suspectorApi.get('/latest', async (c) => {
  const release = await getCurrentSuspectorRelease(c.env.DB);
  if (!release) fail(404, 'no_release', 'SUSpector is not published yet.');
  return c.json(release);
});

// The program itself for the public download page: SUSpector.exe of the current release, unpacked.
suspectorApi.get('/download', async (c) => {
  const release = await getCurrentSuspectorRelease(c.env.DB);
  if (!release) fail(404, 'no_release', 'SUSpector is not published yet.');
  const found = await getSuspectorPackage(c.env.DB, release.version);
  const exe = found && (await readZip(found.bytes)).find((entry) => isSuspectorFile(entry.name));
  if (!exe) fail(500, 'invalid_package', 'The SUSpector archive has no program.');
  return new Response(exe.data, {
    headers: {
      'Content-Type': 'application/vnd.microsoft.portable-executable',
      'Content-Length': String(exe.data.byteLength),
      'Content-Disposition': 'attachment; filename="SUSpector.exe"',
      'Cache-Control': 'no-store',
      'X-SUSpector-Version': release.version,
    },
  });
});

suspectorApi.get('/:version/package', async (c) => {
  const found = await getSuspectorPackage(c.env.DB, c.req.param('version'));
  if (!found) fail(404, 'not_found', 'No such SUSpector version.');
  return new Response(found.bytes, {
    headers: {
      'Content-Type': 'application/zip',
      'Content-Length': String(found.bytes.byteLength),
      'X-Content-SHA256': found.sha256,
    },
  });
});

export const suspectorReleaseApi = new Hono<AppEnv>();
suspectorReleaseApi.use('*', releaseAuth);
suspectorReleaseApi.post('/', async (c) => {
  const bytes = new Uint8Array(await c.req.arrayBuffer());
  return c.json(await storeSuspectorRelease(c.env.DB, bytes, c.get('releaseTag') ?? null), 201);
});
