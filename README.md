# AccessFlow
Небольшая внутренняя система обработки заявок на доступ к корпоративным системам.

## Локальный запуск

1. Скопировать `.env.example` в `.env` и задать `POSTGRES_PASSWORD`.
2. `docker compose up -d` — PostgreSQL на `127.0.0.1:5432`, пользователь `devuser`.
3. Задать строку подключения в user-secrets (пароль в двойных кавычках, если в нём есть `;`):

   ```sh
   dotnet user-secrets set --project src/AccessFlow.Api "ConnectionStrings:AccessFlow" 'Host=localhost;Port=5432;Database=accessflow;Username=devuser;Password="<пароль>"'
   ```

4. `dotnet run --project src/AccessFlow.Api` — база `accessflow` создаётся, миграции применяются при старте.
5. `dotnet run --project src/AccessFlow.SystemStub` — заглушка System на `http://localhost:5199`: принимает вызовы Provisioning по URL из seed-данных и всегда выдаёт доступ. Без неё одобренные заявки остаются в Approved.

Тесты (`dotnet test`) поднимают собственный PostgreSQL через Testcontainers; нужен только запущенный Docker.
