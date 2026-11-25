# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

PrenotaConMe: multi-tenant SaaS for online appointment booking (barbers, hairdressers, beauty salons), a Reservio-like product. The requirements are in `idea_iniziale.md` (Italian). Stack decisions are in `docs/STACK.md`, and the phased backlog with acceptance criteria is in `docs/BACKLOG.md` (story IDs like `F1-09`). Docs are in Spanish; code, identifiers and comments are in English.

## Commands

Run from the repo root unless noted. The SDK is pinned by `global.json` (.NET 10).

```bash
dotnet build PrenotaConMe.slnx
dotnet test tests/PrenotaConMe.UnitTests
dotnet test tests/PrenotaConMe.IntegrationTests          # Docker (Testcontainers, postgres:18-alpine), used in CI
# Without Docker: throwaway DB on the local Postgres (role needs CREATEDB). Bash syntax; in PowerShell set $env:PRENOTA_TEST_PG first
PRENOTA_TEST_PG="Host=localhost;Username=prenota;Password=prenota" dotnet test tests/PrenotaConMe.IntegrationTests
dotnet test tests/PrenotaConMe.UnitTests --filter "FullyQualifiedName~TenantIsolationTests.Insert_stamps_tenant_and_created_at"
dotnet run --project src/backend/PrenotaConMe.Api        # http://localhost:5080/scalar, migrates DB on start in Development

# EF migrations (local tool in dotnet-tools.json). Migrations live in Infrastructure; the Api is the startup project
# so EF sees every module's entity configurations. If it fails with a missing wwwroot path, delete Api bin/obj (stale static-assets manifest).
dotnet ef migrations add <Name> -p src/backend/PrenotaConMe.Infrastructure -s src/backend/PrenotaConMe.Api -o Persistence/Migrations
```

Frontend (run in `src/frontend/prenotaconme-web`):
```bash
npm start                  # ng serve, http://localhost:4200
npm test                   # Vitest, single run
npx ng test --include src/app/core/i18n/i18n.spec.ts --watch=false   # single spec
npx ng build
npm run api:generate       # regenerate typed API client from openapi/prenotaconme-api.json
```

Secrets for local dev (SMTP password, `Notifications:StaffEmail`, `Platform:AdminEmails:0`) live in user-secrets, never in `appsettings.Development.json` (the repo is public): `dotnet user-secrets set Smtp:Password <value> --project src/backend/PrenotaConMe.Api`.

Web Push (F2-02) needs a VAPID key pair in `Push:PublicKey` / `Push:PrivateKey` (user-secrets locally; without them push is off). Generate one with:
```bash
node -e "const c=require('crypto');const k=c.generateKeyPairSync('ec',{namedCurve:'prime256v1'});const j=k.privateKey.export({format:'jwk'});console.log('Push:PublicKey  '+Buffer.concat([Buffer.from([4]),Buffer.from(j.x,'base64url'),Buffer.from(j.y,'base64url')]).toString('base64url'));console.log('Push:PrivateKey '+j.d)"
```
The Angular service worker only runs in production builds, so push is tested with `npx ng build` + the API serving `wwwroot`, not `ng serve`.

Local DB is native PostgreSQL 18 (no Docker on the dev machine): db `prenotaconme`, user/password `prenota`/`prenota`, Seq optional at `localhost:5341`. `deploy/docker-compose.dev.yml` is the Docker alternative.

## Architecture

**Modular monolith.** There is one deployable (`PrenotaConMe.Api`). Business modules (Tenancy, Users, Catalog, Staff, Locations, Scheduling, Notifications, Chatbot, Calendar, Finance) are planned as separate projects. Each module implements `IModule` (`src/backend/PrenotaConMe.Shared/Modules/IModule.cs`) and must be added explicitly to `ModuleCatalog.All` (`src/backend/PrenotaConMe.Api/Hosting/ModuleCatalog.cs`). At startup, `ModuleExtensions` does two things for each module:
- registers its services;
- maps its endpoints under `/api/{module.Name}`, and registers the module assembly as a `ModelAssembly` so its `IEntityTypeConfiguration`s are applied to the single `AppDbContext`.

**How modules may depend on each other:**
- **Lower-level domain types:** a module may reference another module's domain types to get real foreign keys, but only in one acyclic direction. Current chain: Scheduling → Staff → Catalog → Locations.
- **Everything else:** goes through Shared contracts.
  - `ICurrentUser`.
  - `IMembershipWriter`: `EnsureAsync`/`RemoveAsync` are idempotent and only stage changes.
  - `ITenantDirectory`.
  - `ITenantInitializer`: starter data when a business registers. It is ordered: Locations (10) seeds the default location, then Catalog (20) seeds the sample services, reading the staged location through `DbSet.Local`.
  - `IUserSignInHook`: runs from `GET /api/users/me`. Staff uses it to link invitations by verified e-mail.
  - `IUserDirectory`: read-only contact details (name, e-mail, phone) of local users.
  - `PlatformAdmins`: platform super-admins (role P) from `Platform:AdminEmails`.

**Shared scheduling helpers:** `TimeZones.IsValidIana` and `WeeklyIntervals` (validate order, granularity and overlap; coverage within opening hours). All weekly hours are local to the location. Location opening hours use 30-minute steps; staff working hours use 1-hour blocks and must fall inside the opening hours.

**Availability and booking (Scheduling module).**
- **Engine:** `AvailabilityCalculator` is a pure function with no I/O and no clock, so it is unit-tested directly. A slot starts on the 30-minute grid in the location's local time. The whole service must fit inside both the staff member's working hours and the location's opening hours. Days off, overlapping appointments and past times are excluded, and so are times skipped by DST. Slots are returned as UTC instants.
- **Loading:** `AvailabilityService` gathers the calculator's inputs for one location and service: active staff assigned to both, their hours at that location, time off, and busy appointments.
- **Booking:** `POST /api/scheduling/appointments` recomputes that local day's slots and only accepts an offered start. With no staff chosen ("any"), it picks the free person with the fewest appointments that day. It snapshots the service name and price, and grants the Client role.
- **Client changes (F1-12):** `/api/scheduling/appointments/mine`, `/{id}/cancel` and `/{id}/reschedule` act only on the caller's own appointments (others → 404). Both changes need `StartsAt - Tenant.CancellationNoticeHours >= now` (409 `appointment.tooLate`); the Admin sets the notice with `PUT /api/tenancy/tenants/current/settings`. Rescheduling recomputes slots with `AvailabilityQuery.IgnoreAppointmentId` so the appointment's own time counts as free; `GET /availability?rescheduling={id}` does the same only for the owner. Frontend page: `/:slug/prenotazioni`.
- **Backoffice calendar (F1-13):** `GET /api/scheduling/calendar?from&to` (Admin/Staff; Staff only get staff records linked to their user). Admin endpoints under `/calendar/appointments` create guest appointments (`ClientUserId` null + `ClientName`/`ClientPhone`, check `ck_appointments_has_client`), move (staff/start) and cancel without notice; working hours are not enforced there, only the overlap constraint. Client contact data comes from `IUserDirectory` (Shared, implemented by Users). Frontend: `/admin/calendario` with FullCalendar v6 vanilla core (`@fullcalendar/core|timegrid|interaction`, no Angular wrapper), in the browser's time zone.
- **Clients (F1-14):** there is no client table; `GET /api/scheduling/clients` aggregates appointments per client key: the user id for registered clients, `guest:` + phone (or name) for guests entered in the calendar. `GET /clients/detail?key=` returns the history; the calendar DTO carries `ClientKey`. Frontend: `/admin/clienti` and `/admin/clienti/scheda?key=`.
- **Overlap guarantee:** the `ex_appointments_staff_no_overlap` constraint (`EXCLUDE USING gist` on staff + `tstzrange [)`, ignoring cancelled appointments) is created with raw SQL in the `AddAppointments` migration. It turns concurrent double bookings into 409 `slot.taken`.
- **Deleting referenced data:** appointments reference location, service and staff with `ON DELETE RESTRICT`. The delete endpoints map that to 409 `*.inUse` ("deactivate instead").
- **Constraint errors:** use `DbErrors.IsUniqueViolation/IsForeignKeyViolation/IsExclusionViolation`, which cover both SaveChanges and ExecuteDelete. RESTRICT raises SQLSTATE 23001, not 23503.

**Background jobs (Hangfire, F2-01).** `AddJobs` (Infrastructure/Jobs) uses Hangfire with Postgres storage in the `hangfire` schema of the app DB (created at startup, not an EF migration); `EnableLongPolling` (LISTEN/NOTIFY) picks up new jobs at once. Inject `IBackgroundJobClient`: it is scoped (`TenantBackgroundJobClient`) and stamps the current tenant as job parameter `TenantId`; `TenantJobActivator` opens one DI scope per job and restores it on `TenantContext`, so job code reads tenant data like a request does. Job methods must be public, take serializable args and `CancellationToken.None` as a placeholder. `Jobs:Server=false` disables processing in a process. Dashboard: `/hangfire`, only for platform super-admins (`Platform:AdminEmails`, verified e-mail, `PlatformAdmins` in Shared, `MeDto.IsPlatformAdmin`); the SPA calls `POST /api/jobs/dashboard-session` (bearer) which sets a 1 h cookie scoped to `/hangfire`, then opens the page. Hangfire.Core pulls a vulnerable Newtonsoft.Json, pinned to 13.x in Infrastructure.

**Notifications (e-mail F1-11, Web Push F2-02).** After an appointment is booked, moved or cancelled (client or backoffice endpoints), Scheduling's `AppointmentEvents.PublishAsync` builds an `AppointmentNotice` (Shared; carries client/staff user ids and the business slug) and calls `IAppointmentNotifier.Notify(change, notice, actorUserId)`. Bookings enqueue one `AppointmentEmailJob` per recipient (client, staff; retried 5 times, so one failure never re-sends the other) with an `.ics` attachment through MailKit, in the business's default language. Every change also pushes to the client's and the staff member's devices, except to whoever made the change; a move to another staff member tells the previous one it was cancelled. Push: `WebPushSubscription` (platform-wide, unique endpoint, FK to users) managed by `/api/notifications/push/config|subscriptions`; `PushJob` fans out one job per device and deletes subscriptions the push service reports as gone (404/410); `IPushSender` = `VapidPushSender` (Lib.Net.Http.WebPush). The payload uses the Angular service worker format (`notification` + `data.onActionClick`), so `ngsw` shows it and opens `/{slug}/prenotazioni` or `/admin/calendario`. Frontend: `PushService` (SwPush) + `<app-push-toggle>` on "Mis citas" and the backoffice dashboard; sign-out removes the device from the user. Without `Smtp:Host`, `LoggingEmailSender` only logs. Integration tests capture pushes with `ApiFactory.Pushes`.

**Announcements (F2-03).** `Announcement` (tenant-owned, Notifications module) with business-local `StartsOn`/`EndsOn` (inclusive; "today" uses `TenantSummary.TimeZone`). Admin CRUD under `/api/notifications/announcements`; `GET /announcements/current` (anonymous, needs X-Tenant) feeds `<app-announcement-banner>` on `/:slug` and `/:slug/prenotazioni`. Sending (on create with `Notify`, or `POST /{id}/send`) happens once: the row is marked sent and saved before `AnnouncementJob` is queued; the job sends to every user with the Client role in the tenant (one e-mail job per address, one push per user). E-mails link to the booking page when `Notifications:PublicUrl` is set. Frontend: `/admin/avvisi`. `Notifications:StaffEmail` (dev) redirects every staff e-mail to one address. Integration tests replace `IEmailSender` with `CapturingEmailSender` (`ApiFactory.Emails`).

**Multi-tenancy (row-level, fail-closed).** These rules hold for every tenant-owned entity:
- Tenant data derives from `TenantEntity` / `ITenantOwned`.
- `AppDbContext` adds a named query filter (`AppDbContext.TenantFilter`) and an index on `TenantId` to every root entity of that kind.
- With no tenant in the scoped `ITenantContext`, queries return nothing. Platform-level code must call `IgnoreQueryFilters([AppDbContext.TenantFilter])`.
- `AuditingInterceptor` stamps `TenantId` on insert, throws `MissingTenantException` when there is no tenant, and rejects cross-tenant inserts and `TenantId` changes. It also sets `CreatedAt`/`UpdatedAt` via `TimeProvider`.
- Entity ids are UUIDv7 (`Guid.CreateVersion7()`), assigned when the object is constructed. `AppDbContext` marks every `Id` as `ValueGeneratedNever`. Without that, EF assumes a new child added to a tracked parent's collection already exists, and issues an UPDATE that hits 0 rows.
- JSON is strict: enums are serialized as strings, and numbers must be JSON numbers (`NumberHandling.Strict`), which keeps the OpenAPI/TS types as plain `number`.
- **Tenant resolution:** the client sends the business slug in the `X-Tenant` header. `TenantResolutionMiddleware` (Tenancy module) resolves it through `ITenantDirectory`, with the slug lookup cached for 5 minutes, and calls `ITenantContext.SetTenant`. An unknown or inactive slug gets 404. An endpoint marked `.RequireTenant()` gets 400 when the header is missing. Pipeline order: `UseAuthentication` → `UseTenantResolution` → `UseAuthorization`.

**Auth (Firebase).**
- **Token validation:** the API validates Firebase ID tokens with JwtBearer (`Hosting/AuthExtensions.cs`, `Firebase:ProjectId`). Claims keep their raw names (`sub` = Firebase uid, `email`, `name`, `picture`).
- **Local users:** `ICurrentUser.GetUserIdAsync()` (Users module) provisions the local `UserAccount` on first use.
- **Role checks:** `.RequireTenantRole(TenantRole.Admin, ...)` (Shared) implies `.RequireTenant()` plus a `TenantRoleRequirement`, which `TenantRoleHandler` checks against `UserMembership` in the current tenant.
- **Staff invitations:** an Admin creates a staff member with an e-mail. When someone signs in with that verified Google e-mail, `StaffInvitationLinker` links the user and grants them the Staff role. An Admin who adds their own e-mail is linked immediately.
- **Integration tests:** `TestAuthHandler` replaces JWT validation. The header `X-Test-User: uid|email|name` authenticates the request; `ApiFactory.CreateUserClient(uid, tenantSlug)` builds such a client.
- **Frontend:** `AuthService` wraps the Firebase JS SDK directly, without @angular/fire. The `API_CLIENT` middleware adds `Authorization: Bearer <idToken>` and `X-Tenant` (taken from `TenantSelection`, stored in localStorage) to every call. `SessionService` loads `/api/users/me`. Guards: `authGuard`, `backofficeGuard` (Admin/Staff of at least one business, otherwise `/registra-attivita`).

**Persistence conventions:**
- Npgsql with snake_case naming (EFCore.NamingConventions).
- The migrations history table is `__ef_migrations_history`.
- The `btree_gist` extension is enabled; it backs the appointments `EXCLUDE` constraint (see Availability and booking).

**API ↔ frontend contract:**
- Building the Api project writes the OpenAPI document to `src/frontend/prenotaconme-web/openapi/prenotaconme-api.json`, using `Microsoft.Extensions.ApiDescription.Server`.
- `npm run api:generate` turns that document into `src/app/core/api/schema.d.ts`, which `API_CLIENT` (openapi-fetch) consumes.
- CI fails if either generated file is out of date, so after changing endpoints run `dotnet build` and then `npm run api:generate`, and commit both.
- Build-time doc generation runs `Program.cs` only up to `builder.Build()`. Startup code before that point must not need a live database.

**Frontend (Angular 21):**
- Standalone components with signals and zoneless change detection. Files use the new naming (`home.ts`, no `.component` suffix).
- All routes are lazy (`app.routes.ts`): the public layout serves `/`, `/login`, `/registra-attivita` and `/:slug` (per-business booking page at the site root, declared last), and the backoffice layout serves `/admin`. Every fixed top-level route must also be listed in `TenantRules.ReservedSlugs` (backend), so that no business can register its name as a slug.
- Route params bind to component inputs (`withComponentInputBinding`).
- i18n uses Transloco at runtime. JSON files live in `public/i18n/{it,es,en}.json`, the default language is `it`, and every UI string goes through the `transloco` pipe.
- In production the Angular build is copied into the API's `wwwroot` and served by the API itself (`Hosting/SpaHostingExtensions.cs`): same origin (`apiUrl: ''`), and a fallback to `index.html` for every path except `/api*` and `/health*`. `index.html`/`ngsw*` are served `no-cache`.

**Observability:** Serilog is configured from `appsettings*.json`. OpenTelemetry traces and metrics are exported over OTLP only when `OpenTelemetry:OtlpEndpoint` is set (Seq). Health endpoints are `/health` (DB readiness) and `/health/live`.

## Gotchas

- **NuGet versions:** the latest packages on nuget.org are 11.x (.NET 11). Pin Microsoft/EF/Npgsql/Serilog packages to 10.x when adding them.
- **Node and Angular version:** the machine has Node 22.14, which is why the project uses Angular 21 (Angular 22 needs Node ≥22.22.3).
- **npm bug:** npm 11.2 crashes with `edgesOut`. `.npmrc` sets `legacy-peer-deps=true` to work around it.
- **Analyzers:** `AnalysisLevel` is `latest-recommended` (`Directory.Build.props`). CA1707 is suppressed in `tests/` and CA1716 in `.editorconfig`. Keep the build warning-free.
- **Production layout:** Windows Server 2022 with no Docker, a single IIS site (ASP.NET Core Module in-process), and PostgreSQL + Seq native on the same host. `deploy.yml` publishes win-x64 with Angular in `wwwroot`, then `deploy/windows/deploy.ps1` unpacks it to `C:\prenotaconme\releases\<version>` and mirrors it into the IIS site path `D:\IISSites\prenotaconme.rtres.net` (app pool `prenotaconme.rtres.net`). During the copy, `app_offline.htm` keeps the app stopped. Secrets live in `C:\prenotaconme\shared\appsettings.Production.json` on the server. See `deploy/README.md`.

## Key product decisions (affect implementation)

- **Auth:** Firebase Auth (Google SSO) for every user; there is no ASP.NET Identity. The API validates Firebase ID tokens with JwtBearer. Roles per tenant (Admin/Staff/Client) live in a local `UserMembership` table.
- **Client accounts:** clients must have an account to book.
- **Cancellation:** cancellation and rescheduling need a minimum notice that each business configures (default 2 h).
- **Staff assignment:** "any staff" assigns the free staff member with the fewest appointments that day.
- **Chatbot:** the chatbot is channel-agnostic (`IMessagingChannel`). Telegram is used for the first demo, then WhatsApp Cloud API with one number per business. It uses OpenAI GPT (function calling) and Whisper.
