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