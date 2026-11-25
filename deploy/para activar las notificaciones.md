
● En el servidor no hace falta Node: PowerShell genera las claves directamente. He probado la generación en Windows PowerShell 5.1, la misma versión que trae
  Windows Server 2022, y salen claves del formato correcto. La parte que escribe en appsettings.Production.json no la he ejecutado.

  1. Crea el script. En el servidor, crea C:\prenotaconme\vapid.ps1 con el Bloc de notas y pega esto. Guardarlo como archivo evita el problema de las líneas
  cortadas al pegar en la consola.

  $path = 'C:\prenotaconme\shared\appsettings.Production.json'

  # Generar el par de claves VAPID (P-256)
  $ec = [System.Security.Cryptography.ECDsa]::Create([System.Security.Cryptography.ECCurve+NamedCurves]::nistP256)
  $p = $ec.ExportParameters($true)
  function ConvertTo-B64Url([byte[]]$b) { [Convert]::ToBase64String($b).TrimEnd('=').Replace('+', '-').Replace('/', '_') }
  $public = ConvertTo-B64Url ([byte[]](@([byte]4) + $p.Q.X + $p.Q.Y))
  $private = ConvertTo-B64Url $p.D

  # Copia de seguridad y escritura en la configuración
  Copy-Item $path "$path.bak" -Force
  $cfg = Get-Content $path -Raw | ConvertFrom-Json
  $cfg | Add-Member -Force -NotePropertyName Push -NotePropertyValue ([pscustomobject]@{ PublicKey = $public; PrivateKey = $private })
  $cfg | ConvertTo-Json -Depth 10 | Set-Content $path -Encoding UTF8

  "Claves guardadas en $path. PublicKey: $public"

  2. Ejecútalo como administrador. Después borra el script, que no guarda la clave pero ya no hace falta:
  powershell -ExecutionPolicy Bypass -File C:\prenotaconme\vapid.ps1
  Remove-Item C:\prenotaconme\vapid.ps1

  3. Recicla el app pool para que la API cargue las claves:
  Import-Module WebAdministration; Restart-WebAppPool prenotaconme.rtres.net

  Ten en cuenta:
  - Antes de escribir, el script guarda una copia de seguridad en appsettings.Production.json.bak. Si algo sale mal, restáurala.
  - deploy.ps1 copia ese archivo en cada release, así que las claves se mantienen entre despliegues.
  - Genera las claves una sola vez. Si las cambias, todos los usuarios tienen que volver a activar las notificaciones.