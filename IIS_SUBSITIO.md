# Liberación como aplicación (subsitio) de IIS

## Ejemplo de URL
Sitio padre: https://servidor.institucional.gob.mx/
Aplicación hija (alias): TableroSISESIGE
URL final: https://servidor.institucional.gob.mx/TableroSISESIGE/

## Instalación
1. Instalar ASP.NET Core Hosting Bundle .NET 10 en el servidor (reiniciar IIS con coordinación de operaciones si se instaló posteriormente).
2. Generar publicación con `publish.bat`; usar el contenido de `publish\IIS`.
3. Crear carpeta física, ejemplo `D:\Sitios\TableroSISESIGE`; copiar contenido de publicación. No reemplazar `App_Data\sige-sise.db` en actualizaciones (respaldar antes).
4. IIS > Application Pools > Add Application Pool: `AppPool_TableroSISESIGE`, `.NET CLR Version: No Managed Code`, Integrated, Identity: ApplicationPoolIdentity, Enable 32-Bit Applications: False.
5. IIS > Sites > [sitio institucional padre] > Add Application: alias `TableroSISESIGE`, Application Pool `AppPool_TableroSISESIGE`, Physical Path `D:\Sitios\TableroSISESIGE`. **No usar Add Virtual Directory**: debe convertirse en Application.
6. HTTPS/hostname/certificado se heredan del sitio padre. No crear un binding extra en el subsitio.
7. NTFS para `IIS AppPool\AppPool_TableroSISESIGE`: lectura/ejecución en raíz; Modificar sobre `App_Data` (incluidos archivos SQLite auxiliares). Permisos de despliegue únicamente para el equipo autorizador de liberaciones.
8. Bloquear el acceso HTTP directo a carpetas y archivos fuera de wwwroot; ASP.NET Core sirve los estáticos exclusivamente desde `wwwroot`.

## Validación obligatoria de rutas
- GET `/TableroSISESIGE/` => 200, HTML del tablero.
- GET `/TableroSISESIGE/styles.css` => 200 y Content-Type text/css.
- GET `/TableroSISESIGE/app.js` => 200 y Content-Type javascript.
- GET `/TableroSISESIGE/assets/oaj-logo.png` => 200 image/png.
- GET `/TableroSISESIGE/api/meta` => 200 JSON con conteos SISE/SIGE.
- GET `/TableroSISESIGE/api/dashboard` => 200 JSON con datasets.sise y datasets.sige (o mayúsculas).
- GET `/TableroSISESIGE/admin` => redirige al login (sin sesión).
- POST `/TableroSISESIGE/admin/login` => 302 a `/TableroSISESIGE/admin` tras autenticación correcta.
- GET `/TableroSISESIGE/admin/users` => 200 solo para administrador autenticado.
- POST `/TableroSISESIGE/admin/import` => carga Excel y valida conteos.
- GET `/TableroSISESIGE/App_Data/sige-sise.db` => 404; no debe descargar la base.
- GET `/api/dashboard` del sitio PADRE no debe ser utilizado por esta aplicación.

### Prueba PowerShell
```powershell
$base = 'https://servidor.institucional.gob.mx/TableroSISESIGE/'
@('','styles.css','app.js','assets/oaj-logo.png','api/meta','api/dashboard','admin/login') | ForEach-Object {
  $url = $base + $_
  try { $r = Invoke-WebRequest -Uri $url -MaximumRedirection 0 -SkipHttpErrorCheck; "{0} {1}" -f $r.StatusCode,$url }
  catch { "ERROR $url : $($_.Exception.Message)" }
}
```

## Estrategia de URL
Los recursos HTML de entrada y llamadas JavaScript usan rutas relativas. Las páginas administrativas reciben `base href` con el `Request.PathBase` resuelto por IIS; los redirect se construyen con ese prefijo. No configurar `UsePathBase("/TableroSISESIGE")` bajo IIS cuando ANCM ya lo entrega: duplicaría el prefijo.

## Alcance de validación
Comprobaciones estáticas sobre rutas y paquete realizadas. Compilación / prueba de integración IIS deben ejecutarse en el servidor Windows con .NET SDK/Hosting Bundle.
