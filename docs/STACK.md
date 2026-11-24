# PrenotaConMe — Stack tecnológico recomendado

## Context
Clon de Reservio: SaaS multi-tenant de reservas para barberías, peluquerías, centros estéticos, etc. Base: `idea_iniziale.md`. Decisiones del usuario: .NET 10 Minimal API + Angular PWA (cerrado), multi-tenant, VPS Windows Server 2022 + IIS, PostgreSQL, WhatsApp Meta Cloud API, OpenAI (GPT + Whisper), Firebase Auth (Google SSO), Angular Material, <50 negocios el primer año, un solo dev full-stack. MVP incluye: i18n, Web Push, página pública por negocio, sincronización bidireccional con GCal.

## Arquitectura
**Monolito modular** (un solo deployable, módulos por dominio con vertical slices). Sin microservicios.

Módulos: `Tenancy`, `Users` (perfiles y roles), `Catalog` (servicios), `Staff`, `Locations`, `Scheduling` (disponibilidad + citas), `Notifications` (WhatsApp/email/push/anuncios), `Chatbot`, `Calendar` (GCal sync), `Finance` (gastos + informes).

## Backend (.NET 10)
| Área | Elección |
|---|---|
| API | Minimal API + endpoint groups por módulo, OpenAPI nativo + Scalar |
| ORM | EF Core 10 + Npgsql, migraciones |
| Multi-tenant | Columna `TenantId` + global query filters de EF; resolución por slug de URL/claim JWT |
| Anti-doble reserva | Constraint `EXCLUDE USING gist` sobre `tstzrange` por staff (extensión `btree_gist`) |
| Fechas/zonas | NodaTime (zona IANA por sede) |
| Auth | **Firebase Authentication** (Google SSO; email/contraseña opcional para staff). Angular: `@angular/fire`. API: `JwtBearer` validando el ID token de Firebase (issuer `securetoken.google.com/<project>`). Sin ASP.NET Identity |
| Autorización | Tabla propia `UserMembership` (FirebaseUid, TenantId, Rol Admin/Staff/Client) en Postgres; policies por endpoint. Firebase Admin SDK para invitaciones/custom claims si hace falta |
| Validación | FluentValidation |
| Jobs | Hangfire con storage PostgreSQL (recordatorios, sync GCal, procesamiento chatbot) |
| Tiempo real | SignalR (calendario staff/admin actualizado en vivo) |
| PDF / Excel | QuestPDF (licencia Community) / ClosedXML |
| Google Calendar | `Google.Apis.Calendar.v3`, OAuth por staff, push notifications (watch channels) + sync tokens |
| Web Push | `Lib.Net.Http.WebPush` (VAPID) |
| Email | MailKit + SMTP transaccional (Brevo/Postmark) |
| OpenAI | SDK oficial `OpenAI` (.NET): GPT con function calling + `gpt-4o-mini-transcribe`/Whisper |
| Telegram (demo) | `Telegram.Bot`; long polling en dev, webhook con `secret_token` en prod |
| WhatsApp | HttpClient tipado contra Graph API (no hay SDK oficial) + webhook verificado por firma |
| Logs/obs. | Serilog + OpenTelemetry → Seq |
| Tests | xUnit, Testcontainers (Postgres real), Playwright para E2E |

## Frontend (Angular 21 — Angular 22 exige Node ≥22.22.3; migrar al actualizar Node)
- Standalone components + Signals, `@angular/pwa` (service worker, instalable, push).
- Angular Material + CDK.
- i18n: **Transloco** (cambio de idioma en runtime; IT/ES/EN).
- Calendario: **FullCalendar** (vistas día/semana por staff, drag & drop).
- Gráficos de informes: ngx-echarts.
- Cliente API generado desde OpenAPI (NSwag u `openapi-typescript`).
- Dos zonas en una sola app: **pública** (`/b/{slug}` reserva sin fricción) y **backoffice** (admin/staff), con lazy loading.

## Chatbot (multicanal)
Núcleo independiente del canal: `IMessagingChannel` (recibir/enviar texto, audio, botones) con implementaciones **Telegram** (demo con el primer cliente) y **WhatsApp** (producción). Mismo motor GPT + tools + estado de conversación.

**Telegram (demo):** un bot creado con BotFather; el cliente se vincula a su cuenta con deep link `t.me/<bot>?start=<token>` desde la web o con el botón "Compartir contacto"; sin plantillas ni ventana de 24 h; audios OGG → transcripción.

**WhatsApp:**
Webhook Meta → endpoint API → encola job Hangfire → si es audio: descarga media + transcripción → GPT con tools (`list_services`, `get_availability`, `create_booking`, `reschedule`, `cancel`) que llaman a los mismos servicios de `Scheduling` → respuesta. Estado de conversación en Postgres.
Recordatorios: plantillas aprobadas por Meta (utility), programadas con Hangfire (p.ej. 24h y 2h antes).
**Número propio por negocio** (decidido):
- Tabla `WhatsAppAccount` por tenant: `WabaId`, `PhoneNumberId`, número, access token cifrado (Data Protection API), estado de plantillas.
- Webhook único; el tenant se resuelve por `metadata.phone_number_id` del payload.
- Fase de pruebas: un solo tenant con **el número del dev**, configurado a mano (Meta App + System User token). Se puede empezar con el número de test que da Meta y pasar luego al propio.
- Producción: alta de cada negocio vía **Embedded Signup** (requiere ser Tech Provider de Meta y verificar el Business).
- Ojo: si el número está activo en la app de WhatsApp, usar la modalidad **coexistence** (WhatsApp Business app + Cloud API) o liberarlo de la app antes de registrarlo en Cloud API.

## Infraestructura
- VPS **Windows Server 2022** (sin Docker). Guía: `deploy/README.md`.
- **IIS** + ASP.NET Core Hosting Bundle (in-process): un único sitio; la API sirve también la PWA de Angular desde `wwwroot` (mismo origen, sin ARR ni proxy).
- TLS: win-acme (Let's Encrypt) con renovación automática.
- **PostgreSQL 18 nativo** en el mismo servidor, `listen_addresses = 'localhost'`, puerto 5432 cerrado.
- Seq nativo (MSI) en localhost:5341.
- Backups: pgBackRest (o `pg_dump` programado) → almacenamiento S3-compatible, con prueba de restore periódica (F5-06).
- CI/CD: GitHub Actions → build + tests → `dotnet publish win-x64` + `ng build` en un zip → SCP/SSH (OpenSSH Server de Windows) → `deploy/windows/deploy.ps1` (release en `C:\prenotaconme\releases`, `app_offline.htm`, `robocopy /MIR` a `D:\IISSites\prenotaconme.rtres.net`, reciclado del app pool).
- Monitoreo: Uptime Kuma.
- GDPR: datos en UE, DPA con OpenAI y Meta, consentimiento opt-in para WhatsApp.

## Estructura de repo (monorepo)
```
/src/backend/PrenotaConMe.Api          (host Minimal API)
/src/backend/Modules/<Modulo>          (un proyecto por módulo)
/src/backend/PrenotaConMe.Shared
/src/frontend/prenotaconme-web         (Angular)
/tests/...
/deploy/README.md, deploy/windows/deploy.ps1
```

## Verificación (al implementar)
- `docker compose up` local levanta todo; tests de integración con Testcontainers verifican constraint de solapamiento y aislamiento entre tenants.
- WhatsApp: número de test de Meta + ngrok para webhook.
- GCal: cuenta de prueba, crear cita → aparece en GCal; evento en GCal → bloquea hueco.
- Lighthouse PWA en la app pública.

## Siguiente paso propuesto
Definir backlog MVP por fases (1: tenancy + auth + catálogo + staff + reservas web; 2: notificaciones + GCal; 3: chatbot; 4: finanzas + informes) y hacer el scaffolding del monorepo.
