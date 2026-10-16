param(
  [Parameter(Mandatory=$true)][string]$PublishPath,
  [string]$SiteName = "SIGE-SISE-Ejecutivo",
  [string]$AppPool = "SIGE-SISE-Ejecutivo",
  [int]$Port = 8088,
  [string]$HostHeader = ""
)
$ErrorActionPreference = "Stop"
if (-not ([Security.Principal.WindowsPrincipal][Security.Principal.WindowsIdentity]::GetCurrent()).IsInRole([Security.Principal.WindowsBuiltInRole]::Administrator)) {
  throw "Ejecute PowerShell como Administrador."
}
Import-Module WebAdministration
$PublishPath = (Resolve-Path $PublishPath).Path
if (-not (Test-Path "IIS:\AppPools\$AppPool")) { New-WebAppPool -Name $AppPool | Out-Null }
Set-ItemProperty "IIS:\AppPools\$AppPool" -Name managedRuntimeVersion -Value ""
Set-ItemProperty "IIS:\AppPools\$AppPool" -Name processModel.identityType -Value ApplicationPoolIdentity
if (Test-Path "IIS:\Sites\$SiteName") { Remove-Website -Name $SiteName }
New-Website -Name $SiteName -PhysicalPath $PublishPath -ApplicationPool $AppPool -Port $Port -HostHeader $HostHeader | Out-Null
$appData = Join-Path $PublishPath "App_Data"
New-Item -ItemType Directory -Path $appData -Force | Out-Null
$identity = "IIS AppPool\$AppPool"
icacls $appData /grant "${identity}:(OI)(CI)M" /T | Out-Null
Write-Host "Sitio instalado: http://localhost:$Port/" -ForegroundColor Green
Write-Host "Admin: http://localhost:$Port/admin" -ForegroundColor Green
Write-Host "IMPORTANTE: cambie Dashboard:AdminPassword antes de habilitar acceso de red." -ForegroundColor Yellow
