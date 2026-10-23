declare namespace Cloudflare {
  interface Env {
    TEST_MIGRATIONS: import('cloudflare:test').D1Migration[];
    TEST_ACCESS_PRIVATE_JWK: string;
    TEST_GITHUB_PRIVATE_JWK: string;
  }
}
