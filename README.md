# Northwind Support Desk

A support ticket management system: raise tickets, search and filter them, assign them to agents, escalate tickets with full audit history, and monitor SLA targets.

**.NET 10 Web API** + **React 19 / TypeScript** + **SQL Server**, in one monorepo.

---

## Quick Start

### Option A: Local Development (Host API + Docker SQL Server)

**Prerequisites:** .NET SDK 10 (`global.json`), Node.js 22 (`.nvmrc`; 20.19+ works), Docker.

```bash
cp .env.example .env          # optional, every value has a working default
npm install                   # installs the web app dependencies
npm run db:up                 # SQL Server 2022 in Docker on localhost:1433
npm run api                   # terminal 1: migrates, seeds, then serves the API
npm run web                   # terminal 2: React dev server on http://localhost:5173
```

### Option B: Full Backend in Docker (`docker compose`)

```bash
docker compose down -v && docker compose up --build -d
VITE_API_PROXY_TARGET=http://localhost:5080 npm run web
```

| Service | Local Host URL | Docker URL |
| --- | --- | --- |
| Web app | `http://localhost:5173` | `http://localhost:5173` (proxied to `5080`) |
| API | `https://localhost:7043` (`http://localhost:5043`) | `http://localhost:5080` |
| Health check | `https://localhost:7043/health` | `http://localhost:5080/health` |
| Swagger | `https://localhost:7043/swagger` | `http://localhost:5080/swagger` |

---

## Development Login Credentials

All seeded active support agents share the same development password in local/demo environments:

| Agent Name | Email | Password | Category / Status |
| --- | --- | --- | --- |
| **Alex Turner** *(recommended)* | `alex.turner@northwind-support.example` | `Dev_Support_Pass1!` | Billing & Invoicing (Active) |
| **Priya Nair** | `priya.nair@northwind-support.example` | `Dev_Support_Pass1!` | Authentication & Access (Active) |
| **Marcus Vance** | `marcus.vance@northwind-support.example` | `Dev_Support_Pass1!` | Integrations & API (Active) |
| **Elena Rostova** | `elena.rostova@northwind-support.example` | `Dev_Support_Pass1!` | Performance & Outages (Active) |
| **Samira Hadid** | `samira.hadid@northwind-support.example` | `Dev_Support_Pass1!` | Data Import & Export (Active, at capacity) |
| **Devon Brooks** | `devon.brooks@northwind-support.example` | `Dev_Support_Pass1!` | Billing & Invoicing (**Inactive** — login rejected with `401`) |

---

## What Was Implemented & Business Rules

### Task 1: Frontend Filter Defect (`DEFECT-117`) & Server-Side SQL Filtering (`FL-1`–`FL-6`)
- **Defect Fix (`DEFECT-117`)**: Fixed stale closure / missing dependency bug in `useTicketList.ts` so changing any filter (`status`, `priority`, `categoryId`, `assignedAgentId`, `unassignedOnly`, `slaStatus`) or sort column immediately re-queries the API, including when already on page 1.
- **Server-Side Filtering**: Implemented composable `IQueryable<Ticket>` filtering in `TicketQueries.ListAsync` executed in SQL **before** `CountAsync` and **before** `OrderBy`/`Skip`/`Take`:
  - `search`: case-insensitive match on `Title` or `Description` (trimmed; blank ignored).
  - `status`, `priority`, `categoryId`, `customerId`, `assignedAgentId`: exact match when supplied.
  - `unassignedOnly`: when `true`, returns tickets where `AssignedAgentId IS NULL` and takes precedence over `assignedAgentId` (`FL-3`).
  - `slaStatus`: filters `OnTrack`, `AtRisk`, `Breached`, and `Met` directly in SQL using the ticket's `CreatedAtUtc`, `TargetResolutionAtUtc`, `Status`, and configured `AtRiskThresholdPercent`.

### Task 2: Ticket Escalation, Auto-Assignment & SLA Policy (`BR-1`–`BR-8`, `UI-1`–`UI-5`)
- **Priority Auto-Bump (`BR-1`)**: On `POST /api/tickets`, Premium customers requesting `Low` or `Medium` priority have their ticket automatically bumped one level (`Low -> Medium`, `Medium -> High`). `High` and `Critical` remain unchanged; Standard customers keep their requested priority.
- **Auto-Assignment (`BR-2`, `BR-6`)**: Selects an active agent in the ticket's category whose open ticket count (`Status NOT IN (Resolved, Closed)`) is strictly below `MaxConcurrentTickets`, choosing the agent with the lowest open workload (tie-broken by lowest `Agent.Id`). If no eligible agent exists, the ticket remains unassigned (`AutoAssigned = false`).
- **SLA Calculation & Status (`BR-3`, `BR-7`, `BR-8`)**:
  - Configurable base windows in `appsettings.json` (`Low = 72h`, `Medium = 24h`, `High = 8h`, `Critical = 4h`), halved (`0.5x`) for Premium customers with a `1h` minimum floor.
  - Status evaluation: `Met` (`Resolved` or `Closed`), `Breached` (active and `now > TargetResolutionAtUtc`), `AtRisk` (active, not breached, and `<= 25%` of total SLA window remaining), `OnTrack` (otherwise).
- **Ticket Escalation (`BR-4`–`BR-6`, `API-1`–`API-3`)**:
  - `POST /api/tickets/{id}/escalate` validates a required reason (`10–500` chars after trimming), rejects `Resolved`/`Closed` tickets with `409 Conflict`, bumps priority one level up to `Critical`, recalculates the SLA target (`min(existingTarget, CreatedAtUtc + newWindow)` so an existing deadline is never extended), optionally re-assigns to another eligible agent in the same category when `reassign = true` (retaining the current assignee if none is available), and persists an immutable `TicketEscalation` audit record.
  - `GET /api/tickets/{id}/escalations` returns the ticket's escalation history newest-first (`404` if the ticket does not exist).

### Task 3: Login & Authentication (`AU-1`–`AU-13`)
- **Credentials & Hashing (`AU-1`, `AU-2`)**: Added `PasswordHash` to `Agent` via EF Core migration `AddAgentPasswordHash`. Passwords are hashed and verified with PBKDF2-HMAC-SHA256 (`100,000` iterations, 128-bit random salt, 256-bit subkey, constant-time `CryptographicOperations.FixedTimeEquals`).
- **Endpoints (`AU-3`–`AU-5`)**:
  - `POST /api/auth/login` (`[AllowAnonymous]`): validates input (`400` on blank/malformed email), verifies active agent & password (`401` with generic message `"Invalid email or password."` for unknown email, wrong password, or inactive agent), and returns `{ token, expiresAtUtc, user }`.
  - `GET /api/auth/me`: returns the authenticated agent's profile (`id`, `fullName`, `email`, `categoryId`, `categoryName`) or `401`.
- **Central Policy & Actor Attribution (`AU-7`, `AU-8`)**:
  - Configured a central fallback authorization policy in `Program.cs` requiring an authenticated user on every endpoint unless opted out with `[AllowAnonymous]`.
  - `POST /api/tickets/{id}/escalate` records `EscalatedBy` from the authenticated agent's JWT `Name` claim, ignoring any spoofed value in the request body.
- **React Auth Flow (`AU-9`–`AU-13`)**: Added `/login` page, `RequireAuth` route guard that redirects unauthenticated users to `/login` and returns them to their original destination after login, header user badge + sign-out button, automatic `Authorization: Bearer <token>` header injection in `api/client.ts`, and automatic session clearing + redirect on any `401` response.

### Task 4: Dockerise the API (`DK-1`–`DK-8`)
- Multi-stage `apps/api/Dockerfile` (SDK 10 build with cached NuGet restore layer -> ASP.NET 10 runtime on HTTP port `8080` running as non-root `$APP_UID`) and `.dockerignore`.
- Added `api` service to `docker-compose.yml` published on `localhost:5080`, waiting for `sqlserver` healthcheck, configured exclusively via runtime environment variables, and added a central `[AllowAnonymous]` `GET /health` endpoint checking database connectivity.

---

## Design Decisions (Chosen vs. Rejected)

### 1. Domain Policy Objects (`TicketTriagePolicy`, `SlaPolicy`, `SlaEvaluator`) vs. Handler Logic
- **Chosen**: Placed priority bumping, candidate agent selection, and SLA window calculation in pure, stateless domain policy classes (`TicketTriagePolicy`, `SlaPolicy`, `SlaEvaluator`) inside `SupportDesk.Domain`, and placed state mutation invariants (`Ticket.Escalate(...)`) on the `Ticket` aggregate root.
- **Rejected**: Writing triage rules directly inside `RaiseTicketCommandHandler` and `EscalateTicketCommandHandler`, or introducing a MediatR pipeline / domain event dispatcher.
- **Why**: Both ticket creation and ticket escalation share SLA calculation (`BR-3`, `BR-5`) and category workload agent selection (`BR-2`, `BR-6`). Pure domain policies eliminate duplication, keep `SupportDesk.Domain` free of infrastructure dependencies, and allow fast, deterministic unit testing with a fake clock (`FakeDateTimeProvider`) and zero mocking overhead.

### 2. SQL-Side `SlaStatus` Filtering vs. In-Memory Evaluation
- **Chosen**: Translated all filters—including `SlaStatus` (`OnTrack`, `AtRisk`, `Breached`, `Met`)—into `IQueryable<Ticket>` expressions in `TicketQueries.ApplyFilters` before `CountAsync` and `Skip`/`Take`.
- **Rejected**: Fetching all tickets into memory and calling `SlaEvaluator.Evaluate` in C# before paging, or storing a persisted `SlaStatus` column on `Tickets`.
- **Why**: In-memory filtering breaks SQL pagination and loads the entire table on every list request (`FL-1`). Storing `SlaStatus` in a column would go stale as time passes (`AtRisk` -> `Breached`) without a background polling job (which is out of scope). Computing the threshold in the SQL `WHERE` clause against `clock.UtcNow` guarantees accurate counts and paging at query time.

### 3. Self-Contained PBKDF2 & HMAC-SHA256 JWT Services vs. ASP.NET Core Identity
- **Chosen**: Implemented `Pbkdf2PasswordHasher` (`Rfc2898DeriveBytes.Pbkdf2`) and `JwtTokenService` (RFC 7519 HS256 JWT signing/validation) behind `IPasswordHasher` and `ITokenService` interfaces in `SupportDesk.Application`, wired into ASP.NET Core's authentication pipeline via `BearerTokenAuthenticationHandler` and a central `FallbackPolicy`.
- **Rejected**: Pulling in `Microsoft.AspNetCore.Identity.EntityFrameworkCore` (`UserManager`, `SignInManager`, `AspNetUsers` tables).
- **Why**: Full ASP.NET Core Identity would introduce 7+ schema tables and leak framework concerns into the `Agent` domain aggregate. Using BCL cryptography (`System.Security.Cryptography`) keeps `SupportDesk.Domain` and `SupportDesk.Application` completely dependency-free (preserving all `LayerDependencyTests`) while honouring `IDateTimeProvider` for deterministic token expiration unit tests.

---

## Defect `DEFECT-117`: Root Cause, Fix & Verification

- **Root Cause**: In `apps/web/src/features/tickets/hooks/useTicketList.ts`, the `useEffect` responsible for calling `listTickets(filters)` had an incomplete dependency list (`[filters.search, filters.page, filters.pageSize]`) with `// eslint-disable-next-line react-hooks/exhaustive-deps`. Whenever a user changed `status`, `priority`, `categoryId`, `assignedAgentId`, `unassignedOnly`, `slaStatus`, or table sorting while already on page 1 (`filters.page` stayed `1`), React did not re-run the effect, leaving stale tickets on screen.
- **Fix**: Wrapped `load` in `useCallback(..., [filters])` and updated `useEffect` to depend on `[load]`, removing the `eslint-disable` comment.
- **How to Verify**:
  1. Run `npm run test --workspace apps/web` — `useTicketList.test.ts` renders the hook on page 1, updates `filters.status` to `'Open'`, and asserts `listTickets` is immediately invoked a second time with `{ page: 1, pageSize: 10, status: 'Open' }`.
  2. In the browser on `/tickets` (while on Page 1), select any Status or Priority dropdown filter and observe the immediate `GET /api/tickets?...` network request and updated table rows.

---

## Server-Side Filtering: Architecture & SQL Verification

- **Where Applied**: `apps/api/src/SupportDesk.Infrastructure/Queries/TicketQueries.cs` in `ApplyFilters(IQueryable<Ticket> query, ListTicketsQuery filter, DateTime utcNow, SlaPolicy slaPolicy)`. `ApplyFilters` is called on the base `AsNoTracking()` query before `CountAsync` and before `ApplyOrdering(...).Skip(...).Take(...)`.
- **How the SQL Was Checked**:
  - Enabled EF Core SQL command logging and verified with `query.ToQueryString()` and relational SQLite unit tests (`TicketQueriesTests.cs`) that every filter translates to SQL `WHERE` predicates and no client-side evaluation occurs.
- **How to Add the Next Filter**:
  1. Add the optional parameter to `ListTicketsQuery` (`SupportDesk.Application/Features/Tickets/Queries/ListTickets/ListTicketsQuery.cs`).
  2. Add the corresponding `if (filter.NewField is not null) query = query.Where(...);` clause in `TicketQueries.ApplyFilters`.
  3. Add a test case in `TicketQueriesTests.cs`.

---

## Authentication Details

- **Credential Storage & Hashing**: Stored in `Agents.PasswordHash` (`nvarchar(256)`). Formatted as `v1$<iterations>$<base64-salt>$<base64-subkey>` using PBKDF2-HMAC-SHA256 with `100,000` iterations, a 16-byte cryptographic random salt per hash, and a 32-byte derived subkey verified with `CryptographicOperations.FixedTimeEquals`.
- **Token Issue, Storage & Lifetime**:
  - Issued by `JwtTokenService` signed with `HMACSHA256` using `Jwt:SigningKey` from configuration/environment variables.
  - Default lifetime is `480` minutes (8 hours, configurable via `Jwt__ExpiryMinutes`), with `sub` (Agent ID), `email`, `name`, `iat`, and `exp` claims.
  - Stored in browser `sessionStorage` (`supportdesk.auth.session`) so closing the browser tab clears the session, and attached via `Authorization: Bearer <token>` in `apps/web/src/api/client.ts`.
- **What Would Change Before Production**:
  - Store the signing key in a managed secret store (Azure Key Vault / AWS Secrets Manager) or switch to asymmetric signing (`RS256`/`ES256`) backed by an internal OIDC identity provider.
  - Issue short-lived access tokens (15 minutes) paired with `HttpOnly`, `Secure`, `SameSite=Strict` refresh token cookies with token rotation and revocation.
  - Add rate limiting / account lockout on `POST /api/auth/login`.

---

## Database Changes & Indexing Justification

1. **`20261022114559_AddTicketEscalations`**:
   - Created table `TicketEscalations` (`Id`, `TicketId`, `FromPriority`, `ToPriority`, `Reason`, `EscalatedBy`, `EscalatedAtUtc`, `PreviousAssignedAgentId`, `NewAssignedAgentId`) with `ON DELETE CASCADE` from `Tickets` and `ON DELETE NO ACTION` to `Agents`.
   - **Deliberate Index**: Added composite index `IX_TicketEscalations_TicketId_EscalatedAtUtc` on `(TicketId, EscalatedAtUtc)`.
   - **Why**: Every ticket detail load queries `WHERE TicketId = @id ORDER BY EscalatedAtUtc DESC` (`GET /api/tickets/{id}/escalations`), and list/detail projections count escalations per `TicketId`. Indexing `(TicketId, EscalatedAtUtc)` allows SQL Server to satisfy both the equality lookup on `TicketId` and the sort order on `EscalatedAtUtc` in a single index seek without a sort operator or table scan.
2. **`20261022183720_AddAgentPasswordHash`**:
   - Added `PasswordHash` (`nvarchar(256) NOT NULL DEFAULT ''`) to `Agents` alongside the existing unique index `IX_Agents_Email` used by `POST /api/auth/login`.

---

## Docker Build, Verification & Production Migration Strategy

### Commands & Environment Variables
- Build and start both SQL Server and the API container from a clean state:
  ```bash
  docker compose down -v && docker compose up --build -d
  ```
- Run the React frontend against the Dockerised API:
  ```bash
  VITE_API_PROXY_TARGET=http://localhost:5080 npm run web
  ```
- Key runtime environment variables (documented in `.env.example`):
  - `MSSQL_SA_PASSWORD`: SQL Server SA password.
  - `ConnectionStrings__Default`: SQL Server connection string injected at container startup.
  - `Jwt__SigningKey`, `Jwt__Issuer`, `Jwt__Audience`, `Jwt__ExpiryMinutes`: JWT configuration injected at runtime.
  - `Database__MigrateAndSeedOnStartup`: When `true` in `docker-compose.yml`, applies EF Core migrations and seeds demo data on an empty volume.

### Verification Checks
1. **Fresh Volume Startup & Health Check**:
   - `docker compose down -v && docker compose up --build -d` starts `sqlserver`, waits for its healthcheck, starts `api` on port `5080`, applies migrations, and seeds the 40 demo tickets.
   - `curl http://localhost:5080/health` returns `200 OK` with `{"status":"Healthy","database":"Reachable"}` without requiring a token.
   - Logging in via `POST http://localhost:5080/api/auth/login` and calling `GET http://localhost:5080/api/tickets` returns the seeded tickets (`totalCount: 40`).
2. **Layer Caching**:
   - `apps/api/Dockerfile` copies `global.json`, `Directory.Build.props`, `Directory.Packages.props`, and `.csproj` files before `dotnet restore`. Editing a `.cs` file and re-running `docker compose build api` reuses the `CACHED` restore layer and only recompiles the changed source layer.
3. **Runtime vs. SDK Image Size**:
   - The `mcr.microsoft.com/dotnet/sdk:10.0` build stage is ~1.18 GB uncompressed, whereas the final `northwind-support-api:latest` runtime image built on `mcr.microsoft.com/dotnet/aspnet:10.0` is **315 MB** (~73% smaller) with no SDK or build caches included.
4. **Non-Root User**:
   - `docker compose exec api whoami` prints `app` (UID `1654`), confirming the container does not run as `root`.

### How Migrations Would Run in a Real Production Deployment
Running `Database.MigrateAsync()` inside `Program.cs` on application startup is convenient for local Docker Compose, but unsafe in production where multiple API replicas start concurrently (risking migration race conditions, lock timeouts, and coupling app startup latency/permissions to schema DDL privileges). In production:
1. Build an **EF Core migration bundle** (`dotnet ef migrations bundle --self-contained`) or generate an idempotent SQL script (`dotnet ef migrations script --idempotent`) in CI.
2. Run the migration bundle as a dedicated, single-instance **pre-deployment step / init job** using a migration-specific database credential with DDL permissions, while the API containers run under a least-privilege DML-only database user (`SELECT`, `INSERT`, `UPDATE`, `DELETE`).
3. Keep all schema changes backward-compatible (expand-and-contract pattern) so rolling deployments never break running replicas.

---

## Testing Summary

### How to Run
```bash
npm test                                                  # Runs all backend (.NET) and frontend (Vitest) tests
dotnet test SupportDesk.sln --configuration Release       # 65 unit tests + 4 architecture/model tests
npm run test --workspace apps/web                         # 14 frontend tests across 5 test suites
npm run lint && npm run typecheck                         # ESLint + TypeScript strict verification
```

### What Was Tested
- **Backend Unit & Relational Tests (`65` tests in `SupportDesk.UnitTests`)**:
  - `TicketQueriesTests`: Every individual filter (`search`, `status`, `priority`, `categoryId`, `customerId`, `assignedAgentId`, `unassignedOnly`, `slaStatus`), combined filters, `unassignedOnly` precedence over `assignedAgentId`, and filtered `TotalCount` accuracy before pagination against in-memory SQLite.
  - `SlaEvaluatorTests`: Standard vs. Premium SLA windows across all priorities, minimum floor clamping, `OnTrack` / `AtRisk` / `Breached` / `Met` boundary transitions using `FakeDateTimeProvider`, and non-extension of deadlines on escalation.
  - `TicketTriagePolicyTests`: Premium priority auto-bump (`Low -> Medium`, `Medium -> High`, `High/Critical` unchanged), active agent workload selection, capacity exclusion, tie-breaking by lowest ID, and excluding the current assignee on escalation re-assignment.
  - `EscalateTicketCommandHandlerTests` & `RaiseTicketCommandHandlerTests`: End-to-end command orchestration, validation (`reason` length boundaries), `409 Conflict` on `Resolved`/`Closed` tickets, `404` on unknown IDs, and actor attribution from the authenticated token.
  - `AuthTests`: PBKDF2 hashing & verification, login success (`200`), wrong password (`401`), unknown email (`401`), inactive agent (`401`), token expiry validation, and authenticated actor override on escalation.
- **Architecture & Persistence Model Tests (`4` tests in `SupportDesk.ArchitectureTests`)**:
  - Enforces Clean Architecture layer dependencies and verifies `SupportDbContext` has no pending unmigrated model changes (`HasPendingModelChanges() == false`).
- **Frontend Tests (`14` tests in `apps/web`)**:
  - `useTicketList.test.ts`: Regression test for `DEFECT-117` (changing `status` on page 1 re-fetches immediately).
  - `SlaCell.test.tsx`: Renders accessible text + symbol + semantic tone for `OnTrack`, `AtRisk`, `Breached`, and `Met`.
  - `EscalationPanel.test.tsx`: Disables escalation for `Resolved`/`Closed` tickets, validates short reasons client-side, and submits valid escalations.
  - `Auth.test.tsx`: Unauthenticated redirect to `/login`, login error display on `401`, successful sign-in redirect with `Authorization: Bearer` header injection, and automatic session clearing on `401`.

### What Was Deliberately Not Tested
- Full browser E2E tests (Playwright/Cypress) against a live SQL Server container were omitted to keep `npm test` fast, self-contained, and runnable on any machine without requiring Docker to be running during unit test execution.

---

## Effective AI Usage Disclosure

- **Tools & Purpose**: Used Google Antigravity (AI pair-programming assistant) to explore the existing Clean Architecture boundaries, scaffold boilerplate DTOs/components, generate EF Core migrations, and assist with writing unit test cases.
- **Workflow That Worked Well**: Breaking each task into an isolated feature branch (`fix/defect-117`, `feat/server-side-filtering`, `feat/ticket-escalation`, `feat/authentication`, `feat/dockerise-api`), implementing domain policies and unit tests first, and running `SupportDesk.ArchitectureTests` after each change to ensure Clean Architecture constraints were never violated.
- **Accepted As-Is**: Boilerplate React test harness setup and FluentValidation validator structure (`EscalateTicketRequestValidator`, `LoginRequestValidator`), which matched the existing `RaiseTicketRequestValidator` pattern cleanly.
- **Changed or Rejected & Why**:
  - *Rejected*: Adding `Microsoft.AspNetCore.Authentication` or `System.IdentityModel.Tokens.Jwt` package references to `SupportDesk.Infrastructure` or `SupportDesk.UnitTests`, because `Directory.Packages.props` uses Central Package Management and `LayerDependencyTests` strictly prohibits `SupportDesk.Infrastructure` from referencing `Microsoft.AspNetCore`. Instead, implemented RFC 7519 HMAC-SHA256 JWT signing/verification directly using `System.Security.Cryptography.HMACSHA256` and `System.Text.Json`.
  - *Changed*: Initial test assertion in `AuthTests.cs` used `principal.FindFirstValue(ClaimTypes.NameIdentifier)`, which is an extension method from `Microsoft.AspNetCore.Authentication`. Replaced it with standard BCL `principal.FindFirst(ClaimTypes.NameIdentifier)?.Value`.
- **Bugs Caught & Verification**: Every line of generated code was verified by compiling with `EnforceCodeStyleInBuild=true`, running all 69 .NET tests (`SupportDesk.UnitTests` + `SupportDesk.ArchitectureTests`), running `npm run lint`, `npm run typecheck`, and 14 Vitest tests, and verifying the live Docker Compose stack against SQL Server.

---

## Known Limitations

The following items were kept out of scope per the assessment specification and would be addressed in a production roadmap:
- **Authentication & Identity**: No self-service registration, password reset, MFA, OAuth/SSO, refresh token rotation, or login rate limiting / account lockout; `sessionStorage` holds the bearer token in the browser tab rather than an `HttpOnly` cookie backed by a BFF.
- **Role-Based Access Control (RBAC)**: All authenticated active agents have equal permissions; there is no supervisor vs. tier-1 agent role matrix.
- **Background SLA Notifications & Real-Time Updates**: SLA status is computed dynamically on read/query; there is no background worker emitting webhook/email alerts when a ticket crosses into `AtRisk`/`Breached`, nor SignalR/WebSocket live updates on the ticket board.
- **Pagination at Multi-Million Row Scale**: `OFFSET / FETCH` (`Skip`/`Take`) with `COUNT(*)` works well for thousands of tickets, whereas scaling to 10M+ rows would replace offset paging with keyset (cursor) pagination and full-text search indexes (`CONTAINS`) instead of `LIKE '%term%'`.
