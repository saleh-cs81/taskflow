# TaskFlow — Task Management & Team Collaboration Platform

A Paymo-style, multi-tenant SaaS for task management, team collaboration, time tracking, and reporting.

Full design blueprint: [TaskFlow-Architecture.md](TaskFlow-Architecture.md).

## Stack

- **Backend:** ASP.NET Core 9 Web API, Clean Architecture (Domain / Application / Infrastructure / API)
- **Database:** SQL Server (EF Core 9, code-first migrations)
- **Auth:** JWT access tokens + rotating refresh tokens, permission-based RBAC
- **Multi-tenancy:** shared DB, `TenantId` global query filters
- **Bilingual:** English + Arabic with full RTL/LTR (request localization, localized API messages)

## Solution layout

```
src/
  TaskFlow.Domain          Entities, enums, base types (no dependencies)
  TaskFlow.Application     Interfaces, DTOs, validators, permissions catalogue
  TaskFlow.Infrastructure  EF Core, DbContext, interceptors, JWT, auth service, seeders
  TaskFlow.API             Controllers, middleware, RBAC policies, localization, Program.cs
```

## Running locally

### Option A — Docker (recommended)

```bash
docker compose up --build
```

API: http://localhost:8080  ·  Swagger: http://localhost:8080/swagger
SQL Server on `localhost:1433`, Redis on `localhost:6379`. Migrations apply automatically on startup.

### Option B — local dotnet

1. Have SQL Server reachable on `localhost:1433` (or edit `ConnectionStrings:Default` in `src/TaskFlow.API/appsettings.json`).
2. Run:

```bash
dotnet run --project src/TaskFlow.API
```

Migrations + the global permission catalogue are seeded on startup.

## EF Core migrations

```bash
# add a migration
dotnet ef migrations add <Name> --project src/TaskFlow.Infrastructure --startup-project src/TaskFlow.API --output-dir Persistence/Migrations

# apply manually
dotnet ef database update --project src/TaskFlow.Infrastructure --startup-project src/TaskFlow.API
```

## Auth flow (Phase 1)

| Endpoint | Purpose |
|----------|---------|
| `POST /api/v1/auth/register` | Creates a company (tenant), seeds system roles, registers the first user as **CompanyAdmin**, returns tokens |
| `POST /api/v1/auth/login` | Email + password → access + refresh tokens |
| `POST /api/v1/auth/refresh` | Rotates the refresh token, returns a new pair |
| `POST /api/v1/auth/logout` | Revokes a refresh token |
| `POST /api/v1/auth/forgot-password` | Issues a reset token (email sending: Phase 4) |
| `POST /api/v1/auth/reset-password` | Resets the password with a valid token |
| `GET  /api/v1/me` | Current user, tenant, and permissions (requires bearer token) |

### Language

Send `Accept-Language: ar` (or `?culture=ar`) to receive Arabic API/error messages.

## Security notes

- Set a strong `Jwt:SigningKey` (32+ chars) and a real SQL password via environment variables before any non-local use.
- Passwords hashed with PBKDF2-SHA256 (100k iterations); refresh tokens stored hashed (SHA-256).
