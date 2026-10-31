# WinOpti community — сервер для «Твой ПК против похожих»

Cloudflare Worker + база D1. Бесплатного тарифа Cloudflare хватает с запасом (100 000 запросов в день).

Хранится только: игра, модель процессора и видеокарты, объём памяти, разрешение экрана, средний FPS и 1% low,
случайный id установки (чтобы не сравнивать человека с самим собой). IP и что-либо личное не сохраняется.
Сравнение показывается, только когда похожих замеров хотя бы 5.

## Выложить (один раз)

```
cd cloud
npx wrangler login                      # вход в свой аккаунт Cloudflare в браузере
npx wrangler d1 create winopti          # выдаст database_id — вставь его в wrangler.toml
npx wrangler d1 execute winopti --remote --file=schema.sql
npx wrangler deploy                     # выдаст адрес вида https://winopti-community.<имя>.workers.dev
```

Адрес вставить в `Fps/Community.cs` → `DefaultEndpoint` и пересобрать WinOpti. Пока адрес пустой, функция в программе скрыта.

## Проверить локально, без Cloudflare

```
node cloud/test/local_server.mjs 8787     # Node 24+: база в памяти + 6 тестовых замеров
WinOpti.exe --page=fps --demo-fps --community=http://127.0.0.1:8787   # только Debug-сборка
```
