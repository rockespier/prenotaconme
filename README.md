# PrenotaConMe

SaaS multi-tenant de reservas online (barberías, peluquerías, centros estéticos…).

- Idea: [`idea_iniziale.md`](idea_iniziale.md) · Stack: [`docs/STACK.md`](docs/STACK.md) · Backlog: [`docs/BACKLOG.md`](docs/BACKLOG.md) · Despliegue: [`deploy/README.md`](deploy/README.md)

## Estructura

```
src/backend/PrenotaConMe.Api             Host Minimal API (.NET 10)
src/backend/PrenotaConMe.Infrastructure  EF Core + PostgreSQL, multi-tenant, migraciones
src/backend/PrenotaConMe.Shared          Entidades base, tenancy, contrato IModule
src/frontend/prenotaconme-web            Angular 21 PWA (Material, Transloco)
tests/                                   xUnit (unit + integración con Testcontainers)
deploy/                                  Despliegue en Windows Server/IIS, compose de dev
```

## Requisitos locales

- .NET SDK 10, Node 22 (≥22.12)
- PostgreSQL 18 (nativo o `docker compose -f deploy/docker-compose.dev.yml up -d`)
- Opcional: Seq (http://localhost:5341) para logs/trazas

### PostgreSQL nativo en Windows

```powershell
winget install PostgreSQL.PostgreSQL.18
# Con el usuario postgres (contraseña elegida en la instalación):
psql -U postgres -c "CREATE ROLE prenota LOGIN PASSWORD 'prenota' CREATEDB;"
psql -U postgres -c "CREATE DATABASE prenotaconme OWNER prenota;"
psql -U postgres -d prenotaconme -c "CREATE EXTENSION IF NOT EXISTS btree_gist;"
```

## Ejecutar

```bash
# Backend (aplica migraciones al arrancar en Development) → http://localhost:5080/scalar
dotnet run --project src/backend/PrenotaConMe.Api

# Frontend → http://localhost:4200
cd src/frontend/prenotaconme-web && npm install && npm start
```

## Tests

```bash
dotnet test tests/PrenotaConMe.UnitTests
dotnet test tests/PrenotaConMe.IntegrationTests   # requiere Docker (Testcontainers)
# Sin Docker, contra el Postgres local (BD temporal; el rol necesita CREATEDB):
$env:PRENOTA_TEST_PG = "Host=localhost;Username=prenota;Password=prenota"; dotnet test tests/PrenotaConMe.IntegrationTests
cd src/frontend/prenotaconme-web && npm test
```

## Cliente API tipado

El build del backend genera `src/frontend/prenotaconme-web/openapi/prenotaconme-api.json`. Tras cambiar endpoints:

```bash
dotnet build src/backend/PrenotaConMe.Api
cd src/frontend/prenotaconme-web && npm run api:generate
```

## Migraciones

```bash
dotnet ef migrations add <Nombre> -p src/backend/PrenotaConMe.Infrastructure -s src/backend/PrenotaConMe.Api -o Persistence/Migrations
```
