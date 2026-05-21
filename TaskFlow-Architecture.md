# TaskFlow — Task Management & Team Collaboration Platform
### Complete Architecture & Implementation Blueprint (Paymo-style SaaS)

> Working name: **TaskFlow**. Stack assumptions (change anytime): **.NET 9** Web API · **SQL Server 2019+** · **EF Core 9** · vanilla JS + Bootstrap 5 frontend · **Flutter** mobile · shared-DB multi-tenancy with `TenantId`.

---

## 0. Reading Guide

This document delivers the 11 requested artifacts:

| # | Section |
|---|---------|
| 1 | [System Architecture](#1-system-architecture) |
| 2 | [Database Design](#2-database-design) |
| 3 | [API Structure](#3-api-structure) |
| 4 | [Frontend Folder Structure](#4-frontend-folder-structure) |
| 5 | [Mobile App Architecture](#5-mobile-app-architecture) |
| 6 | [Development Roadmap](#6-development-roadmap) |
| 7 | [Suggested Timeline](#7-suggested-timeline) |
| 8 | [Best Practices](#8-best-practices) |
| 9 | [Migration Strategy from Paymo](#9-migration-strategy-from-paymo) |
| 10 | [Deployment Strategy](#10-deployment-strategy) |
| 11 | [Recommended Third-Party Services](#11-recommended-third-party-services) |

---

## 1. System Architecture

### 1.1 High-level topology

```
                          ┌─────────────────────────────┐
                          │      Clients                │
                          │  Web (HTML/JS/Bootstrap)    │
                          │  Flutter (Employee + Admin) │
                          └──────────────┬──────────────┘
                                         │ HTTPS / JWT
                          ┌──────────────▼──────────────┐
                          │   API Gateway / Reverse Proxy│
                          │   (YARP or Nginx)            │
                          │  TLS, rate-limit, routing    │
                          └──────────────┬──────────────┘
                                         │
        ┌────────────────────────────────┼────────────────────────────────┐
        │                                │                                 │
┌───────▼────────┐            ┌──────────▼──────────┐          ┌──────────▼──────────┐
│  TaskFlow API   │            │  SignalR Hub        │          │  Background Worker  │
│ (ASP.NET Core)  │            │  (realtime updates) │          │ (Hangfire/Quartz)   │
│ Clean Arch      │            │                     │          │ sync, emails, recur │
└───────┬─────────┘            └──────────┬──────────┘          └──────────┬──────────┘
        │                                 │                                │
        └─────────────┬───────────────────┴────────────────┬──────────────┘
                      │                                      │
              ┌───────▼────────┐                    ┌────────▼─────────┐
              │  SQL Server     │                    │  Redis           │
              │  (primary data) │                    │  cache + SignalR │
              └─────────────────┘                    │  backplane       │
                      │                              └──────────────────┘
              ┌───────▼────────────────┐
              │  Blob/Object Storage    │  Azure Blob / AWS S3 / MinIO
              │  (files & attachments)  │
              └─────────────────────────┘
```

### 1.2 Architectural style

**Modular Monolith with Clean Architecture** (not microservices yet).

> **Why modular monolith first:** A Paymo-style product has tightly coupled domains (tasks ↔ time ↔ projects ↔ billing). Starting with microservices would add distributed-transaction pain, deployment complexity, and cost with no early benefit. A modular monolith with clear bounded contexts lets you extract a service later (e.g. Reporting, Migration) only when load demands it.

**Layers (Clean Architecture):**

```
TaskFlow.sln
├── src/
│   ├── TaskFlow.Domain          ← Entities, value objects, enums, domain events. No dependencies.
│   ├── TaskFlow.Application      ← Use-cases (CQRS handlers), DTOs, interfaces, validators. Depends on Domain.
│   ├── TaskFlow.Infrastructure   ← EF Core, repositories, external services (email, storage, Paymo client). Depends on Application.
│   ├── TaskFlow.API              ← Controllers, middleware, DI wiring, SignalR hubs. Depends on Application + Infrastructure.
│   └── TaskFlow.Worker           ← Background jobs (Hangfire). Depends on Application + Infrastructure.
├── tests/
│   ├── TaskFlow.UnitTests
│   ├── TaskFlow.IntegrationTests
│   └── TaskFlow.ArchitectureTests   ← enforces layer dependency rules
└── deploy/
    ├── docker/
    └── k8s/  (optional)
```

**Module boundaries (folders inside Application/Domain):**
`Identity`, `Tenancy`, `Projects`, `Tasks`, `TimeTracking`, `Collaboration`, `Files`, `Notifications`, `Reporting`, `Billing`, `Integration.Paymo`.

### 1.3 Cross-cutting concerns

| Concern | Approach |
|---------|----------|
| CQRS / mediation | **MediatR** — commands & queries as discrete handlers |
| Validation | **FluentValidation** in a MediatR pipeline behavior |
| Mapping | **Mapster** (faster than AutoMapper) |
| Logging | **Serilog** → console + Seq/Elastic; structured logs with correlation IDs |
| Caching | **Redis** (IDistributedCache) for reference data, permissions, dashboards |
| Realtime | **SignalR** with Redis backplane for scale-out |
| Background jobs | **Hangfire** (SQL-backed, has dashboard) for recurring tasks, reminders, Paymo sync |
| Multi-tenancy | `ITenantContext` resolved from JWT claim; EF Core **global query filter** on `TenantId` |
| API docs | **Swagger / OpenAPI** via Swashbuckle, grouped by version |
| Rate limiting | Built-in **ASP.NET Core rate limiter** (token-bucket per tenant/IP) |
| Health checks | `/health/live`, `/health/ready` (DB, Redis, storage probes) |

### 1.4 Multi-tenancy model

- **Shared database, shared schema, `TenantId` discriminator** on every tenant-owned table.
- `TenantId` (the Company) is stamped on every entity via `ISaveChangesInterceptor`.
- EF Core global query filter automatically scopes all queries: `modelBuilder.Entity<T>().HasQueryFilter(e => e.TenantId == _tenant.Id)`.
- `Super Admin` operations bypass the filter via an explicit `IgnoreQueryFilters()` flag.
- **Upgrade path:** if a large enterprise client needs isolation, the same code supports "DB-per-tenant" by swapping the connection-string resolver — no domain changes.

---

## 2. Database Design

### 2.1 Conventions

- Surrogate PK: `Id BIGINT IDENTITY` (or `uniqueidentifier` if you prefer GUIDs for offline-sync friendliness — recommended for mobile: see §5).
- Every tenant table has: `TenantId`, `CreatedAtUtc`, `CreatedById`, `UpdatedAtUtc`, `UpdatedById`, `IsDeleted` (soft delete), `RowVersion` (`rowversion` for optimistic concurrency).
- Soft delete via global query filter (`IsDeleted = 0`).
- Indexing: composite index `(TenantId, <frequently-filtered-col>)` on hot tables.

### 2.2 Core schema (grouped by module)

**Identity & Tenancy**
```
Tenants(Id, Name, Slug, Plan, SubscriptionStatus, TrialEndsUtc, ...)
Users(Id, TenantId, Email, NormalizedEmail, PasswordHash, FullName, AvatarUrl,
      Locale, TimeZone, IsActive, ...)
Roles(Id, TenantId, Name)                      -- SuperAdmin is global (TenantId NULL)
UserRoles(UserId, RoleId)
Permissions(Id, Code)                          -- e.g. "projects.create"
RolePermissions(RoleId, PermissionId)
RefreshTokens(Id, UserId, TokenHash, ExpiresUtc, RevokedUtc, ReplacedByTokenHash, DeviceInfo)
Invitations(Id, TenantId, Email, RoleId, Token, ExpiresUtc, AcceptedUtc)
ActivityLogs(Id, TenantId, UserId, Action, EntityType, EntityId, MetadataJson, CreatedAtUtc)
AuditLogs(Id, TenantId, UserId, TableName, RecordId, ChangeType, OldValuesJson, NewValuesJson, CreatedAtUtc)
```

**Workspace & Org**
```
Workspaces(Id, TenantId, Name, Settings_Json)
Departments(Id, TenantId, Name, ManagerUserId)
Teams(Id, TenantId, Name, DepartmentId)
TeamMembers(TeamId, UserId, RoleInTeam)
Clients(Id, TenantId, Name, ContactEmail, CompanyName)
```

**Projects**
```
ProjectCategories(Id, TenantId, Name, Color)
Projects(Id, TenantId, WorkspaceId, Name, Code, CategoryId, ClientId, Status,
         StartDate, DueDate, BudgetType, BudgetAmount, BudgetHours, Color, IsBillable, ...)
ProjectMembers(ProjectId, UserId, Role)
Milestones(Id, TenantId, ProjectId, Name, DueDate, Status)
```

**Tasks**
```
TaskLists(Id, TenantId, ProjectId, Name, Position)        -- Kanban columns / groups
Tasks(Id, TenantId, ProjectId, TaskListId, ParentTaskId,  -- ParentTaskId => subtask
      Title, Description, Status, Priority, AssigneeId, ReporterId,
      StartDate, DueDate, EstimateHours, Position, CompletedAtUtc, IsBillable, ...)
TaskDependencies(Id, PredecessorTaskId, SuccessorTaskId, Type)   -- FS/SS/FF/SF
TaskWatchers(TaskId, UserId)
Tags(Id, TenantId, Name, Color)
TaskTags(TaskId, TagId)
ChecklistItems(Id, TenantId, TaskId, Text, IsDone, Position)
RecurringTaskRules(Id, TenantId, TaskTemplateJson, CronExpression, NextRunUtc)
Comments(Id, TenantId, EntityType, EntityId, AuthorId, Body, ParentCommentId, CreatedAtUtc)
Mentions(Id, CommentId, MentionedUserId)
```

**Time tracking**
```
TimeEntries(Id, TenantId, UserId, TaskId, ProjectId, StartUtc, EndUtc,
            DurationSeconds, IsBillable, IsRunning, Note, Source, ...)
Timesheets(Id, TenantId, UserId, PeriodStart, PeriodEnd, Status)   -- Draft/Submitted/Approved
TimesheetEntries(TimesheetId, TimeEntryId)
```

**Files**
```
Files(Id, TenantId, EntityType, EntityId, FileName, ContentType, SizeBytes,
      StorageKey, Version, UploadedById, CreatedAtUtc)
```

**Notifications & Collaboration**
```
Notifications(Id, TenantId, UserId, Type, Title, Body, LinkUrl, IsRead, CreatedAtUtc)
NotificationPreferences(UserId, Channel, Type, IsEnabled)         -- email/push/in-app
Discussions(Id, TenantId, ProjectId, Title, CreatedById)
DiscussionPosts(Id, DiscussionId, AuthorId, Body, CreatedAtUtc)
```

**Billing & Subscription**
```
Plans(Id, Name, MaxUsers, MaxProjects, PriceMonthly, Features_Json)
Subscriptions(Id, TenantId, PlanId, Provider, ProviderSubId, Status, CurrentPeriodEndUtc)
Invoices(Id, TenantId, SubscriptionId, Amount, Currency, Status, IssuedUtc, PdfStorageKey)
```

**Integration (Paymo migration)**
```
PaymoConnections(Id, TenantId, ApiKeyEncrypted, LastSyncUtc, Status)
MigrationJobs(Id, TenantId, Type, Status, TotalRecords, ProcessedRecords, StartedUtc, FinishedUtc)
EntityMappings(Id, TenantId, EntityType, PaymoId, LocalId, LastSyncedUtc)   -- duplicate prevention + incremental sync
MigrationErrors(Id, MigrationJobId, PaymoId, EntityType, Message, PayloadJson, RetryCount)
```

### 2.3 Key relationships (ER summary)

- `Tenant 1—* Workspace 1—* Project 1—* Task` (self-referencing for subtasks).
- `Task *—* User` via assignee (1) + watchers (many).
- `Task 1—* TimeEntry *—1 User`.
- `Project *—1 Client`, `Project *—* User` via `ProjectMembers`.
- Polymorphic `Comments` / `Files` keyed by `(EntityType, EntityId)` — keep an index on it.

### 2.4 Performance & integrity

- Filtered index on `Tasks (TenantId, ProjectId, Status) WHERE IsDeleted = 0`.
- Index `TimeEntries (TenantId, UserId, StartUtc)` for timesheet queries.
- Use **`DATETIME2` + UTC everywhere**; convert to user `TimeZone` at the edge.
- Concurrency: `rowversion` on Tasks/TimeEntries to prevent lost updates from web + mobile.
- Reporting reads can go through indexed views or a read replica later.

---

## 3. API Structure

### 3.1 Conventions

- Base: `https://api.taskflow.app/api/v1/...`
- Versioning via URL segment (`/v1`) using `Asp.Versioning`.
- JSON, camelCase, RFC 7807 `ProblemDetails` for errors.
- Pagination: `?page=1&pageSize=25` → envelope `{ items, page, pageSize, total }`.
- Filtering/sorting: `?status=open&sort=-dueDate`.
- All write endpoints idempotency-aware where mobile retries matter (`Idempotency-Key` header).

### 3.2 Endpoint map (representative)

```
Auth
  POST   /auth/register
  POST   /auth/login                 → { accessToken, refreshToken }
  POST   /auth/refresh
  POST   /auth/logout
  POST   /auth/forgot-password
  POST   /auth/reset-password

Tenancy & Users
  GET    /me
  GET    /users            POST /users        PATCH /users/{id}    DELETE /users/{id}
  POST   /invitations      POST /invitations/{token}/accept
  GET    /roles            POST /roles        PUT  /roles/{id}/permissions
  GET    /activity-logs

Workspaces / Org
  CRUD   /workspaces  /departments  /teams  /clients
  POST   /teams/{id}/members

Projects
  CRUD   /projects
  GET    /projects/{id}/members   POST /projects/{id}/members
  CRUD   /projects/{id}/milestones
  GET    /projects/{id}/board          ← kanban payload (lists + tasks)
  GET    /projects/{id}/gantt

Tasks
  CRUD   /tasks
  PATCH  /tasks/{id}/move              ← drag&drop: { taskListId, position }
  POST   /tasks/{id}/subtasks
  CRUD   /tasks/{id}/checklist
  POST   /tasks/{id}/watchers
  CRUD   /tasks/{id}/dependencies
  GET/POST /tasks/{id}/comments
  GET/POST /tasks/{id}/attachments
  GET    /tasks/calendar?from=&to=

Time Tracking
  POST   /time/start                   ← start live timer
  POST   /time/stop
  CRUD   /time/entries
  GET    /timesheets   POST /timesheets/{id}/submit   POST /timesheets/{id}/approve

Collaboration
  CRUD   /discussions  /discussions/{id}/posts
  GET    /feed                          ← activity feed
  GET    /notifications   POST /notifications/{id}/read

Files
  POST   /files (multipart or pre-signed URL flow)
  GET    /files/{id}/download

Reporting
  GET    /reports/project-progress
  GET    /reports/productivity
  GET    /reports/time
  GET    /reports/{type}/export?format=pdf|xlsx|csv

Integration (Paymo)
  POST   /integrations/paymo/connect
  POST   /integrations/paymo/migrate          ← full migration job
  POST   /integrations/paymo/sync             ← incremental
  GET    /integrations/paymo/jobs/{id}        ← progress
  GET    /integrations/paymo/errors

Realtime (SignalR hubs, not REST)
  /hubs/tasks    /hubs/notifications    /hubs/timer
```

### 3.3 Security on every endpoint

- `[Authorize]` by default; `[AllowAnonymous]` only on auth endpoints.
- Permission-based policy: `[RequirePermission("projects.create")]`.
- Tenant isolation enforced server-side — clients **never** send `TenantId`.
- Rate-limit buckets: anonymous (low), authenticated (per-user), migration (separate heavy bucket).

---

## 4. Frontend Folder Structure

Pure HTML5 + external CSS/JS + Bootstrap 5, AJAX to the API. No SPA framework.

```
/web
├── index.html
├── /pages
│   ├── auth/login.html  register.html  reset-password.html
│   ├── dashboard.html
│   ├── projects/list.html  board.html  gantt.html  calendar.html
│   ├── tasks/detail.html
│   ├── time/timesheet.html
│   ├── reports/index.html
│   ├── admin/users.html  roles.html  settings.html
│   └── integration/paymo.html
├── /components                         ← reusable HTML partials, injected via fetch()
│   ├── navbar.html  sidebar.html  task-card.html  modal-task.html  toast.html
├── /assets
│   ├── /css
│   │   ├── theme.css                   ← variables, colors, spacing (logical properties only)
│   │   ├── layout.css  components.css  ← uses margin-inline/padding-inline/text-align:start
│   │   ├── bootstrap.css               ← LTR build (loaded when dir=ltr)
│   │   ├── bootstrap.rtl.css           ← RTL build (loaded when dir=rtl)
│   │   └── rtl.css                     ← explicit mirror overrides (icons, charts, edge cases)
│   │   └── fonts.css                   ← Arabic webfont (Cairo/Tajawal) + Latin font
│   ├── /js
│   │   ├── /core
│   │   │   ├── api.js                  ← fetch wrapper: base URL, JWT header, auto-refresh, error toast
│   │   │   ├── auth.js                 ← token storage, login/logout, route guard
│   │   │   ├── i18n.js                 ← language switch (ar/en), loads /locales/*.json, sets dir=rtl/ltr
│   │   │   ├── store.js                ← lightweight client state
│   │   │   ├── realtime.js             ← SignalR connection
│   │   │   └── components.js           ← loadComponent(), render helpers
│   │   ├── /modules
│   │   │   ├── projects.js  tasks.js  board.js (drag&drop)  timer.js
│   │   │   ├── reports.js  notifications.js  admin.js  paymo.js
│   │   └── /vendor                     ← bootstrap.bundle.min.js, chart.js, sortablejs, signalr
│   ├── /images
│   └── /locales
│       ├── en.json
│       └── ar.json
└── /docs  (optional component style guide)
```

**Frontend conventions**
- One `module.js` per page; each exports an `init()` called on `DOMContentLoaded`.
- All API calls go through `core/api.js` — single place for JWT + refresh + 401 handling.
- Drag & drop via **SortableJS**; charts via **Chart.js**; realtime via **@microsoft/signalr**.
- No inline CSS/JS — enforced via a simple lint step in CI.

### 4.1 Full Bilingual + RTL/LTR (whole-page mirroring — first-class requirement)

The app is **fully bilingual (English + Arabic)** with **complete layout direction switching** — the entire page mirrors, not just translated text. This is built in from day one on every page.

**1. Direction lives at the root**
- `<html lang="en" dir="ltr">` ↔ `<html lang="ar" dir="rtl">`. The language toggle sets **both** attributes; the browser mirrors the whole layout (sidebar moves to the right, alignment flips, etc.).

**2. CSS uses logical properties (auto-flipping)**
- `margin-inline-start/end`, `padding-inline-*`, `inset-inline-*`, `text-align: start/end`, `border-inline-*` — these flip automatically with `dir`. **No `left`/`right` in layout CSS.**
- Load `bootstrap.rtl.css` when `dir=rtl`, `bootstrap.css` when `dir=ltr` (swapped by `i18n.js`).

**3. Explicitly mirrored elements** (`rtl.css` under `[dir="rtl"]`)
- Directional icons (arrows, chevrons, back/forward, breadcrumbs) → `transform: scaleX(-1)` or icon swap.
- Kanban column order, Gantt timeline direction, progress bars, sliders.
- Sidebar/drawer slide direction; toast/notification anchor side.

**4. Translation system**
- Every text node carries `data-i18n="key"` (and `data-i18n-attr` for placeholders/titles); `i18n.js` swaps from `locales/en.json` / `locales/ar.json`.
- Numbers, dates, currency formatted via `Intl.NumberFormat` / `Intl.DateTimeFormat` with the active locale (`ar` vs `en`).
- Arabic typography: dedicated webfont (Cairo / Tajawal) loaded for `lang=ar`.

**5. Persistence**
- Selected language stored on the **user profile** (server-side, `Users.Locale`) → consistent across web, mobile, email, and devices. Falls back to browser `Accept-Language` for anonymous pages.

**6. Server-side localization (.NET)**
- API returns localized **validation and error messages** based on `Accept-Language` header (ASP.NET Core `IStringLocalizer` + resource files `.ar.resx` / `.en.resx`).
- Emails and PDF/Excel exports rendered in the user's locale + direction.

**7. QA gate**
- Every page reviewed in **both** directions before it's "done"; visual checks for clipped text, wrong alignment, un-mirrored icons.

---

## 5. Mobile App Architecture (Flutter)

Two apps from **one shared codebase / mono-repo** with build flavors: `employee` and `admin`.

```
/mobile
├── /lib
│   ├── main_employee.dart   main_admin.dart      ← flavor entrypoints
│   ├── /core
│   │   ├── /network         ← Dio client, JWT interceptor, refresh, retry
│   │   ├── /storage         ← secure storage (tokens), Drift/SQLite (offline cache)
│   │   ├── /sync            ← offline queue + conflict resolution
│   │   ├── /di              ← get_it / Riverpod providers
│   │   └── /i18n            ← intl, ar + en, RTL via Directionality
│   ├── /features
│   │   ├── auth/  tasks/  time_tracking/  projects/  notifications/  collaboration/  profile/
│   │   │   each: data/ (repo+dto) · domain/ (entities+usecases) · presentation/ (screens+widgets+controller)
│   └── /shared              ← widgets, theme, constants
└── /test
```

**Patterns**
- **Clean Architecture + Riverpod** (or Bloc) for state.
- **Repository pattern**: each repo reads/writes local DB first, syncs with API.
- **Offline-first sync:** local **Drift (SQLite)** mirror; mutations recorded in an outbox table with a client-generated GUID id (hence GUID PKs help). On reconnect, the sync engine replays the outbox; conflicts resolved by `RowVersion` / last-write-wins with server authority for time entries.
- **Background timer:** native foreground service (Android) / background task (iOS) keeps the live timer running; reconciled with `/time/stop` on resume.
- **Push:** Firebase Cloud Messaging (both platforms), token registered against `NotificationPreferences`.
- **Bilingual + RTL/LTR:** `MaterialApp` driven by locale (`ar`/`en`) with `flutter_localizations` + `intl`; Flutter's `Directionality` mirrors the entire UI automatically for Arabic. Locale read from user profile, mirrored screens reviewed in both directions.

---

## 6. Development Roadmap

Phased so each phase ships something usable.

**Phase 0 — Foundation (infra)**
Solution scaffold (Clean Arch), EF Core + migrations, Docker compose (API + SQL + Redis), CI pipeline, Serilog, Swagger, health checks.

**Phase 1 — Identity & Tenancy**
Register/login, JWT + refresh tokens, RBAC, multi-tenant interceptor + query filters, invitations, activity/audit logging. Web auth pages.

**Phase 2 — Projects & Tasks (core value)**
Projects CRUD + members + milestones. Tasks CRUD, subtasks, priorities, status, tags, checklist, comments, watchers, dependencies. Kanban board with drag & drop. SignalR realtime task updates. Web: board + list + task detail.

**Phase 3 — Time Tracking**
Live timer (start/stop), manual entries, billable flags, timesheets + approval. Web timesheet UI.

**Phase 4 — Collaboration, Files, Notifications**
Discussions, mentions, activity feed. File upload to blob storage. In-app + email notifications, preferences. Push wiring.

**Phase 5 — Calendar & Reporting**
Task calendar + Gantt view. Reports (project progress, productivity, time) with PDF/Excel/CSV export. Dashboard widgets.

**Phase 6 — Mobile apps**
Flutter employee app (tasks, timer, notifications, profile) → admin app (dashboard, approvals, monitoring). Offline sync + push.

**Phase 7 — Paymo integration & migration**
Paymo API client, full migration job, entity mapping, incremental scheduled sync, error logging + retry, validation, duplicate prevention.

**Phase 8 — Billing, hardening, launch**
Subscription/billing (PayPal — your preferred provider), plan limits, security review, load testing, monitoring/alerting, staging→prod rollout.

---

## 7. Suggested Timeline

Assumes a small focused team (2 backend, 1 frontend, 1 mobile, shared QA/DevOps). Calendar weeks, parallelized.

| Phase | Scope | Duration |
|-------|-------|----------|
| 0 | Foundation & infra | 2 weeks |
| 1 | Identity & tenancy | 3 weeks |
| 2 | Projects & tasks + Kanban + realtime | 6 weeks |
| 3 | Time tracking | 3 weeks |
| 4 | Collaboration, files, notifications | 4 weeks |
| 5 | Calendar & reporting | 4 weeks |
| 6 | Mobile (both apps) | 8 weeks (overlaps 4–5) |
| 7 | Paymo migration & sync | 4 weeks |
| 8 | Billing, hardening, launch | 4 weeks |

**Realistic MVP (Phases 0–3): ~14 weeks.**
**Full platform incl. mobile & migration: ~7–8 months** with overlap. Solo developer: roughly 2–2.5×.

---

## 8. Best Practices

**Backend**
- Clean Architecture + SOLID; domain has zero framework dependencies.
- CQRS via MediatR; thin controllers (validate → send → return).
- Repository + Unit of Work over EF Core; no business logic in controllers.
- FluentValidation in a pipeline behavior; never trust client input.
- Async all the way; `CancellationToken` plumbed through.
- Architecture tests (NetArchTest) to enforce layer rules in CI.

**Security**
- Argon2id or ASP.NET Identity PBKDF2 for password hashing.
- Short-lived access tokens (15 min) + rotating refresh tokens (revocable, hashed at rest).
- HTTPS/HSTS enforced; secure headers (CSP, X-Frame-Options).
- Tenant isolation server-side only; permission checks per endpoint.
- File access via signed, expiring URLs; validate content-type + size; scan if possible.
- Secrets in Key Vault / Secrets Manager — never in source.
- OWASP Top 10 review before launch; audit logs immutable/append-only.

**Frontend**
- Semantic HTML5, Bootstrap utility classes, external CSS/JS only.
- Single API wrapper for auth/refresh; central error toasts.
- i18n + RTL from day one (don't retrofit).
- Debounce search, optimistic UI for drag & drop with server reconcile.

**Data**
- UTC everywhere; soft delete + audit on all tenant tables.
- Migrations reviewed; never destructive without a backfill plan.
- Optimistic concurrency (`rowversion`) on multi-client entities.

**Process**
- Trunk-based dev, PR reviews, CI gates (build + test + lint + arch tests).
- Feature flags for risky rollouts; conventional commits.

---

## 9. Migration Strategy from Paymo

**Goal:** import all existing Paymo data, then keep in sync until cutover.

**Phase A — Connect & map**
1. User pastes Paymo API key → stored encrypted in `PaymoConnections`.
2. Build a typed `PaymoApiClient` (Polly for retry/backoff, respects Paymo rate limits).
3. Define an `EntityMappings` table: `(EntityType, PaymoId, LocalId)` — the backbone of duplicate prevention and incremental sync.

**Phase B — Full migration (dependency order)**
Run as a Hangfire job, importing in FK order so references resolve:
```
Clients → Users/Contacts → Projects → TaskLists → Tasks → Comments
        → TimeEntries → Files/Attachments → Invoices (if needed)
```
- Each record: check `EntityMappings`; insert if new, update if changed.
- Stream in batches; update `MigrationJobs.ProcessedRecords` for a live progress bar.
- Download attachments and re-upload to your blob storage, store new `StorageKey`.

**Phase C — Validation & errors**
- Post-import counts reconciliation (Paymo count vs local count per entity).
- Failures → `MigrationErrors` with payload + retry count; **retry with exponential backoff** (Polly), max N attempts, then surface in the errors UI for manual resolution.
- Data validation rules (required fields, valid dates, orphan checks) before commit.

**Phase D — Incremental sync**
- Scheduled Hangfire job (e.g. every 15 min) pulls Paymo records modified since `LastSyncUtc`.
- Upsert via `EntityMappings`; conflict policy: Paymo is source-of-truth until cutover, then disable sync.
- **Cutover:** freeze Paymo, run a final delta sync, validate, switch users to TaskFlow.

**Idempotency & safety**
- Every importer is idempotent (re-runnable without duplicates) thanks to `EntityMappings`.
- Dry-run mode that reports what *would* be imported.
- Full migration is reversible per-tenant (delete by `MigrationJobId` tag).

---

## 10. Deployment Strategy

**Containers**
- Multi-stage Dockerfiles for API and Worker (small runtime images).
- `docker-compose.yml` for local: api, worker, sqlserver, redis, seq (logs), mailhog.

**Environments**
- **Local** (compose) → **Staging** → **Production**. Identical images; config via env vars / Key Vault.

**CI/CD (GitHub Actions or Azure DevOps)**
```
CI:  restore → build → unit + integration tests → arch tests → docker build → push to registry
CD:  deploy to staging → smoke tests → manual approval → deploy to prod (rolling)
```
- EF Core migrations applied as a gated migration step (not auto on startup in prod).

**Hosting options**
- **Azure (recommended given .NET):** App Service or Container Apps + Azure SQL + Azure Cache for Redis + Blob Storage + Front Door (TLS/CDN/WAF).
- **AWS:** ECS Fargate + RDS SQL Server + ElastiCache + S3 + CloudFront.
- **Kubernetes** only when you need it — manifests in `/deploy/k8s`.

**Operations**
- Automated DB backups (point-in-time restore) + tested restore runbook.
- Monitoring: Application Insights / OpenTelemetry → traces, metrics, logs; alerts on error rate, latency, queue depth.
- Zero-downtime: rolling deploy, health-check gated, DB migrations backward-compatible.
- Secrets in Key Vault/Secrets Manager; least-privilege service identities.

---

## 11. Recommended Third-Party Services

| Need | Recommendation | Notes |
|------|----------------|-------|
| Hosting | **Azure** (Container Apps / App Service) | Best .NET fit; AWS Fargate as alternative |
| Database | **Azure SQL** / SQL Server | PostgreSQL (Azure DB for Postgres) if you prefer OSS |
| Cache + SignalR backplane | **Redis** (Azure Cache for Redis) | also Hangfire-adjacent caching |
| File storage | **Azure Blob** / **AWS S3** / **MinIO** (self-host) | signed URLs |
| Background jobs | **Hangfire** | SQL-backed, has dashboard |
| Email | **SendGrid** / **Amazon SES** / **Postmark** | transactional + templates |
| Push notifications | **Firebase Cloud Messaging** | Android + iOS |
| Realtime | **ASP.NET Core SignalR** (+ Azure SignalR Service for scale) | |
| Auth (optional managed) | self-host JWT, or **Auth0 / Azure AD B2C** | only if you want managed identity |
| Payments / billing | **PayPal Subscriptions** | your preferred provider; redirect to PayPal autopay |
| Logging / observability | **Serilog** + **Seq** (dev) / **Application Insights** (prod) | |
| Error tracking | **Sentry** | backend + Flutter |
| PDF/Excel export | **QuestPDF** (PDF) + **ClosedXML** (Excel) | both .NET-native, no licensing traps |
| CI/CD | **GitHub Actions** / **Azure DevOps** | |
| Secrets | **Azure Key Vault** / **AWS Secrets Manager** | |
| Mobile state | **Riverpod**; local DB **Drift**; HTTP **Dio** | offline-first |

---

## Next Step

This is the design. The natural next move is **Phase 0 + Phase 1**: scaffold the .NET 9 solution (Clean Architecture, EF Core, Docker, JWT auth with refresh tokens, multi-tenancy, RBAC) so you have running code. Say the word and I'll generate it.
