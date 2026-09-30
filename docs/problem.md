# 1. Business problem

В компании сотрудники запрашивают доступ к внутренним системам через почту или мессенджеры.

Проблемы текущего процесса:

- заявка может потеряться;
- непонятно, кто должен её согласовывать;
- отсутствует единый статус;
- сложно восстановить историю принятия решения;
- после согласования provisioning выполняется вручную;
- нет надёжного audit trail.

## AS-IS

```text
Employee
   ↓
Email / messenger
   ↓
Manager / IT / system owner?
   ↓
manual clarification
   ↓
approval
   ↓
manual provisioning
```

## TO-BE

```text
Employee
   ↓
AccessFlow API
   ↓
validation
   ↓
routing rule
   ↓
Approver
   ↓
approve / reject
   ↓
audit log
   ↓
provisioning integration
```

---
## Initial scope (draft)
# 2. Scope  

Система должна позволять:

1. создать заявку на доступ;
2. автоматически определить согласующего;
3. согласовать или отклонить заявку;
4. сохранить историю действий;
5. после согласования инициировать provisioning через HTTP-интеграцию;
6. показать состояние заявки через API.

### Out of scope
Не реализовывать:

- полноценный authentication provider;
- LDAP / Active Directory;
- настоящий IAM;
- сложный frontend;
- Kafka/RabbitMQ;
- Kubernetes;
- микросервисы;
- notifications;
- полноценную RBAC-модель.

Это сознательное ограничение scope.

Главная архитектурная идея:

> production-minded, but intentionally simple.
...