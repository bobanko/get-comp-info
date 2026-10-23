import { env } from 'cloudflare:test';
import { exports } from 'cloudflare:workers';
import { importJWK, SignJWT, type JWK } from 'jose';
import { describe, expect, it } from 'vitest';
import { sha256Hex } from '../src/crypto';
import { addUser, consoleRequest, harvesterKey, webRequest } from './helpers';
import { buildRelease, buildZip } from './zip-builder';

let counter = Math.floor(Math.random() * 5000);
function nextVersion(): string {
  counter++;
  return `2026.10.24.${counter}`;
}

async function githubToken(claims: Record<string, unknown> = {}, audience = 'comp-info-release'): Promise<string> {
  const jwk = JSON.parse(env.TEST_GITHUB_PRIVATE_JWK) as JWK;
  return new SignJWT({ repository: 'bobanko/get-comp-info', ref_type: 'tag', ref: 'refs/tags/v0', ...claims })
    .setProtectedHeader({ alg: 'RS256', kid: 'github-test' })
    .setIssuer('https://token.actions.githubusercontent.com')
    .setAudience(audience)
    .setIssuedAt()
    .setExpirationTime('10m')
    .sign(await importJWK(jwk, 'RS256'));
}

async function publish(bytes: Uint8Array, token?: string | null, url = 'https://comp-info.test/api/release') {
  const headers: Record<string, string> = { 'Content-Type': 'application/zip' };
  if (token) headers.Authorization = `Bearer ${token}`;
  const response = await exports.default.fetch(new Request(url, { method: 'POST', headers, body: bytes }));
  return { status: response.status, body: (await response.json()) as any };
}

async function publishVersion(version: string, files: Record<string, string> = { 'get-comp-info.ps1': 'Write-Host 1', 'menu.BAT': '@echo off' }) {
  return publish(await buildRelease(version, files), await githubToken({ ref: `refs/tags/v${version}` }));
}

describe('publishing releases', () => {
  it('accepts a GitHub Actions tag build of the program repository', async () => {
    const version = nextVersion();
    const bytes = await buildRelease(version, { 'get-comp-info.ps1': 'Write-Host "привет"', 'cloud-config.json': '{}' }, { deflate: true });
    const result = await publish(bytes, await githubToken({ ref: `refs/tags/v${version}` }));
    expect(result.status).toBe(201);
    expect(result.body).toMatchObject({ version, size: bytes.byteLength, sha256: await sha256Hex(bytes) });
  });

  it('rejects missing, forged and foreign tokens', async () => {
    const version = nextVersion();
    const bytes = await buildRelease(version, { 'a.ps1': 'x' });
    const ref = `refs/tags/v${version}`;
    expect((await publish(bytes, null)).status).toBe(401);
    expect((await publish(bytes, 'garbage')).status).toBe(401);
    expect((await publish(bytes, await githubToken({ ref }, 'other-audience'))).status).toBe(401);
    expect((await publish(bytes, await githubToken({ ref, repository: 'someone/fork' }))).status).toBe(403);
    expect((await publish(bytes, await githubToken({ ref: 'refs/heads/main', ref_type: 'branch' }))).status).toBe(403);
    expect((await publish(bytes, await githubToken({ ref: 'refs/tags/v1999.01.01.1' }))).body.error.code).toBe('tag_mismatch');
  });

  it('refuses to publish the same version twice', async () => {
    const version = nextVersion();
    expect((await publishVersion(version)).status).toBe(201);
    expect((await publishVersion(version)).status).toBe(409);
  });

  it('checks the archive against its manifest', async () => {
    const version = nextVersion();
    const token = await githubToken({ ref: `refs/tags/v${version}` });
    const expectError = async (bytes: Uint8Array, code: string, status = 400) => {
      const result = await publish(bytes, token);
      expect(result.status).toBe(status);
      expect(result.body.error.code).toBe(code);
    };

    await expectError(new TextEncoder().encode('not a zip'), 'invalid_package');
    await expectError(await buildZip([{ name: 'a.ps1', data: 'x' }]), 'invalid_package');
    await expectError(await buildRelease('1.0', { 'a.ps1': 'x' }), 'invalid_version');
    await expectError(await buildRelease(version, { 'a.ps1': 'x' }, { extra: [{ name: 'evil.ps1', data: 'x' }] }), 'invalid_package');
    await expectError(await buildRelease(version, { 'sub/a.ps1': 'x' }), 'invalid_manifest');
    await expectError(await buildRelease(version, { '..\\a.ps1': 'x' }), 'invalid_manifest');
    await expectError(await buildRelease(version, { 'notes.txt': 'x' }), 'invalid_manifest');
    const tampered = await buildRelease(version, { 'a.ps1': 'x' }, {
      manifest: { files: [{ path: 'a.ps1', sha256: '0'.repeat(64), size: 1 }] },
    });
    await expectError(tampered, 'invalid_package');
    await expectError(new Uint8Array(1024 * 1024 + 1), 'too_large', 413);
  });

  it('allows publishing without GitHub only for local development', async () => {
    const version = nextVersion();
    const bytes = await buildRelease(version, { 'a.ps1': 'x' });
    expect((await publish(bytes, null, 'http://localhost:8787/api/release')).status).toBe(201);
  });
});

describe('downloading releases', () => {
  it('serves the current release to console keys', async () => {
    const { key } = await harvesterKey();
    const version = nextVersion();
    const files = { 'get-comp-info.ps1': 'Write-Host 2' };
    const bytes = await buildRelease(version, files);
    await publish(bytes, await githubToken({ ref: `refs/tags/v${version}` }));

    const latest = await consoleRequest(key, 'GET', '/program/latest');
    expect(latest.status).toBe(200);
    expect(latest.body).toMatchObject({ version, sha256: await sha256Hex(bytes), size: bytes.byteLength, commit: 'abc123', notes: ['feat: test release'], isCurrent: true });

    const response = await exports.default.fetch(
      new Request(`https://comp-info.test/api/client/program/${version}/package`, { headers: { Authorization: `Bearer ${key}` } }),
    );
    expect(response.status).toBe(200);
    expect(response.headers.get('Content-Type')).toBe('application/zip');
    const downloaded = new Uint8Array(await response.arrayBuffer());
    expect(await sha256Hex(downloaded)).toBe(await sha256Hex(bytes));
    expect(response.headers.get('X-Content-SHA256')).toBe(await sha256Hex(bytes));

    expect((await consoleRequest(key, 'GET', '/program/1999.01.01.1/package')).status).toBe(404);
    expect((await consoleRequest(null, 'GET', '/program/latest')).status).toBe(401);
  });

  it('lets admins list releases and roll back', async () => {
    const admin = await addUser('admin');
    const editor = await addUser('editor');
    const { key } = await harvesterKey();
    const older = nextVersion();
    const newer = nextVersion();
    await publishVersion(older);
    await publishVersion(newer);
    expect((await consoleRequest(key, 'GET', '/program/latest')).body.version).toBe(newer);

    expect((await webRequest(editor, 'GET', '/releases')).status).toBe(403);
    expect((await webRequest(editor, 'POST', `/releases/${older}/make-current`)).status).toBe(403);

    const list = await webRequest(admin, 'GET', '/releases');
    const current = list.body.releases.filter((release: { isCurrent: boolean }) => release.isCurrent);
    expect(current.map((release: { version: string }) => release.version)).toEqual([newer]);

    const rolledBack = await webRequest(admin, 'POST', `/releases/${older}/make-current`);
    expect(rolledBack.body.current.version).toBe(older);
    expect((await consoleRequest(key, 'GET', '/program/latest')).body.version).toBe(older);
    expect((await webRequest(admin, 'POST', '/releases/1999.01.01.1/make-current')).status).toBe(404);
  });
});
