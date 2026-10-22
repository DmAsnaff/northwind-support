# Technical Approach & Engineering Walkthrough

This document explains the investigation, architectural reasoning, and verification strategy applied across Tasks 1–4 of the Northwind Support Desk technical assessment.

---

## Task 1: Fix Frontend Filter Defect (`DEFECT-117`) & Implement Server-Side Filtering

### 1.1 Investigating `DEFECT-117`
1. **Reproduction & Code Trace**:
   - Inspecting the ticket list feature (`apps/web/src/features/tickets/TicketListPage.tsx` -> `TicketFilters.tsx` -> `useTicketList.ts`), `TicketListPage` maintained a single `TicketListParams` state object (`filters`) and updated it properly via `updateFilter` whenever any dropdown (`status`, `priority`, `categoryId`, `assignedAgentId`, `unassignedOnly`, `slaStatus`) or sort header changed.
   - Inside `useTicketList.ts`, however, the data-fetching `useEffect` had `// eslint-disable-next-line react-hooks/exhaustive-deps` and an incomplete dependency array: `[filters.search, filters.page, filters.pageSize]`.
   - When a filter change did not alter `page` (for example, changing a dropdown while already on `page === 1`, or toggling sort columns), React never re-ran the `useEffect`, leaving the table showing stale rows despite the query state having changed.
2. **Fix**:
   - Wrapped `load` in `useCallback(..., [filters])` and made `useEffect` depend on `[load]`, removing the `eslint-disable` suppression.
   - Added a regression unit test (`apps/web/src/features/tickets/hooks/useTicketList.test.ts`) using `@testing-library/react`'s `renderHook` to verify that updating `status` while remaining on page 1 immediately triggers `listTickets` with `{ page: 1, pageSize: 10, status: 'Open' }`.

### 1.2 Server-Side SQL Filtering (`FL-1` to `FL-6`)
1. **Problem**:
   - `TicketQueries.ListAsync` in `SupportDesk.Infrastructure/Queries/TicketQueries.cs` built an `AsNoTracking()` `IQueryable<Ticket>`, ignored the filter fields on `ListTicketsQuery`, counted all rows in `Tickets`, and paged unfiltered results.
2. **Implementation**:
   - Added a composable static `ApplyFilters(IQueryable<Ticket> query, ListTicketsQuery filter)` helper inside `TicketQueries` that chains `.Where(...)` predicates **before** `CountAsync` and **before** `OrderBy`/`Skip`/`Take`:
     - `Search`: trimmed, case-insensitive `EF.Functions.Like(t.Title, pattern) || EF.Functions.Like(t.Description, pattern)` with SQLite-safe fallback (`ToLower().Contains(...)`) so all unit tests run against in-memory SQLite while SQL Server uses `LIKE`.
     - `Status`, `Priority`, `CategoryId`, `CustomerId`: exact column predicates when non-null.
     - `UnassignedOnly` vs `AssignedAgentId`: when `UnassignedOnly == true`, filters `t.AssignedAgentId == null` and takes precedence over `AssignedAgentId` (`FL-3`).
     - `SlaStatus`: translated into SQL predicates comparing `t.TargetResolutionAtUtc` and `t.CreatedAtUtc` against `now` (`clock.UtcNow`) and the configured `AtRiskThresholdPercent`:
       - `Met`: `t.Status == Resolved || t.Status == Closed`
       - `Breached`: `t.Status != Resolved && t.Status != Closed && now > t.TargetResolutionAtUtc`
       - `AtRisk`: active ticket where `now <= t.TargetResolutionAtUtc` and remaining window is within `AtRiskThresholdPercent` (`t.CreatedAtUtc.AddSeconds((1.0 - thresholdRatio) * EF.Functions.DateDiffSecond(t.CreatedAtUtc, t.TargetResolutionAtUtc)) <= now`)
       - `OnTrack`: active ticket where `now <= t.TargetResolutionAtUtc` and not yet within the at-risk window.
3. **Verification**:
   - Verified with SQLite relational tests (`TicketQueriesTests.cs`) covering every individual filter, combined filters (`Status + Priority + UnassignedOnly`), `UnassignedOnly` precedence over `AssignedAgentId`, `SlaStatus` filtering, and verifying that `TotalCount` equals the filtered total before pagination.

---

## Task 2: Ticket Escalation, Auto-Triage & SLA Policy

### 2.1 Domain Modelling & SOLID Boundaries
To keep business rules pure, deterministic, and shared across ticket creation and manual escalation without duplicating logic:
1. **`SlaPolicy` (`SupportDesk.Domain/Policies/SlaPolicy.cs`)**:
   - Encapsulates configurable base SLA hours per `TicketPriority` (`Low: 72h, Medium: 24h, High: 8h, Critical: 4h`), `PremiumCustomerMultiplier` (`0.5`), `MinimumWindowHours` (`1h`), and `AtRiskThresholdPercent` (`25%`).
   - Bound from `appsettings.json` (`Sla` section) via `SlaSettings` in Infrastructure so no magic numbers are scattered across handlers.
2. **`SlaEvaluator` (`SupportDesk.Domain/Policies/SlaEvaluator.cs`)**:
   - Pure domain service computing `CalculateTargetResolutionUtc` (respecting `BR-3` and `BR-5`'s rule that escalation never extends an existing deadline) and `Evaluate(ticket, utcNow)` (`Met`, `Breached`, `AtRisk`, `OnTrack`).
3. **`TicketTriagePolicy` (`SupportDesk.Domain/Policies/TicketTriagePolicy.cs`)**:
   - Pure domain service implementing:
     - `EvaluateCreationPriority(requestedPriority, customer)` (`BR-1`: Premium customers requesting `Low` or `Medium` are bumped one level).
     - `EvaluateEscalationPriority(ticket)` (`BR-4`: bumps one level up to `Critical`; throws `DomainRuleException` (`409 Conflict`) for `Resolved`/`Closed` tickets).
     - `SelectAssignee(categoryId, candidates, excludedAgentId)` (`BR-2` & `BR-6`: filters active agents in the ticket's category below `MaxConcurrentTickets`, excludes the current assignee on re-assignment when requested, and orders by lowest workload then `Id` ascending).
4. **Aggregate Root Encapsulation (`Ticket.cs` & `TicketEscalation.cs`)**:
   - Added `Ticket.Escalate(...)` on the `Ticket` aggregate root, which enforces domain invariants (`Resolved`/`Closed` rejection, priority bump, status transition from `Resolved` if ever applicable, deadline tightening via `SlaEvaluator`, optional assignee update, and appending an immutable `TicketEscalation` audit record).

### 2.2 Persistence & Indexing
- Added `TicketEscalationConfiguration` and generated EF Core migration `20261022114559_AddTicketEscalations`.
- Configured foreign key from `TicketEscalations.TicketId` to `Tickets.Id` with cascade delete, and added composite index `IX_TicketEscalations_TicketId_EscalatedAtUtc` on `(TicketId, EscalatedAtUtc)` so `GET /api/tickets/{id}/escalations` and escalation count checks on `TicketId` execute as index seeks ordered by timestamp without table scans.

### 2.3 Frontend Implementation
- Unified the SLA visual indicator (`SlaIndicator` in `SlaCell.tsx`) using symbol + text + semantic tone (`✓ On track`, `! At risk`, `✕ Breached`, `● SLA met`) so status is never conveyed by colour alone (`UI-1`).
- Added the `slaStatus` dropdown filter to `TicketFilters.tsx` (`UI-2`).
- Built `useTicketEscalations.ts`, `EscalationPanel.tsx`, and `EscalationHistory.tsx` integrated into `TicketDetailPage.tsx` (`UI-3`, `UI-4`), plus the post-creation triage banner on `CreateTicketPage.tsx` (`UI-5`).

---

## Task 3: Login & Authentication

### 3.1 Backend Credentials, Hashing & JWT Architecture
1. **Password Storage (`AU-1`, `AU-2`)**:
   - Added `PasswordHash` (`nvarchar(256)`) to the `Agent` aggregate and generated EF Core migration `20261022183720_AddAgentPasswordHash`.
   - Implemented `Pbkdf2PasswordHasher` (`SupportDesk.Infrastructure/Services/Pbkdf2PasswordHasher.cs`) using `Rfc2898DeriveBytes.Pbkdf2` (HMAC-SHA256, 100,000 iterations, 128-bit random salt per password, 256-bit subkey) and constant-time comparison via `CryptographicOperations.FixedTimeEquals`.
   - Updated `SupportDbSeeder` to seed all demo agents with the development password `Dev_Support_Pass1!` (and backfill any existing local database rows whose `PasswordHash` was empty).
2. **Stateless HMAC-SHA256 JWT (`AU-3`, `AU-4`, `AU-6`, `AU-7`)**:
   - Implemented `JwtTokenService` (`SupportDesk.Infrastructure/Services/JwtTokenService.cs`) issuing and validating standard RFC 7519 HS256 JWTs containing `sub` (agent ID), `email`, `name`, `iat`, and `exp`, using `IDateTimeProvider` (`clock.UtcNow`) for deterministic expiry testing.
   - Configured central authorization in `Program.cs` via `AddAuthorizationBuilder().SetFallbackPolicy(new AuthorizationPolicyBuilder().RequireAuthenticatedUser().Build())`. Every controller endpoint requires authentication automatically unless explicitly decorated with `[AllowAnonymous]` (`POST /api/auth/login` and `GET /health`).
3. **Actor Identity from Token (`AU-8`)**:
   - `TicketsController.Escalate` extracts `ClaimTypes.Name` from `HttpContext.User` and passes it as `authenticatedActorName` to `EscalateTicketCommandHandler`, overriding any client-supplied `escalatedBy` field in the request body.

### 3.2 Frontend Session & Protected Routing (`AU-9` to `AU-13`)
- Stored the session (`token`, `expiresAtUtc`, `user`) in `sessionStorage` inside `apps/web/src/api/client.ts` so every `request<T>` call automatically attaches `Authorization: Bearer <token>`.
- When any API call returns `401 Unauthorized`, `client.ts` immediately clears `sessionStorage` and notifies `AuthProvider`, redirecting the user to `/login`.
- Wrapped application routes with `RequireAuth`, preserving the requested location in router state and returning the agent there after signing in.

---

## Task 4: Dockerise the API

1. **Multi-Stage `apps/api/Dockerfile` & `.dockerignore` (`DK-1` to `DK-4`)**:
   - Stage 1 (`mcr.microsoft.com/dotnet/sdk:10.0`) copies `global.json`, `Directory.Build.props`, `Directory.Packages.props`, and the four `.csproj` files first and runs `dotnet restore`, ensuring code edits reuse the cached NuGet layer.
   - Stage 2 (`mcr.microsoft.com/dotnet/aspnet:10.0`) copies the published output, exposes HTTP port `8080`, and runs as `$APP_UID` (non-root user `app`).
   - `.dockerignore` excludes `bin/`, `obj/`, `node_modules/`, `.git/`, `.env*`, `apps/web/`, and `appsettings.Development.json` so no local configuration or secret is ever baked into the image.
2. **Compose Orchestration & Health Check (`DK-5` to `DK-8`)**:
   - Added `api` service to `docker-compose.yml` mapped to `localhost:5080:8080` with `depends_on: sqlserver: condition: service_healthy`.
   - Added central `[AllowAnonymous]` `GET /health` endpoint in `Program.cs` verifying database connectivity via `db.Database.CanConnectAsync()`.

