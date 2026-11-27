# Alta de PrenotaConMe como Tech Provider de WhatsApp (Meta)

Objetivo: que cada negocio conecte **su propio número de WhatsApp Business** desde el backoffice (Embedded Signup, F5-01) y que el chatbot, los avisos y los recordatorios salgan por WhatsApp (F2-04, F2-05, F3-01, F3-02).

> Meta cambia pantallas y plazos a menudo. Comprueba cada paso en la [guía oficial para Tech Providers](https://developers.facebook.com/documentation/business-messaging/whatsapp/solution-providers/get-started-for-tech-providers). Las cifras de plazos y límites de este documento vienen en parte de guías de terceros.

## Nuestro caso: empresa en Perú, primeros clientes en Italia

| Tema | Qué implica |
|---|---|
| **Verificación de la empresa (paso 2)** | Se verifica la empresa peruana: razón social y RUC con la Ficha RUC de SUNAT o la partida registral de SUNARP, y un recibo o documento con la dirección fiscal. La web, la política de privacidad y el Business portfolio tienen que mostrar exactamente esa razón social y esa dirección. |
| **Número de prueba +51** | Sirve para desarrollo y para los vídeos de App Review. El país de nuestro número no limita a qué negocios damos servicio: el programa Tech Provider es global. |
| **Negocios italianos (+39)** | Cada negocio conecta **su** número con Embedded Signup y queda en **su propio** Business portfolio (italiano), no en el nuestro. Necesitan una cuenta de Facebook y un número +39 que reciba el SMS o la llamada de verificación. La coexistence depende del país del número del negocio (Italia), no del nuestro. |
| **Coste por mensaje** | Meta factura según el **país del destinatario**: los clientes de las barberías son +39, así que se aplica la tarifa de Italia. Que cada negocio pague con su tarjeta en euros evita que nosotros facturemos mensajería entre países (IVA intracomunitario, tipo de cambio). |
| **Límites del negocio** | Un negocio recién conectado tiene un límite diario de mensajes iniciados por él (plantillas), que sube con la calidad de los mensajes y con su propia verificación. Con barberías pequeñas no debería ser un problema, pero conviene que verifiquen su portfolio. |
| **Idioma de las plantillas** | Las plantillas de los negocios italianos van en italiano (las de ES/EN quedan para otros mercados). |
| **Datos personales (afecta a F5-04)** | Tratamos datos de residentes en la UE, así que se aplica el **RGPD** aunque la empresa esté en Perú. Los negocios son responsables del tratamiento y nosotros encargados: hace falta un DPA con cada negocio. Perú no tiene decisión de adecuación de la UE, así que las transferencias necesitan **cláusulas contractuales tipo**. Probablemente necesitamos también un **representante en la UE** (art. 27 RGPD). Además se aplica la ley peruana 29733. Conviene revisarlo con un abogado antes de tener clientes reales. |
| **Servidor en Perú** | La base de datos (clientes, citas, conversaciones del chatbot) está en el VPS en Perú, así que cada dato de un cliente italiano es una **transferencia internacional**. Hace falta: cláusulas contractuales tipo, módulo 2 (responsable → encargado) en el DPA con cada negocio; una **evaluación de impacto de la transferencia** (TIA) documentada; y medidas técnicas (TLS, cifrado de tokens, copias cifradas, acceso restringido). Subencargados a declarar: OpenAI (chatbot y transcripción), Google (Firebase Auth y Calendar), Meta (WhatsApp) y Telegram, la mayoría en EE. UU. Alternativa si un cliente o el abogado lo exige: alojar la base de datos de los negocios europeos en un servidor de la UE (más coste y operación, pero quita la transferencia a Perú). |

## Resumen del orden

| # | Paso | Quién | Plazo orientativo |
|---|---|---|---|
| 0 | Requisitos previos (empresa, web, 2FA) | Tú | — |
| 1 | Business portfolio en Meta | Tú | 15 min |
| 2 | Verificación de la empresa (Business Verification) | Tú → Meta | 2–5 días hábiles, a veces más |
| 3 | App de Meta con el producto WhatsApp | Tú | 30 min |
| 4 | Número de prueba y webhook en modo desarrollo | Tú + yo | 1 día |
| 5 | Configuración de Embedded Signup (Facebook Login for Business) | Tú | 30 min |
| 6 | Usuario del sistema y token permanente | Tú | 15 min |
| 7 | Desarrollo de F2-04/F2-05/F3-01/F5-01 con el número de prueba | Yo | — |
| 8 | App Review (acceso avanzado, 2 vídeos) | Tú + yo → Meta | 1–3 semanas |
| 9 | Access Verification (confirmación de Tech Provider) | Tú → Meta | días |

Los pasos 1–6 se pueden hacer ya; el 8 necesita que la funcionalidad del paso 7 esté hecha, porque Meta pide vídeos de nuestra app enviando mensajes y creando plantillas.

## 0. Requisitos previos

- **Empresa con datos legales:** razón social, dirección y RUC (Ficha RUC de SUNAT o partida de SUNARP). El nombre tiene que coincidir con el de los documentos que subas en el paso 2.
- **Web pública con el mismo nombre de empresa** y estas páginas accesibles sin login: política de privacidad, términos del servicio y cómo pedir el borrado de datos. Meta las pide en la configuración de la app y en la verificación.
- **Dominio y correo corporativo** (p. ej. `admin@tudominio`): la verificación por dominio o por correo es la más rápida.
- **Autenticación en dos pasos (2FA)** activada en tu cuenta de Facebook y en el Business portfolio.
- Un **número de teléfono** para pruebas que no esté en uso en WhatsApp: el +51 que vas a usar (o el número de prueba gratuito que da Meta en el paso 4).

## 1. Business portfolio

1. Entra en <https://business.facebook.com> → *Crear cuenta* (o usa el portfolio que ya tengas).
2. Nombre legal de la empresa, tu nombre y el correo corporativo.
3. *Configuración del negocio* → *Información del negocio*: dirección, teléfono y web **iguales** a los documentos.

## 2. Verificación de la empresa

1. *Configuración del negocio* → *Centro de seguridad* → **Iniciar verificación**.
2. Sube un documento oficial con razón social y dirección (Ficha RUC de SUNAT, partida registral de SUNARP o un recibo de servicios a nombre de la empresa).
3. Confirma por correo del dominio, teléfono o DNS.
4. Espera la aprobación (normalmente 2–5 días hábiles). Si la rechazan, suele ser porque el nombre o la dirección no coinciden exactamente con la web o el documento.

## 3. App de Meta

1. <https://developers.facebook.com> → *Mis apps* → **Crear app** → tipo **Business** → asociarla al portfolio del paso 1.
2. Añade el producto **WhatsApp** (*Configurar*). Meta crea una cuenta de WhatsApp Business de prueba con un número de prueba.
3. *Configuración de la app* → *Básica*: icono, categoría, **URL de la política de privacidad**, **URL de los términos** y **URL de borrado de datos**. Copia el **App ID** y el **App Secret**: no los pegues en ningún archivo del repo.

## 4. Número de prueba y webhook (modo desarrollo)

1. *WhatsApp* → *Configuración de la API*: anota el **Phone number ID** y el **WhatsApp Business Account ID** de prueba, y añade tu móvil como destinatario de prueba (hasta 5).
2. *WhatsApp* → *Configuración* → *Webhook*:
   - URL de devolución de llamada: `https://<dominio de staging>/api/notifications/whatsapp/webhook` (la crearé en F3-01).
   - Token de verificación: una cadena aleatoria que inventas tú; la guardaremos en el servidor.
   - Suscríbete al campo **messages**.
3. En modo desarrollo solo los destinatarios de prueba reciben mensajes. Es suficiente para construir y grabar los vídeos del paso 8.

## 5. Embedded Signup (Facebook Login for Business)

1. En la app, añade el producto **Facebook Login for Business**.
2. *Configuraciones* → **Crear configuración** → variación de login **WhatsApp Embedded Signup**.
3. Permisos: `whatsapp_business_management`, `whatsapp_business_messaging` (y `business_management` si la pantalla lo propone).
4. *Dominios permitidos* / *URI de redirección OAuth válidos*: el dominio de staging y el de producción (`https://...`).
5. Copia el **Configuration ID**: lo usará el botón "Conecta WhatsApp" del backoffice.

## 6. Usuario del sistema y token permanente

1. *Configuración del negocio* → *Usuarios* → **Usuarios del sistema** → *Añadir* → rol **Administrador**.
2. *Asignar activos*: la app del paso 3 (control total) y la cuenta de WhatsApp de prueba.
3. **Generar token** sin caducidad con `whatsapp_business_management` y `whatsapp_business_messaging`.
4. Guárdalo solo en user-secrets (local) y en `C:\prenotaconme\shared\appsettings.Production.json` (servidor), igual que la clave de OpenAI.

Los tokens de **cada negocio** no los generas tú: salen del Embedded Signup cuando el negocio conecta su número, y se guardan cifrados en la base de datos (`WhatsAppAccount`, F2-04).

## 7. Desarrollo en PrenotaConMe (lo hago yo, con los datos de los pasos 3–6)

- **F2-04:** tabla `WhatsAppAccount` por negocio (WABA ID, Phone number ID, token cifrado con Data Protection).
- **F3-01:** webhook con verificación del challenge, firma `X-Hub-Signature-256` con el App Secret, negocio por `phone_number_id` e idempotencia por id de mensaje. El chatbot ya es independiente del canal, así que reutiliza todo lo de Telegram.
- **F3-02:** identificar al cliente por su número; si no tiene cuenta, enviarle el enlace para registrarse.
- **F2-05:** plantillas de confirmación, recordatorio y cancelación en IT/ES/EN, creadas desde la app y enviadas a aprobación.
- **F5-01:** botón "Conecta WhatsApp" con el Embedded Signup (SDK de Facebook JS + Configuration ID) que guarda la cuenta del negocio.

## 8. App Review

1. *Revisión de la app* → *Permisos y funciones*: solicita **acceso avanzado** a `whatsapp_business_messaging` y `whatsapp_business_management`.
2. Graba y sube dos vídeos (sin cortes, se tiene que ver la URL de la app):
   - Un mensaje enviado desde PrenotaConMe que llega a WhatsApp (p. ej. confirmación de una cita).
   - PrenotaConMe creando una plantilla de mensaje.
   - Muy recomendable: un negocio completando el **Embedded Signup** dentro del backoffice; Meta rechaza a menudo las solicitudes sin ese recorrido.
3. Describe el caso de uso: "SaaS de reservas para barberías y salones; cada negocio conecta su número para confirmar citas, enviar recordatorios y atender a sus clientes con un asistente".

## 9. Access Verification

Tras aprobar la App Review, Meta pide confirmar que eres Tech Provider (*Access Verification* en el panel de la app). Cuando termina:

- Puedes dar de alta negocios fuera del modo desarrollo. El límite por defecto se cita como **10 negocios nuevos cada 7 días**, que sube a **200** con la verificación completa (según guías de terceros: confírmalo en el panel).

## Pagos y costes

- Meta cobra por **mensajes de plantilla** (confirmaciones y recordatorios). Las respuestas dentro de la ventana de 24 h desde el último mensaje del cliente son gratuitas.
- Como Tech Provider, lo normal es que **cada negocio añada su propio método de pago** en WhatsApp Manager durante el Embedded Signup. Que pague la plataforma y lo repercuta (línea de crédito compartida) es una función de los Solution Partners. Hay que decidir el modelo antes de F5-01.

## Números que ya usan la app WhatsApp Business ("coexistence")

Meta permite conectar a la API un número que el negocio ya usa en la app WhatsApp Business del móvil, sin perder la app (se vincula escaneando un QR desde la app, versión reciente). Según varios proveedores está disponible en la UE, Italia incluida, desde finales de 2025; Meta no publica la lista de países en las páginas de Embedded Signup, así que hay que confirmarlo en la primera prueba real. Embedded Signup v2 deja de funcionar el 15 de octubre de 2026: usaremos v4.

## Qué me tienes que pasar (por canal privado, nunca en el repo)

- App ID y App Secret.
- Configuration ID de Embedded Signup.
- Token del usuario del sistema.
- Phone number ID y WABA ID de prueba.
- Token de verificación del webhook que hayas elegido.

## Fuentes

- [Meta: Become a Tech Provider](https://developers.facebook.com/documentation/business-messaging/whatsapp/solution-providers/get-started-for-tech-providers)
- [Meta: Embedded Signup v4](https://developers.facebook.com/documentation/business-messaging/whatsapp/embedded-signup/version-4-public-preview/)
- [Twilio: Tech Provider program integration guide](https://www.twilio.com/docs/whatsapp/isv/tech-provider-program/integration-guide)
- [Infobip: Tech Provider Program step-by-step](https://www.infobip.com/docs/whatsapp/tech-provider-program/setup-and-integration)
- [Chakra: WhatsApp Coexistence en la UE](https://chakrahq.com/article/whatsapp-coexistence-live-eu-uk-europe-whatsapp-business-for-api-live/amp/)
