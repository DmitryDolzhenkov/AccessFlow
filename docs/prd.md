# AccessFlow — PRD

Термины — по [GLOSSARY.md](../GLOSSARY.md). Архитектурные решения — в [docs/adr/](adr/). Исходная постановка — [problem.md](problem.md).

## 1. Проблема

Доступ к внутренним системам запрашивается через почту и мессенджеры: заявки теряются, непонятно, кто согласует, нет единого статуса, провизионинг выполняется вручную, истории решений нет.

AccessFlow даёт единый API: подать Access Request, получить решение System Owner, автоматически выполнить Provisioning и сохранить неизменяемый Audit Log.

Принцип: production-minded, but intentionally simple.

## 2. Участники

| Участник | Кто это | Что делает |
|---|---|---|
| Requester | Любой User | Подаёт Access Request для себя или для другого User; отменяет свои Pending-заявки |
| Beneficiary | Любой User | Получает доступ; видит заявки на себя |
| System Owner | Единственный текущий владелец System | Одобряет или отклоняет Access Request к своей System |
| AccessFlow | Сама система | Выполняет Provisioning; фиксируется в Audit Log как исполнитель событий Provisioning |

Users, Systems, System Owner каждой System и URL интеграции каждой System задаются seed-данными.

## 3. Жизненный цикл Access Request

```text
Pending ──approve──► Approved ──(provisioning ok)──────► Provisioned
   │                    └──────(provisioning failed)────► ProvisioningFailed
   ├──reject──► Rejected
   └──cancel──► Cancelled
```

Конечные статусы: Provisioned, ProvisioningFailed, Rejected, Cancelled. Из конечного статуса переходов нет.

## 4. Бизнес-правила

### Идентификация (ADR 0001)

- **BR-01.** Вызывающий User определяется заголовком `X-User-Id`. Подлинность заголовка не проверяется.
- **BR-02.** Если заголовка нет или он не ссылается на существующего User — `401 Unauthorized` на любом endpoint.

### Создание

- **BR-03.** Requester — всегда вызывающий User.
- **BR-04.** Beneficiary — любой существующий User, включая самого Requester.
- **BR-05.** System — любая существующая System. Ролей внутри System нет: запрашивается доступ к System целиком.
- **BR-06.** Justification обязательна (непустая).
- **BR-07.** Новая заявка создаётся в статусе Pending.
- **BR-08.** Для пары Beneficiary + System допускается не более одной Active Access Request (Pending или Approved). Нарушение — `409 Conflict`. Правило гарантируется ограничением в базе данных, а не только проверкой в коде, в том числе при одновременном создании.
- **BR-09.** После перехода заявки в конечный статус для той же пары можно подать новую.

### Решение

- **BR-10.** Одобрить или отклонить заявку может только текущий System Owner её System. Он определяется в момент действия, а не фиксируется при создании заявки.
- **BR-11.** System Owner может принять решение и по заявке, где он сам Requester или Beneficiary.
- **BR-12.** Решение возможно только в статусе Pending.
- **BR-13.** При отклонении Rejection Reason обязательна (непустая). При одобрении комментарий необязателен.

### Отмена

- **BR-14.** Отменить заявку может только её Requester и только в статусе Pending.

### Конкурентность

- **BR-15.** Если два действия над одной заявкой происходят одновременно (например, отмена и одобрение), выполняется первое; второе получает `409 Conflict` и ничего не меняет.

### Provisioning (ADR 0002)

- **BR-16.** Переход в Approved и запись о необходимости Provisioning сохраняются в одной транзакции. HTTP-вызов в System выполняется асинхронно фоновым обработчиком в том же процессе.
- **BR-17.** Контракт интеграции одинаков для всех System: `POST {url System}` с телом `{ "accessRequestId", "beneficiaryId", "systemId" }`. `accessRequestId` — ключ идемпотентности; System обязана корректно обрабатывать повторный вызов.
- **BR-18.** Ответ 2xx — заявка переходит в Provisioned.
- **BR-19.** Ответ 4xx — постоянная ошибка: заявка сразу переходит в ProvisioningFailed без повторов.
- **BR-20.** Ответ 5xx или таймаут — повтор с экспоненциальной задержкой. Число попыток, задержка и таймаут задаются в конфигурации. Когда попытки исчерпаны — ProvisioningFailed.
- **BR-21.** Ручного повтора Provisioning нет. Повторить можно только новой заявкой (BR-09).
- **BR-22.** Каждая попытка Provisioning пишется в технические логи.

### Audit Log

- **BR-23.** Каждое изменение заявки создаёт запись в Audit Log. События: Created, Approved, Rejected, Cancelled, Provisioned, ProvisioningFailed.
- **BR-24.** Запись содержит: заявку, событие, исполнителя, время, статус до и после, а также Justification, комментарий, Rejection Reason или причину ошибки Provisioning — что применимо.
- **BR-25.** Исполнитель событий Provisioned и ProvisioningFailed — AccessFlow, а не User.
- **BR-26.** Записи Audit Log никогда не изменяются и не удаляются.
- **BR-27.** Отклонённые попытки действий (нет прав, неверный статус, конфликт) в Audit Log не пишутся — только в технические логи.
- **BR-28.** Запись в Audit Log сохраняется в той же транзакции, что и изменение заявки.

### Видимость

- **BR-29.** Заявку и её Audit Log видят только её Requester, Beneficiary и текущий System Owner её System. Остальным — `404 Not Found`, чтобы не раскрывать существование заявки.
- **BR-30.** Действие, на которое у видящего заявку User нет права (например, Beneficiary пытается одобрить), — `403 Forbidden`. Действие в недопустимом статусе — `409 Conflict`.

## 5. API

| Возможность | Endpoint | Кто может |
|---|---|---|
| Подать заявку | `POST /access-requests` | Любой User |
| Посмотреть заявку и её статус | `GET /access-requests/{id}` | BR-29 |
| Посмотреть Audit Log заявки | `GET /access-requests/{id}/audit-log` | BR-29 |
| Мои заявки | `GET /access-requests/mine` | Заявки, где вызывающий — Requester или Beneficiary |
| Ждут моего решения | `GET /access-requests/pending-my-decision` | Pending-заявки к System, где вызывающий — System Owner |
| Одобрить | `POST /access-requests/{id}/approve` | System Owner (BR-10) |
| Отклонить | `POST /access-requests/{id}/reject` | System Owner (BR-10) |
| Отменить | `POST /access-requests/{id}/cancel` | Requester (BR-14) |

Ошибки валидации входных данных (пустая Justification, неизвестные Beneficiary или System, пустая Rejection Reason) — `400 Bad Request`.

Формат тел запросов и ответов, пагинация и фильтры списков определяются в issues.

## 6. Технические ограничения

Модульный монолит, .NET, PostgreSQL, EF Core. Без брокера сообщений, MediatR, AutoMapper. Бизнес-правила — вне HTTP-контроллеров. Подробнее — [AGENTS.md](../AGENTS.md) и [docs/adr/](adr/).

Для разработки и тестов System заменяется заглушкой, реализующей контракт BR-17.

## 7. Вне scope

- revoke выданного доступа и срок действия доступа;
- роли внутри System;
- многошаговое или параллельное согласование, правила маршрутизации кроме «System Owner этой System»;
- замещение отсутствующего System Owner;
- административный API для Users, Systems и System Owner;
- деактивация User или System;
- ручной повтор Provisioning;
- полноценный authentication provider, LDAP / Active Directory, настоящий IAM, полноценная RBAC-модель;
- frontend, notifications;
- Kafka / RabbitMQ, Kubernetes, микросервисы.

## 8. Допущения

- **A-01.** Seed-данные только добавляют и изменяют Users и Systems, но не удаляют записи, на которые ссылаются заявки.
- **A-02.** API работает только во внутреннем доверенном контуре (следствие BR-01, см. ADR 0001).
