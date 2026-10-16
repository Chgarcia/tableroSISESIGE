# Seguimiento ejecutivo SISE / SIGE

Aplicación web ASP.NET Core **.NET 10** para IIS, con **SQLite** como fuente del tablero y un módulo administrativo simple para cargar el Excel de seguimiento.

## Qué incluye

- Tablero ejecutivo basado en el diseño entregado en `Tablero_ejecutivo_SISE_SIGE 1.zip`.
- Consulta SISE / SIGE desde SQLite, sin seleccionar Excel en el navegador.
- Filtros, KPIs, distribución por estatus, temas, prioridades, detalle y exportación CSV.
- `/admin` con autenticación por cookie y carga de `.xlsx` / `.xlsm`.
- Importador del Excel implementado en .NET, sin Excel/Office instalado en el servidor.
- Reemplazo **transaccional**: si el archivo es inválido, la información anterior se conserva.
- Base SQLite local en `App_Data/sige-sise.db`; no se incluye en Git.

## Datos locales

La base SQLite y los archivos Excel de seguimiento no se publican en el repositorio. En una instalación nueva se crea una base vacía; cargue un Excel autorizado desde `/admin` para poblar el tablero. Los datos locales existentes se conservan.

El importador busca dinámicamente la fila de encabezados de cada hoja; por eso soporta que SISE y SIGE tengan el encabezado en filas distintas.

## Configuración del administrador inicial

El usuario inicial es `admin`. No existe una contraseña predeterminada.

Antes del primer arranque, configure las variables de entorno de ASP.NET Core:

- `Dashboard__AdminUser`
- `Dashboard__AdminPassword`

`Dashboard__AdminPassword` es obligatoria si todavía no hay usuarios en la base. Las cuentas existentes no se modifican al cambiar esta variable. No guarde contraseñas en archivos versionados.

## Ejecutar en desarrollo

Requiere .NET 10 SDK.

Configure `Dashboard__AdminPassword` en el entorno antes de ejecutar:

```powershell
cd .\SIGE_SISE_Ejecutivo
dotnet restore
dotnet run
```

Abra la URL que muestre la consola. El tablero está en `/` y el administrador en `/admin`.

## Publicar para IIS

En una máquina con .NET 10 SDK:

```powershell
Set-ExecutionPolicy -Scope Process Bypass -Force
.\scripts\publish.ps1
```

Se crea la carpeta `publish` con la aplicación lista para IIS.

### Requisitos del servidor IIS

1. Windows Server con IIS.
2. Instalar el **ASP.NET Core Hosting Bundle para .NET 10**.
3. Application Pool con **No Managed Code**.
4. Dar permiso **Modify** al identity del Application Pool sobre `App_Data`, ya que SQLite debe escribir ahí.
5. No habilitar WebDAV sobre esta aplicación.
6. Publicar preferentemente detrás de HTTPS en el entorno institucional.

### Alta automática del sitio

Ejecute PowerShell como Administrador:

```powershell
.\scripts\install-iis.ps1 -PublishPath "D:\Sitios\SIGE-SISE" -Port 8088
```

Si primero publicó a otra carpeta, copie el contenido de `publish` a `D:\Sitios\SIGE-SISE` y luego ejecute el script.

## Flujo de actualización

1. Ingrese a `/admin`.
2. Seleccione el Excel vigente.
3. La aplicación valida que existan las hojas `SISE` y `SIGE`.
4. Busca los encabezados `#`, `Sesión`, `Áreas`, `Órgano Jurisdiccional`, `Contacto`, `Cto`, `Tema`, `Tipo`, `Detalle tema`, `Acuerdos`, `Seguimiento`, `Estatus`.
5. Si todo es correcto, reemplaza los registros de SQLite dentro de una transacción y registra fecha, archivo y usuario de la carga.
6. El tablero toma los nuevos datos al refrescar la página.

## Endpoints

- `/` tablero.
- `/admin` administración.
- `/api/dashboard` datos completos SISE/SIGE en JSON.
- `/api/meta` conteos y última carga.

## Estructura

- `Program.cs`: API, autenticación y endpoints admin.
- `Services/XlsxImporter.cs`: lectura OpenXML del Excel.
- `Services/Database.cs`: persistencia SQLite.
- `Services/AdminPages.cs`: interfaz administrativa.
- `wwwroot/`: tablero y recursos visuales.
- `App_Data/sige-sise.db`: base SQLite local, excluida de Git.
- `sample-data/`: archivos Excel locales, excluidos de Git.
- `scripts/`: publicación y configuración IIS.

## Consideraciones de seguridad

La autenticación solicitada es deliberadamente simple. Para un despliegue institucional más amplio conviene sustituirla por AD/LDAP/SSO y restringir `/admin` por red o grupo. El tablero/API son de consulta y no requieren login en esta versión.

## Ramas de trabajo

- `dev`: desarrollo y trabajo diario.
- `qa`: validación y pruebas.
- `prod`: versiones para producción.

No versionar bases de datos, Excel, credenciales ni archivos generados de compilación/publicación.
