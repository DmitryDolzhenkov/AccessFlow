# Progress

| Issue | Название | Статус | Примечания |
|---|---|---|---|
| #2 | Каркас решения и создание Access Request | Done | Модули Directory и AccessRequests — отдельные проекты; общий AccessFlowDbContext в хосте, схемы на модуль, одна история миграций. |
| #3 | Одна Active Access Request на пару Beneficiary + System | Done | Частичный уникальный индекс `(BeneficiaryId, SystemId) WHERE Status IN ('Pending', 'Approved')`; нарушение индекса при вставке → `409`. Тесты очищают `access_requests` перед каждым тестом. |
| #4 | Audit Log: событие Created и чтение | Done | Таблица `access_requests.audit_log`; заявка и запись Created добавляются в один `SaveChangesAsync` (одна транзакция). `ActorId` необязателен: null — исполнитель AccessFlow (BR-25). Порядок: `OccurredAt`, затем `Sequence` (bigint identity, порядок вставки), т.к. UUIDv7 в .NET не монотонен внутри миллисекунды. Тесты только через API: порядок нескольких записей, BR-28 и BR-27 для `400`/`401` подтверждены ревью кода. |
| #5 | Одобрение и отклонение Access Request System Owner | Done | `POST /access-requests/{id}/approve` (тело и комментарий необязательны) и `/reject` (`rejectionReason` обязательна), успех — `204`. Порядок проверок: не видит → `404`, не текущий System Owner → `403`, пустая Rejection Reason → `400`, не Pending → `409`. BR-15: `Status` — concurrency token (`UPDATE … WHERE "Status" = 'Pending'`), проигравшее действие откатывается целиком → `409`. В `audit_log` добавлены `Comment` и `RejectionReason`; пустой комментарий хранится как null. |
| #6 | Отмена Access Request Requester | Todo | |
| #7 | Списки: мои заявки и ждут моего решения | Todo | |
| #8 | Provisioning: outbox, фоновый обработчик, успешный путь | Todo | |
| #9 | Provisioning: ошибки, повторы и ProvisioningFailed | Todo | |
