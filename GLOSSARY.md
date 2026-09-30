# AccessFlow

Внутренняя система, через которую сотрудники запрашивают доступ к корпоративным системам, а владельцы систем его согласуют или отклоняют.

## Language

### Участники

**User**:
Сотрудник, заранее заведённый в AccessFlow. Любой участник заявки — это User.
_Avoid_: Employee, account

**Requester**:
User, подавший Access Request — для себя или за другого.
_Avoid_: Initiator, author

**Beneficiary**:
User, которому будет выдан доступ по Access Request. Может совпадать с Requester.
_Avoid_: Recipient, target user, grantee

**System Owner**:
Единственный User, в данный момент отвечающий за System и принимающий решение по всем Access Request к ней.
_Avoid_: Approver, manager, admin

### Заявка

**System**:
Корпоративная система, доступ к которой можно запросить (например, Jira).
_Avoid_: Application, resource

**Access Request**:
Запрос на выдачу доступа одному Beneficiary к одной System.
_Avoid_: Ticket, application, request (без уточнения)

**Justification**:
Обязательное объяснение Requester, зачем Beneficiary нужен доступ к System.
_Avoid_: Reason, description, comment

**Active Access Request**:
Access Request в статусе Pending или Approved. Для одной пары Beneficiary + System может существовать не более одной.
_Avoid_: Open request, in-flight request

**Rejection Reason**:
Обязательное объяснение System Owner, почему Access Request отклонён.
_Avoid_: Comment, note

### Статусы Access Request

**Pending**:
Access Request ждёт решения System Owner.
_Avoid_: New, submitted, awaiting approval

**Approved**:
System Owner одобрил Access Request, Provisioning ещё не завершён.
_Avoid_: Accepted, in progress

**Provisioned**:
Доступ фактически выдан в System. Конечный статус.
_Avoid_: Completed, done, granted

**ProvisioningFailed**:
Provisioning окончательно не удался. Конечный статус; повтор возможен только новой Access Request.
_Avoid_: Error, failed

**Rejected**:
System Owner отказал в доступе. Конечный статус.
_Avoid_: Declined, denied

**Cancelled**:
Requester отозвал Access Request до решения System Owner. Конечный статус.
_Avoid_: Withdrawn, revoked

### Выполнение

**Provisioning**:
Фактическая выдача доступа в System после одобрения Access Request, выполняемая AccessFlow через интеграцию с этой System.
_Avoid_: Fulfillment, grant

**Audit Log**:
Неизменяемая хронология всех действий над Access Request: кто, что и когда сделал. Записи никогда не изменяются и не удаляются.
_Avoid_: History, event log, journal
