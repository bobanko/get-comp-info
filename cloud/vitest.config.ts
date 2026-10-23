import { cloudflareTest, readD1Migrations } from '@cloudflare/vitest-plugin';
import { exportJWK, generateKeyPair } from 'jose';
import { defineConfig } from 'vitest/config';

export default defineConfig(async () => {
  const migrations = await readD1Migrations('./migrations');

  // Tests sign Cloudflare Access tokens with this key pair; the Worker verifies them with the public half.
  const { publicKey, privateKey } = await generateKeyPair('RS256', { extractable: true });
  const publicJwk = { ...(await exportJWK(publicKey)), kid: 'test', alg: 'RS256', use: 'sig' };
  const privateJwk = { ...(await exportJWK(privateKey)), kid: 'test', alg: 'RS256' };

  // Stand-in for GitHub Actions OIDC tokens used to publish program releases.
  const github = await generateKeyPair('RS256', { extractable: true });
  const githubPublicJwk = { ...(await exportJWK(github.publicKey)), kid: 'github-test', alg: 'RS256', use: 'sig' };
  const githubPrivateJwk = { ...(await exportJWK(github.privateKey)), kid: 'github-test', alg: 'RS256' };

  return {
    plugins: [
      cloudflareTest({
        wrangler: { configPath: './wrangler.jsonc' },
        miniflare: {
          bindings: {
            TEST_MIGRATIONS: migrations,
            TEST_ACCESS_PRIVATE_JWK: JSON.stringify(privateJwk),
            ACCESS_TEAM_DOMAIN: 'test-team.cloudflareaccess.com',
            ACCESS_AUD: 'test-aud',
            ACCESS_JWKS: JSON.stringify({ keys: [publicJwk] }),
            DEV_AUTH_EMAIL: 'dev@example.com',
            GITHUB_OIDC_JWKS: JSON.stringify({ keys: [githubPublicJwk] }),
            TEST_GITHUB_PRIVATE_JWK: JSON.stringify(githubPrivateJwk),
          },
        },
      }),
    ],
    test: {
      setupFiles: ['./test/setup.ts'],
    },
  };
});
