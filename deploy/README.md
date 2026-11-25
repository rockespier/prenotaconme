# Despliegue en Windows Server 2022 (IIS)

Un único sitio IIS: la API (.NET 10, in-process) sirve también la PWA de Angular desde `wwwroot`. PostgreSQL y Seq corren nativos en el mismo servidor.

```
Internet ──443──> IIS (sitio "prenotaconme.rtres.net", TLS con win-acme)
                   └─ ASP.NET Core Module ─> PrenotaConMe.Api
                         ├─ /api/*, /health   endpoints
                         └─ resto             wwwroot (Angular) + fallback a index.html
                   PostgreSQL 18 (localhost:5432) · Seq (localhost:5341)
```

Todos los comandos: **PowerShell como Administrador** en el servidor.

## 1. IIS + .NET 10 Hosting Bundle

```powershell
Install-WindowsFeature Web-Server, Web-WebSockets -IncludeManagementTools
```

Descarga e instala el **ASP.NET Core 10 Hosting Bundle** desde https://dotnet.microsoft.com/download/dotnet/10.0 (sección *ASP.NET Core Runtime → Windows → Hosting Bundle*). Después:

```powershell
net stop was /y; net start w3svc
```

## 2. Carpetas, app pool y sitio

| Ruta | Uso |
|---|---|
| `D:\IISSites\prenotaconme.rtres.net` | Ruta física del sitio IIS (archivos en producción) |
| `C:\prenotaconme\incoming` | Zip subido por el workflow + `deploy.ps1` |
| `C:\prenotaconme\releases\<versión>` | Releases descomprimidas (se guardan las últimas 5, para rollback) |
| `C:\prenotaconme\shared` | `appsettings.Production.json`, copiado en cada release |

```powershell
$root = 'C:\prenotaconme'
$site = 'D:\IISSites\prenotaconme.rtres.net'
'incoming','releases','shared' | ForEach-Object { New-Item -ItemType Directory -Force "$root\$_" | Out-Null }
New-Item -ItemType Directory -Force $site | Out-Null

Import-Module WebAdministration
New-WebAppPool -Name prenotaconme.rtres.net
Set-ItemProperty IIS:\AppPools\prenotaconme.rtres.net -Name managedRuntimeVersion -Value ''   # "No managed code"
New-Website -Name prenotaconme.rtres.net -PhysicalPath $site -ApplicationPool prenotaconme.rtres.net -Port 80 -HostHeader 'prenotaconme.rtres.net'

# Hangfire (F2-01) corre dentro de la app: el pool no debe pararse por inactividad
Install-WindowsFeature Web-AppInit
Set-ItemProperty IIS:\AppPools\prenotaconme.rtres.net -Name startMode -Value AlwaysRunning
Set-ItemProperty IIS:\AppPools\prenotaconme.rtres.net -Name processModel.idleTimeout -Value ([TimeSpan]::Zero)
Set-ItemProperty IIS:\Sites\prenotaconme.rtres.net -Name applicationDefaults.preloadEnabled -Value True

# El app pool necesita leer los archivos del sitio
icacls $site /grant 'IIS AppPool\prenotaconme.rtres.net:(OI)(CI)RX' /T
```

`deploy.ps1` pone `app_offline.htm` en el sitio (el módulo de ASP.NET Core detiene la app y libera los archivos), copia la release con `robocopy /MIR`, quita `app_offline.htm` y recicla el app pool.

Si existe, detén o elimina el *Default Web Site* para liberar el puerto 80.

## 3. Configuración de producción (fuera de las releases)

Copia `deploy/windows/appsettings.Production.example.json` a `C:\prenotaconme\shared\appsettings.Production.json` y rellena la contraseña de la BD, el dominio y la sección `Smtp` (emails de confirmación; sin `Smtp:Host` no se envían, solo se registran en el log) `Notifications:PublicUrl` (URL pública https del sitio: enlaces de los emails y webhook de Telegram), `Telegram:Mode` (`Webhook` en producción), `Push:PublicKey`/`Push:PrivateKey` (claves VAPID para las notificaciones push; se generan con el comando de `CLAUDE.md`, y si se cambian, los dispositivos tienen que volver a activar las notificaciones) y `Platform:AdminEmails` (emails Google verificados que pueden abrir el panel de jobs `/hangfire`, desde el menú de la cuenta en el backoffice). `deploy.ps1` lo copia en cada release.

Hangfire crea sus tablas en el esquema `hangfire` de la misma BD al arrancar (el rol `prenota` es dueño de la BD, así que tiene permiso).

## 4. PostgreSQL 18

Instalador de https://www.postgresql.org/download/windows/ y, después (con `C:\Program Files\PostgreSQL\18\bin` en el PATH):

```powershell
psql -U postgres -c "CREATE ROLE prenota LOGIN PASSWORD 'CHANGE_ME';"
psql -U postgres -c "CREATE DATABASE prenotaconme OWNER prenota;"
psql -U postgres -d prenotaconme -c "CREATE EXTENSION IF NOT EXISTS btree_gist;"
```

Postgres solo debe escuchar en local. En `C:\Program Files\PostgreSQL\18\data\postgresql.conf`: `listen_addresses = 'localhost'`, y no abras el 5432 en el firewall.

## 5. Seq (opcional, logs y trazas)

Instalador MSI de https://datalust.co/download. Escucha en `http://localhost:5341`. Para verlo desde tu PC: `ssh -L 5341:localhost:5341 deploy@TU_SERVIDOR`.

## 6. TLS con win-acme (Let's Encrypt)

Con el DNS del dominio ya apuntando al servidor y el puerto 80 abierto, descarga win-acme de https://www.win-acme.com, ejecuta `wacs.exe` y elige el sitio `prenotaconme.rtres.net`. Crea el binding 443 y una tarea programada de renovación.

```powershell
New-NetFirewallRule -DisplayName 'HTTP/HTTPS' -Direction Inbound -Protocol TCP -LocalPort 80,443 -Action Allow
```

## 7. OpenSSH Server y usuario de deploy

```powershell
Add-WindowsCapability -Online -Name OpenSSH.Server~~~~0.0.1.0
Set-Service sshd -StartupType Automatic; Start-Service sshd

# Usuario local para GitHub Actions (necesita ser admin para reciclar el app pool)
$pass = Read-Host -AsSecureString 'Password para deploy'
New-LocalUser -Name deploy -Password $pass -PasswordNeverExpires
Add-LocalGroupMember -Group Administrators -Member deploy
icacls C:\prenotaconme /grant 'deploy:(OI)(CI)M' /T
```

Para usuarios administradores, OpenSSH en Windows lee la clave pública de `C:\ProgramData\ssh\administrators_authorized_keys`, no de `~\.ssh`:

```powershell
Add-Content C:\ProgramData\ssh\administrators_authorized_keys 'CONTENIDO_DE_prenotaconme_deploy.pub'
icacls C:\ProgramData\ssh\administrators_authorized_keys /inheritance:r /grant 'Administrators:F' /grant 'SYSTEM:F'
Restart-Service sshd
```

Restringe el puerto 22 a las IPs que necesites si tu proveedor lo permite.

## 8. GitHub

En *Settings → Environments → `staging`*:

| Tipo | Nombre | Valor |
|---|---|---|
| Secret | `VPS_HOST` | IP o dominio del servidor |
| Secret | `VPS_USER` | `deploy` |
| Secret | `VPS_SSH_KEY` | clave **privada** completa (`prenotaconme_deploy`) |
| Variable | `SITE_DOMAIN` | `staging.tudominio.com` (smoke test de `/health`) |

Clave en tu PC: `ssh-keygen -t ed25519 -C github-deploy -f "$HOME\.ssh\prenotaconme_deploy"` (pulsa Enter dos veces: sin passphrase). Copiar la privada para el secret: `Get-Content "$HOME\.ssh\prenotaconme_deploy" -Raw | Set-Clipboard`. Prueba: `ssh -i $HOME\.ssh\prenotaconme_deploy deploy@TU_SERVIDOR hostname`.

## Despliegue y rollback

Cada push a `main` con el CI en verde ejecuta `.github/workflows/deploy.yml`, que publica la API para win-x64 con Angular en `wwwroot`, sube un zip y ejecuta `deploy/windows/deploy.ps1`. Ese script descomprime en `releases\<versión>`, la copia a `D:\IISSites\prenotaconme.rtres.net` y recicla el app pool.

Rollback manual (reinstala una release anterior):
```powershell
Get-ChildItem C:\prenotaconme\releases | Sort-Object CreationTime -Descending   # ver versiones
powershell -ExecutionPolicy Bypass -File C:\prenotaconme\incoming\deploy.ps1 -Version <versión-anterior>
```

Logs de arranque si el sitio da 500.30/502.5: Visor de eventos → *Application* (origen *IIS AspNetCore Module V2*), o Seq.
