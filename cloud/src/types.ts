// Roles are ordered: each role includes the rights of the previous ones.
export const ROLES = ['viewer', 'harvester', 'editor', 'admin'] as const;
export type Role = (typeof ROLES)[number];

export interface User {
  email: string;
  name: string;
  role: Role;
  active: boolean;
  createdAt: string;
}

export interface ApiKeyInfo {
  id: string;
  label: string;
}

export type AppEnv = {
  Bindings: Env;
  Variables: {
    user: User;
    apiKey?: ApiKeyInfo;
    // Tag of the GitHub Actions run publishing a release; null in local development.
    releaseTag?: string | null;
  };
};

export function isRole(value: unknown): value is Role {
  return typeof value === 'string' && (ROLES as readonly string[]).includes(value);
}

export function hasRole(user: User, minimum: Role): boolean {
  return ROLES.indexOf(user.role) >= ROLES.indexOf(minimum);
}

export interface UserRow {
  email: string;
  name: string;
  role: Role;
  active: number;
  created_at: string;
}

export function toUser(row: UserRow): User {
  return {
    email: row.email,
    name: row.name,
    role: row.role,
    active: row.active === 1,
    createdAt: row.created_at,
  };
}
