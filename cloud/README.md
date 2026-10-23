# Comp Info Cloud

Облачная часть: Cloudflare Worker с API для консоли и веба, база D1, вход в веб через Cloudflare Access. Требования описаны в [../CLOUD.md](../CLOUD.md).

```text
src/
  index.ts          маршрутизация, ошибки, раздача ./public
  auth.ts           личные ключи (консоль) и токены Cloudflare Access (веб)
  devices.ts        записи устройств: ревизии, история, конфликты, мягкое удаление
  routes/console.ts /api/client — синхронизация из консоли
  routes/web.ts     /api/web — устройства, ключи, пользователи
migrations/         схема D1
public/             веб-интерфейс (пока заглушка)
test/               тесты в среде Workers (Miniflare)
```

## Разработка

Нужен Node.js 22+.

```powershell
npm install
npm test                  # тесты
npm run typecheck         # проверка типов
npm run types             # пересоздать worker-configuration.d.ts после правки wrangler.jsonc
```

Локальный запуск:

```powershell
Copy-Item .dev.vars.example .dev.vars
npm run db:migrate:local
npx wrangler d1 execute comp-info --local --command "INSERT INTO users (email, name, role, active, created_at) VALUES ('admin@example.com', 'Dev admin', 'admin', 1, '2026-01-01T00:00:00Z')"
npm run dev
```

Пока `ACCESS_AUD` пуст, каждый веб-запрос выполняется от имени `DEV_AUTH_EMAIL` из `.dev.vars`. В рабочем облаке `ACCESS_AUD` обязателен, а `DEV_AUTH_EMAIL` задавать нельзя.

## Развёртывание

1. Войти в Cloudflare: `npx wrangler login`.
2. Создать базу: `npx wrangler d1 create comp-info`. Полученный `database_id` вписать в `wrangler.jsonc`.
3. Применить схему: `npm run db:migrate:remote`.
4. Опубликовать Worker: `npm run deploy`. Он работает только на своём домене из `routes` (сейчас `comp-info.chagear.top`, домен должен быть подключён к аккаунту Cloudflare). Адреса workers.dev выключены, чтобы Access нельзя было обойти.
5. Настроить Cloudflare Access (Zero Trust → Access → Applications → Self-hosted) — два приложения:
   - **Comp Info**: адрес `comp-info.chagear.top` без пути, политика **Allow** со списком почтовых адресов команды, метод входа «One-time PIN»;
   - **Comp Info console API**: адреса `comp-info.chagear.top/api/client/*` и `comp-info.chagear.top/api/release`, политика **Bypass** (Include → Everyone). Более точный путь главнее, поэтому эти адреса открыты для консоли, а их защищают личные ключи и токен публикации;
   - скопировать **Application Audience (AUD) Tag** приложения «Comp Info» и домен команды (`<team>.cloudflareaccess.com`).
6. Вписать в `wrangler.jsonc` значения `ACCESS_TEAM_DOMAIN` и `ACCESS_AUD`, затем снова `npm run deploy`.
7. Создать первого администратора:

   ```powershell
   npx wrangler d1 execute comp-info --remote --command "INSERT INTO users (email, name, role, active, created_at) VALUES ('you@company.com', 'Имя', 'admin', 1, '2026-10-23T00:00:00Z')"
   ```

   Остальных пользователей добавляет администратор через веб-API (`POST /api/web/users`). Каждого нового пользователя нужно также добавить в политику Access.

Пользователи, которых нет в таблице `users` или которые отключены, получают 403 даже после успешного входа через Access.
