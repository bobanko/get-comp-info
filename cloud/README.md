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

Откройте http://localhost:8787. Запросы на `localhost` выполняются от имени `DEV_AUTH_EMAIL` из `.dev.vars`, без Cloudflare Access; на настоящем домене эта переменная не действует. Скрипт `dev` передаёт Worker'у адрес `localhost`, иначе wrangler подставил бы боевой домен из `routes`.

Если при автоматической перезагрузке wrangler падает с `SQLITE_BUSY`, остановите его и запустите заново; проверьте, что не остались процессы `workerd` от прошлых запусков.

Веб-интерфейс лежит в `public/`: обычные ES-модули без сборки (`js/app.js` — маршруты, `js/pages/*` — страницы, `js/logic.js` — логика без DOM, она покрыта тестами `test/web-logic.test.ts`).

## Развёртывание

1. Войти в Cloudflare: `npx wrangler login`.
2. Создать базу: `npx wrangler d1 create comp-info`. Полученный `database_id` вписать в `wrangler.jsonc`.
3. Применить схему: `npm run db:migrate:remote`.
4. Опубликовать Worker: `npm run deploy`. Он работает только на своём домене из `routes` (сейчас `comp-info.chagear.top`, домен должен быть подключён к аккаунту Cloudflare). Адреса workers.dev выключены, чтобы Access нельзя было обойти.
5. Настроить Cloudflare Access. Сейчас настроено так (команда `chagear.cloudflareaccess.com`):
   - метод входа **One-time PIN** (код на почту);
   - переиспользуемая политика **Comp Info team**: Allow, Include → Emails — список почт команды. **Нового пользователя добавляют сюда** и в таблицу `users`;
   - приложение **Comp Info** (self-hosted) для `comp-info.chagear.top` с этой политикой. У адреса есть исключения (overrides) `/api/client/*` и `/api/release` с поведением `public`: консоль и GitHub Actions ходят туда без входа через браузер, а защищают эти пути личные ключи и OIDC-токен GitHub Actions (секретов для публикации версий не нужно);
   - **Application Audience (AUD) Tag** приложения и домен команды вписаны в `wrangler.jsonc`.
6. После изменения `ACCESS_TEAM_DOMAIN` или `ACCESS_AUD` в `wrangler.jsonc` снова выполнить `npm run deploy`.
7. Создать первого администратора:

   ```powershell
   npx wrangler d1 execute comp-info --remote --command "INSERT INTO users (email, name, role, active, created_at) VALUES ('you@company.com', 'Имя', 'admin', 1, '2026-10-23T00:00:00Z')"
   ```

   Остальных пользователей добавляет администратор на странице «Пользователи» веб-интерфейса.

8. (Рекомендуется) Включить автоматическое обновление политики Access, чтобы новый или снова включённый пользователь мог войти сразу:
   - в панели Cloudflare: My Profile → API Tokens → Create Token → Custom token, разрешение **Account → Access: Apps and Policies → Edit** для этого аккаунта;
   - сохранить токен как секрет Worker'а: `npx wrangler secret put CF_API_TOKEN` (команда спросит значение, в чат и в файлы его не вставлять).

   Аккаунт и политика уже указаны в `wrangler.jsonc` (`CF_ACCOUNT_ID`, `ACCESS_POLICY_ID`). Без токена страница «Пользователи» напоминает добавить почту в политику «Comp Info team» вручную. Правила политики не по почте (например, по домену) обновление не трогает.

Пользователи, которых нет в таблице `users` или которые отключены, получают 403 даже после успешного входа через Access.
