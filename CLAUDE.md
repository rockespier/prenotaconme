# CLAUDE.md

This file provides guidance to Claude Code (claude.ai/code) when working with code in this repository.

## Project

PrenotaConMe: multi-tenant SaaS for online appointment booking (barbers, hairdressers, beauty salons), a Reservio-like product. The requirements are in `idea_iniziale.md` (Italian). Stack decisions are in `docs/STACK.md`, and the phased backlog with acceptance criteria is in `docs/BACKLOG.md` (story IDs like `F1-09`). Docs are in Spanish; code, identifiers and comments are in English.

## Commands

Run from the repo root unless noted. The SDK is pinned by `global.json` (.NET 10).

```bash
dotnet build PrenotaConMe.slnx
dotnet test tests/PrenotaConMe.UnitTests
dotnet test tests/PrenotaConMe.IntegrationTests          # needs Docker (Testcontainers, postgres:18-alpine)
dotnet test tests/PrenotaConMe.UnitTests --filter "FullyQualifiedName~TenantIsolationTests.Insert_stamps_tenant_and_created_at"
dotnet run --project src/backend/PrenotaConMe.Api        # http://localhost:5080/scalar, migrates DB on start in Development

# EF migrations (local tool in dotnet-tools.json; DbContext + design-time factory live in Infrastructure)
dotnet ef migrations add <Name> -p src/backend/PrenotaConMe.Infrastructure -s src/backend/PrenotaConMe.Infrastructure -o Persistence/Migrations
```

Frontend (run in `src/frontend/prenotaconme-web`):
```bash
npm start                  # ng serve, http://localhost:4200
npm test                   # Vitest, single run
npx ng test --include src/app/core/i18n/i18n.spec.ts --watch=false   # single spec
npx ng build
npm run api:generate       # regenerate typed API client from openapi/prenotaconme-api.json
```

Local DB is native PostgreSQL 18 (no Docker on the dev machine): db `prenotaconme`, user/password `prenota`/`prenota`, Seq optional at `localhost:5341`. `deploy/docker-compose.dev.yml` is the Docker alternative.

## Architecture

**Modular monolith.** There is one deployable (`PrenotaConMe.Api`). Business modules (Tenancy, Users, Catalog, Staff, Locations, Scheduling, Notifications, Chatbot, Calendar, Finance) are planned as separate projects. Each module implements `IModule` (`src/backend/PrenotaConMe.Shared/Modules/IModule.cs`) and must be added explicitly to `ModuleCatalog.All` (`src/backend/PrenotaConMe.Api/Hosting/ModuleCatalog.cs`). At startup, `ModuleExtensions` does two things for each module:
- registers its services;
- maps its endpoints under `/api/{module.Name}`, and registers the module assembly as a `ModelAssembly` so its `IEntityTypeConfiguration`s are applied to the single `AppDbContext`.

**Multi-tenancy (row-level, fail-closed).** These rules hold for every tenant-owned entity:
- Tenant data derives from `TenantEntity` / `ITenantOwned`.
- `AppDbContext` adds a named query filter (`AppDbContext.TenantFilter`) and an index on `TenantId` to every root entity of that kind.
- With no tenant in the scoped `ITenantContext`, queries return nothing. Platform-level code must call `IgnoreQueryFilters([AppDbContext.TenantFilter])`.
- `AuditingInterceptor` stamps `TenantId` on insert, throws `MissingTenantException` when there is no tenant, and rejects cross-tenant inserts and `TenantId` changes. It also sets `CreatedAt`/`UpdatedAt` via `TimeProvider`.
- Entity ids are UUIDv7 (`Guid.CreateVersion7()`).
- Tenant resolution middleware (slug/JWT claim) is not written yet (F1).

**Persistence conventions:**
- Npgsql with snake_case naming (EFCore.NamingConventions).
- The migrations history table is `__ef_migrations_history`.
- The `btree_gist` extension is enabled, intended for `EXCLUDE USING gist` constraints that prevent overlapping appointments per staff member.

**API ↔ frontend contract:**
- Building the Api project writes the OpenAPI document to `src/frontend/prenotaconme-web/openapi/prenotaconme-api.json`, using `Microsoft.Extensions.ApiDescription.Server`.
- `npm run api:generate` turns that document into `src/app/core/api/schema.d.ts`, which `API_CLIENT` (openapi-fetch) consumes.
- CI fails if either generated file is out of date, so after changing endpoints run `dotnet build` and then `npm run api:generate`, and commit both.
- Build-time doc generation runs `Program.cs` only up to `builder.Build()`. Startup code before that point must not need a live database.

**Frontend (Angular 21):**
- Standalone components with signals and zoneless change detection. Files use the new naming (`home.ts`, no `.component` suffix).
- All routes are lazy (`app.routes.ts`): the public layout serves `/` and `/b/:slug` (per-business booking page), the backoffice layout serves `/admin`.
- Route params bind to component inputs (`withComponentInputBinding`).
- i18n uses Transloco at runtime. JSON files live in `public/i18n/{it,es,en}.json`, the default language is `it`, and every UI string goes through the `transloco` pipe.
- In production the frontend is served same-origin (`apiUrl: ''`) by Caddy, which proxies `/api` and `/health`.

**Observability:** Serilog is configured from `appsettings*.json`. OpenTelemetry traces and metrics are exported over OTLP only when `OpenTelemetry:OtlpEndpoint` is set (Seq). Health endpoints are `/health` (DB readiness) and `/health/live`.

## Gotchas

- **NuGet versions:** the latest packages on nuget.org are 11.x (.NET 11). Pin Microsoft/EF/Npgsql/Serilog packages to 10.x when adding them.
- **Node and Angular version:** the machine has Node 22.14, which is why the project uses Angular 21 (Angular 22 needs Node ≥22.22.3).
- **npm bug:** npm 11.2 crashes with `edgesOut`. `.npmrc` sets `legacy-peer-deps=true` to work around it.
- **Analyzers:** `AnalysisLevel` is `latest-recommended` (`Directory.Build.props`). CA1707 is suppressed in `tests/` and CA1716 in `.editorconfig`. Keep the build warning-free.
- **Production layout:** PostgreSQL runs natively on the VPS. The API container reaches it via `host.docker.internal` (host-gateway). See `deploy/README.md`.

## Key product decisions (affect implementation)

- **Auth:** Firebase Auth (Google SSO) for every user; there is no ASP.NET Identity. The API validates Firebase ID tokens with JwtBearer. Roles per tenant (Admin/Staff/Client) live in a local `UserMembership` table.
- **Client accounts:** clients must have an account to book.
- **Cancellation:** cancellation and rescheduling need a minimum notice that each business configures (default 2 h).
- **Staff assignment:** "any staff" assigns the free staff member with the fewest appointments that day.
- **Chatbot:** the chatbot is channel-agnostic (`IMessagingChannel`). Telegram is used for the first demo, then WhatsApp Cloud API with one number per business. It uses OpenAI GPT (function calling) and Whisper.
