# PrenotaConMe — Backlog MVP por fases

Referencias: [`idea_iniziale.md`](../idea_iniziale.md) · [`STACK.md`](STACK.md)

**Definition of Done (todas las historias):** tests (unit + integración con Testcontainers donde haya BD), aislamiento por tenant verificado, textos en i18n (IT/ES/EN), endpoint documentado en OpenAPI, desplegado en staging.

Roles: **A** = Admin del negocio · **S** = Staff · **C** = Cliente · **P** = Plataforma (super-admin)

---

## Fase 0 — Fundamentos (setup)
> Estado: F0-01…F0-07 implementadas. F0-08: artefactos listos para Windows Server/IIS (`deploy/`), falta preparar el servidor y los secrets de GitHub.

| ID | Historia | Criterios de aceptación |
|---|---|---|
| F0-01 | Monorepo y solución .NET | `src/backend` con host API + proyectos por módulo + Shared; `src/frontend` Angular; `dotnet build` y `ng build` OK |
| F0-02 | Entorno local | `docker compose -f deploy/docker-compose.dev.yml up` levanta Postgres + Seq; `appsettings.Development` apunta a ellos |
| F0-03 | Persistencia base | EF Core + Npgsql, migraciones, entidad base con `TenantId`, auditoría (`CreatedAt/By`), global query filter por tenant |
| F0-04 | Observabilidad | Serilog → Seq, OpenTelemetry, health checks `/health` |
| F0-05 | OpenAPI + cliente | OpenAPI nativo + Scalar; cliente TypeScript generado en el build del frontend |
| F0-06 | Shell Angular | Standalone + Signals, Angular Material con tema, Transloco (IT/ES/EN), layouts público/backoffice con lazy loading, `@angular/pwa` |
| F0-07 | CI | GitHub Actions: build + tests backend/frontend en cada PR |
| F0-08 | Staging en VPS | Windows Server 2022: IIS + Hosting Bundle, Postgres nativo, TLS win-acme; deploy por SSH desde CI (zip + `deploy.ps1`) |

## Fase 1 — Core de reservas (MVP usable por un negocio)
> Estado: Fase 1 completa (F1-01…F1-14) (registro de negocio, login Google/Firebase, roles por negocio, sedes con horario de apertura, servicios, staff con invitación por email, disponibilidad semanal en franjas de 1 h y ausencias, cálculo de disponibilidad y reserva con garantía de no solapamiento en BD, página pública de reserva con login Google y teléfono, email de confirmación con `.ics` a cliente y staff, mis citas en `/{slug}/prenotazioni` con cancelar/reprogramar dentro de la antelación configurable, calendario backoffice día/semana con alta de citas sin cuenta, drag & drop y cancelación, ficha de cliente con historial).

| ID | Historia | Criterios de aceptación |
|---|---|---|
| F1-01 | Registro de negocio (P/A) | Alta con nombre, slug único, zona horaria, idioma; crea tenant + usuario Admin |
| F1-02 | Autenticación | Firebase Auth con Google SSO (`@angular/fire`); la API valida el ID token de Firebase (JwtBearer); al primer login se crea el perfil local |
| F1-03 | Roles por tenant | Tabla `UserMembership` (FirebaseUid, TenantId, Rol); autorización por policy en cada endpoint; un usuario puede ser cliente de varios negocios |
| F1-04 | Gestión de sedes (A) | CRUD sedes con dirección, zona horaria, horario de apertura |
| F1-05 | Catálogo de servicios (A) | CRUD servicios: nombre, duración configurable (múltiplos de 30 min), precio, sedes donde se ofrece; seed: Corte 30', Barba 30', Corte+Barba 60' |
| F1-06 | Gestión de staff (A) | CRUD staff, invitación por email (al hacer login con esa cuenta Google se vincula), asignación a una o varias sedes y a servicios |
| F1-07 | Disponibilidad del staff (A/S) | Horario semanal en franjas de 1 h por sede + excepciones (vacaciones, ausencias) |
| F1-08 | Motor de disponibilidad | Calcula slots de 30 min por servicio/staff/sede/fecha considerando horario, excepciones y citas; respeta zona horaria (NodaTime). Opción "cualquier staff": se asigna el staff libre con menos citas ese día |
| F1-09 | Reserva de cita | Crear cita; constraint `EXCLUDE` en BD impide solapamientos aunque haya concurrencia (test dedicado) |
| F1-10 | Página pública `/{slug}` (C) | Flujo: sede → servicio → staff (o "cualquiera") → día → slot → login Google obligatorio (+ teléfono la primera vez) → confirmación; responsive y PWA |
| F1-11 | Confirmación por email (C/S) | Email al cliente y al staff con detalles + `.ics` adjunto |
| F1-12 | Mis citas (C) | Listado de próximas/pasadas; reprogramar a otro slot disponible; cancelar (respetando antelación mínima configurable por el Admin, por defecto 2 h) |
| F1-13 | Calendario backoffice (A/S) | FullCalendar día/semana por staff/sede; Admin crea/mueve/cancela citas (drag & drop); Staff ve solo su calendario |
| F1-14 | Ficha de cliente (A) | Listado de clientes del negocio con historial de citas |

## Fase 2 — Notificaciones y Google Calendar
> Estado: F2-01 implementada (Hangfire con storage Postgres en el esquema `hangfire`; los emails de confirmación son jobs con reintentos; los jobs conservan el tenant que los encoló; dashboard `/hangfire` solo para P, porque muestra datos de todos los negocios). F2-02 implementada (Web Push VAPID con el service worker de Angular; activación desde Mis citas y el dashboard; push al cliente y al staff al reservar, mover o cancelar, salvo a quien hizo el cambio). F2-03 implementada (avisos en `/admin/avvisi` con vigencia en fechas locales del negocio, banner en la página pública y en Mis citas, envío único a todos los clientes por push + email; WhatsApp queda para F2-04/05). **Demo por Telegram:** F2-04/F2-05 (WhatsApp) se aplazan a la versión final; antes van F3-T1/T2 (hechas: bot por negocio, vinculación con deep link, citas y avisos también por Telegram), F2-06 + F3-T3 hechas (recordatorios 24 h y 2 h configurables por el Admin, por push + Telegram + email; jobs diferidos que se descartan solos si la cita se mueve o cancela; el cliente puede desactivarlos en Mis citas, activados por defecto). F3-03 hecha (chatbot por Telegram con OpenAI function calling: servicios, disponibilidad, reservar, mis citas, cancelar y mover con las mismas reglas que la web; confirmación obligatoria en un mensaje posterior, controlada por el servidor; historial por conversación que caduca a las 24 h, que adelanta F3-05; los chats sin vincular reciben el enlace para vincular la cuenta). F3-04 hecha (notas de voz de Telegram de hasta 2 min: descarga con getFile, transcripción con Whisper y el mismo flujo que el texto; si no se entiende, el bot pide repetir o escribir). F3-07 hecha, con las reglas de `docs/reglas_agente_ia.md` que encajan con function calling (límite por chat de 6 mensajes/min y 60/día, respondiendo solo al primero que lo supera; presupuesto diario por negocio de 500 mensajes y 2 M de tokens con respuesta fija al superarlo; saludos y agradecimientos con respuesta fija sin LLM; contexto de los últimos 6 mensajes del cliente; `max_tokens` 300; prompt contra inyección y fuera de tema; notas de voz transcritas solo dentro de los límites, con presupuesto de 60 min de audio al día por negocio, duración máxima 1 min y `gpt-4o-mini-transcribe` en lugar de `whisper-1`; tokens y segundos de audio en `chat_usage` y métricas `chatbot.*` en Seq; id de conversación enmascarado en logs). La máquina de estados con JSON del documento se descartó: las herramientas ya validan en servidor. Siguiente: F3-06 (derivación a humano).

| ID | Historia | Criterios de aceptación |
|---|---|---|
| F2-01 | Infraestructura de jobs | Hangfire con storage Postgres, dashboard protegido para P/A |
| F2-02 | Web Push (C/S) | Suscripción VAPID desde la PWA; push en confirmación, cambio y cancelación |
| F2-03 | Anuncios del Admin (A→C) | Mensaje a todos los usuarios activos del negocio (push + email; WhatsApp opcional) con vigencia; banner en la página pública. Recargos (p.ej. festivos) solo informativos en el MVP |
| F2-04 | Cuenta WhatsApp por negocio (A) | Tabla `WhatsAppAccount` (WabaId, PhoneNumberId, token cifrado); configuración manual con **el número del dev** para pruebas |
| F2-05 | Plantillas WhatsApp | Plantillas utility (confirmación, recordatorio, cancelación) en IT/ES/EN, enviadas a aprobación de Meta |
| F2-06 ✅ | Recordatorios | Jobs programados 24 h y 2 h antes (configurable); se reprograman/cancelan con la cita; opt-in del cliente registrado |
| F2-07 | Conexión GCal del staff (S) | OAuth Google por staff; tokens cifrados; desconectar |
| F2-08 | Exportar citas a GCal | Crear/actualizar/borrar evento en GCal al cambiar la cita |
| F2-09 | Importar ocupado desde GCal | Watch channels + sync tokens; eventos personales bloquean slots; renovación automática de canales |
| F2-10 | Tiempo real | SignalR: el calendario del backoffice se actualiza sin recargar |

## Fase 3 — Chatbot (Telegram para la demo, después WhatsApp)
| ID | Historia | Criterios de aceptación |
|---|---|---|
| F3-00 | Abstracción de canal | `IMessagingChannel` + modelo común de mensaje (texto, audio, botones); el motor del chatbot no conoce el canal |
| F3-T1 ✅ | Bot Telegram | Bot de BotFather, token cifrado por tenant; long polling en dev, webhook con `secret_token` en prod |
| F3-T2 ✅ | Vinculación Telegram | Deep link `t.me/<bot>?start=<token>` desde "Mi cuenta" en la web (o botón Compartir contacto) vincula `chat_id` al cliente |
| F3-T3 ✅ | Recordatorios por Telegram | Los jobs de F2-06 envían también por Telegram si el cliente está vinculado |
| F3-01 | Webhook Meta (WhatsApp) | Verificación del challenge y firma `X-Hub-Signature-256`; tenant resuelto por `phone_number_id`; idempotencia por message id |
| F3-02 | Identificación del cliente | Por número de teléfono vinculado a su cuenta (F1-10 pide el teléfono). Si no existe cuenta: el bot envía un enlace a la página pública para registrarse con Google y vincular el número |
| F3-03 ✅ | Mensajes de texto | GPT con tools: `list_services`, `get_availability`, `create_booking`, `reschedule`, `cancel`, `my_appointments`; confirmación explícita antes de reservar |
| F3-04 ✅ | Mensajes de voz | Descarga del audio (Graph API o Telegram `getFile`) → transcripción → mismo flujo que texto |
| F3-05 | Estado de conversación | Historial por conversación en Postgres, expira a las 24 h (ventana de servicio de WhatsApp) |
| F3-06 | Derivación a humano | Si el bot no entiende o el cliente lo pide, avisa al Admin y deja de responder en esa conversación |
| F3-07 ✅ | Control de coste | Límite de mensajes/tokens por tenant/día, métricas de uso en Seq |

## Fase 4 — Finanzas e informes
| ID | Historia | Criterios de aceptación |
|---|---|---|
| F4-01 | Precio real de la cita | Al completar la cita se registra el importe cobrado (por defecto el precio del servicio, editable para recargos) y estado (completada / no-show) |
| F4-02 | Gastos (A) | CRUD de gastos fijos (recurrentes mensuales: alquiler, suministros) y variables (productos, comisiones), por sede |
| F4-03 | Balance mensual (A) | Ingresos − gastos fijos − variables = beneficio neto, filtrable por sede |
| F4-04 | Informe por staff (A) | Mensual: nº de servicios, ventas totales, desglose por servicio |
| F4-05 | Informe general (A) | Ingresos totales, gastos, beneficio neto, comparación con el mes anterior; gráficos ngx-echarts |
| F4-06 | Exportación | PDF (QuestPDF) y Excel (ClosedXML) de los informes F4-03/04/05 |

## Fase 5 — SaaS y producción
| ID | Historia | Criterios de aceptación |
|---|---|---|
| F5-01 | Embedded Signup WhatsApp | Cada negocio conecta su número desde el backoffice (requiere Tech Provider + Business verificado) |
| F5-02 | Onboarding guiado (A) | Wizard: sede → servicios → staff → horarios → WhatsApp → enlace público |
| F5-03 | Panel de plataforma (P) | Listado de tenants, estado, uso de WhatsApp/IA, suspender tenant |
| F5-04 | GDPR | Consentimientos, exportación y borrado de datos del cliente, política de retención, DPA con proveedores |
| F5-05 | Hardening | Rate limiting, CORS, cabeceras de seguridad, rotación de secretos |
| F5-06 | Backups y DR | pgBackRest full + WAL a S3-compatible; prueba de restore documentada |
| F5-07 | Producción | Dominio, TLS, Uptime Kuma, alertas |

---

## Decisiones tomadas
- **Cuenta obligatoria** para clientes: Firebase Auth con Google SSO.
- **Cancelación/reprogramación:** antelación configurable por negocio, por defecto 2 h; se registran no-shows.
- **"Cualquier staff":** reparto equitativo (el libre con menos citas ese día).
- **Recargos:** solo informativos en el MVP; el importe real se ajusta al completar la cita.

- **Demo con el primer cliente:** chatbot por **Telegram**; WhatsApp después, sobre la misma abstracción de canal.

## Decisiones pendientes
1. **Monetización SaaS:** ¿plan de pago para negocios (Stripe) más adelante? Afecta a F5.
2. **Clientes del chatbot sin cuenta Google:** con cuenta obligatoria, el bot no reservará hasta vincularla (ver F3-02). Revisar si genera demasiada fricción.
