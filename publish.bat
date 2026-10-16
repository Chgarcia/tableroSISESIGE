@echo off
setlocal EnableExtensions
cd /d "%~dp0"
set "OUT=%~dp0publish\IIS"
where dotnet >nul 2>&1
if errorlevel 1 (
  echo ERROR: Se requiere .NET 10 SDK en el equipo de compilacion.
  exit /b 1
)
if exist "%OUT%" (
  echo Limpiando carpeta de publicacion anterior...
  rmdir /s /q "%OUT%"
  if errorlevel 1 exit /b 1
)
echo Publicando aplicacion NET 10 para IIS en: %OUT%
dotnet publish "%~dp0SigeSiseEjecutivo.csproj" -c Release -r win-x64 --self-contained false -o "%OUT%"
if errorlevel 1 (
  echo ERROR: Fallo la publicacion.
  exit /b 1
)
if not exist "%OUT%\SigeSiseEjecutivo.dll" exit /b 1
if not exist "%OUT%\web.config" exit /b 1
if not exist "%OUT%\App_Data\sige-sise.db" exit /b 1
echo.
echo PUBLICACION LISTA: %OUT%
echo Suba el CONTENIDO de esa carpeta al directorio fisico de IIS.
echo ATENCION: NO reemplace App_Data\sige-sise.db en actualizaciones posteriores.
exit /b 0
