$ErrorActionPreference = "Stop"
$root = Resolve-Path (Join-Path $PSScriptRoot "..")
$required = @(
  "SigeSiseEjecutivo.csproj","Program.cs","appsettings.json","web.config",
  "wwwroot\index.html","wwwroot\app.js","wwwroot\styles.css","wwwroot\assets\oaj-logo.png",
  "Services\Database.cs","Services\XlsxImporter.cs"
)
foreach ($f in $required) {
  if (-not (Test-Path (Join-Path $root $f))) { throw "Falta $f" }
}
Write-Host "Estructura del paquete correcta." -ForegroundColor Green
