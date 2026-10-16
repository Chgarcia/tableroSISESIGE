# Despliegue en IIS

## 1. Preparar servidor

Instale IIS y el ASP.NET Core Hosting Bundle compatible con .NET 10. Reinicie IIS después de instalar el Hosting Bundle.

## 2. Publicar

Desde el proyecto, en una estación con .NET 10 SDK:

```powershell
Set-ExecutionPolicy -Scope Process Bypass -Force
.\scripts\publish.ps1
```

Copie el contenido de `publish` al servidor, por ejemplo a:

```text
D:\Sitios\SIGE-SISE
```

## 3. Crear Application Pool

- Nombre sugerido: `SIGE-SISE-Ejecutivo`
- .NET CLR version: **No Managed Code**
- Pipeline: Integrated
- Identity: ApplicationPoolIdentity

## 4. Crear sitio

Apunte el sitio IIS a la carpeta publicada. Puede automatizarlo:

```powershell
.\scripts\install-iis.ps1 -PublishPath "D:\Sitios\SIGE-SISE" -Port 8088
```

## 5. Permisos SQLite

El identity del Application Pool necesita **Modify** en:

```text
D:\Sitios\SIGE-SISE\App_Data
```

El script de instalación aplica este permiso automáticamente.

## 6. Configurar el administrador inicial

Antes del primer arranque, configure variables de entorno del proceso/sitio. No existe contraseña predeterminada; `Dashboard__AdminPassword` es obligatoria si no hay cuentas en la base:

```text
Dashboard__AdminUser
Dashboard__AdminPassword
```

No almacene contraseñas en archivos versionados. Las cuentas existentes conservan sus credenciales.

## 7. Probar

- `http://servidor:8088/` — tablero.
- `http://servidor:8088/admin` — carga administrativa.
- `http://servidor:8088/api/meta` — conteos y última carga.

En `/admin`, cargue un Excel de seguimiento autorizado y verifique los conteos. La base SQLite y los Excel locales no se incluyen en Git; una instalación nueva inicia sin registros.

## 8. Operación

Cada Excel válido reemplaza completamente los datos SISE/SIGE dentro de una transacción SQLite. Si la validación falla, la base anterior no se modifica.
