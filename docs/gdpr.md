# GDPR / RGPD en PrenotaConMe (F5-04)

> No es asesoría legal. Base para revisar con un abogado antes del primer cliente real. Contexto: empresa en Perú, servidor en Perú, primeros negocios y clientes en Italia (ver también `whatsapp_tech_provider.md`).

## Roles

| Datos | Responsable (titular) | Encargado |
|---|---|---|
| Citas, clientes del negocio, conversaciones del bot con sus clientes, avisos | Cada negocio | PrenotaConMe |
| Cuenta de usuario (login, teléfono), seguridad y registros técnicos | PrenotaConMe | — |

Hace falta un **contrato de encargo (DPA, art. 28)** con cada negocio, con las **cláusulas contractuales tipo módulo 2** por la transferencia a Perú, y la lista de subencargados de abajo.

## Qué está hecho en la app

| Derecho / obligación | Cómo |
|---|---|
| Información (art. 13) | Página `/privacy` (borrador en italiano con huecos `[...]`), enlazada en el pie de las páginas públicas y en "Mis citas" |
| Acceso y portabilidad (art. 15/20) | "Scarica i miei dati" en Mis citas → `GET /api/users/me/export`: JSON con cuenta, negocios, citas, chats y notificaciones de todos los negocios |
| Supresión por el usuario (art. 17) | "Elimina account" → `DELETE /api/users/me`: anonimiza sus citas (sin nombre, teléfono ni cuenta; las futuras se cancelan), borra la cuenta y, en cascada, membresías, chats, vínculos de Telegram, dispositivos y preferencias; después borra la cuenta de Firebase. Admins y staff tienen que dejar el negocio antes (409 `account.hasBusinessRole`) |
| Supresión pedida al negocio | Ficha del cliente → "Cancella i dati del cliente" → `POST /api/scheduling/clients/erase?key=`: anonimiza sus citas en ese negocio y le quita el rol de cliente allí |
| Limitación de la conservación | Job diario `privacy-chat-retention` (03:00): borra las conversaciones del bot sin mensajes desde hace `Privacy:ChatRetentionDays` días (30) |
| Seguridad (art. 32) | TLS, HSTS, cabeceras y límites de peticiones (F5-05), tokens cifrados con Data Protection, acceso por roles por negocio |

Cada módulo declara sus datos personales implementando `IPersonalData` (Shared); el módulo Users los recorre para exportar y borrar.

## Subencargados

| Proveedor | Para qué | Dónde | Garantía |
|---|---|---|---|
| [Proveedor del VPS] | Servidor y base de datos | Perú | Cláusulas contractuales tipo |
| Google (Firebase Auth, Google Calendar opcional) | Login; calendario del staff | EE. UU./UE | Data Privacy Framework |
| Microsoft (Entra ID) | Login | EE. UU./UE | Data Privacy Framework |
| OpenAI | Asistente y transcripción de notas de voz | EE. UU. | Data Privacy Framework / DPA de OpenAI |
| Telegram | Mensajes (demo) | Varios | Revisar |
| Meta (WhatsApp Cloud API) | Mensajes (versión final) | EE. UU./UE | Data Privacy Framework |
| [Proveedor SMTP] | Emails | [país] | [revisar] |

## Pendiente (legal u operativo)

- Rellenar los huecos de `/privacy` (razón social, RUC, dirección, email de contacto, representante en la UE, proveedores) y traducirla a ES/EN si hace falta.
- Designar un **representante en la UE** (art. 27) si el abogado lo confirma.
- Redactar el **DPA** con cada negocio y la **evaluación de impacto de la transferencia** (TIA) a Perú.
- **Avisos del negocio (F2-03):** hoy se envían a todos los clientes. Si su contenido es promocional, necesitan consentimiento o, como mínimo, un enlace para darse de baja.
- Conservación de las citas: decidir con los negocios un plazo (p. ej. 10 años por obligaciones fiscales) y añadir la anonimización automática si se acuerda.
- Registros técnicos (Seq, logs de IIS): fijar retención (p. ej. 30–90 días).
- Registro de actividades de tratamiento (art. 30) a partir de este documento.
