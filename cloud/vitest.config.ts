import { cloudflareTest, readD1Migrations } from '@cloudflare/vitest-plugin';
import { exportJWK, generateKeyPair } from 'jose';
import { defineConfig } from 'vitest/config';

export default defineConfig(async () => {
  const migrations = await readD1Migrations('./migrations');

  // Tests sign Cloudflare Access tokens with this key pair; the Worker verifies them with the public half.
  const { publicKey, privateKey } = await generateKeyPair('RS256', { extractable: true });
  const publicJwk = { ...(await exportJWK(publicKey)), kid: 'test', alg: 'RS256', use: 'sig' };
  const privateJwk = { ...(await exportJWK(privateKey)), kid: 'test', alg: 'RS256' };

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
          },
        },
      }),
    ],
    test: {
      setupFiles: ['./test/setup.ts'],
    },
  };
});
