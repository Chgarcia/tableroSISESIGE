param(
  [string]$Configuration = "Release",
  [string]$Output = "..\publish"
)
$ErrorActionPreference = "Stop"
$project = Join-Path $PSScriptRoot "..\SigeSiseEjecutivo.csproj"
$out = Join-Path $PSScriptRoot $Output
Write-Host "[1/3] Restaurando paquetes..." -ForegroundColor Cyan
dotnet restore $project
Write-Host "[2/3] Publicando .NET 10 para IIS..." -ForegroundColor Cyan
dotnet publish $project -c $Configuration --no-restore -o $out
Write-Host "[3/3] Verificando archivos..." -ForegroundColor Cyan
$required = @("SigeSiseEjecutivo.dll", "web.config", "wwwroot\index.html")
foreach ($f in $required) {
  $p = Join-Path $out $f
  if (-not (Test-Path $p)) { throw "Falta archivo de publicación: $f" }
}
Write-Host "Publicación lista en: $out" -ForegroundColor Green
