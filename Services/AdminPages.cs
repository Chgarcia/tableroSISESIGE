using System.Net;

namespace SigeSiseEjecutivo.Services;

public static class AdminPages
{
    public static string Login(string? error=null) => Page("Acceso administrativo", $"""
      <main class="admin-shell"><section class="admin-card login-card">
      <img src="assets/oaj-logo.png" class="admin-logo" alt="OAJ"><p class="section-kicker">Administración</p><h1>Actualización SISE / SIGE</h1>
      <p>Acceso restringido para cargar la versión vigente del Excel de seguimiento.</p>
      {(error is null?"":$"<div class='admin-alert error'>{WebUtility.HtmlEncode(error)}</div>")}
      <form method="post" action="admin/login" class="admin-form"><label>Usuario<input name="username" autocomplete="username" required></label><label>Contraseña<input name="password" type="password" autocomplete="current-password" required></label><button class="button primary" type="submit">Ingresar</button></form>
      <p class="admin-note"><a href="./">← Volver al tablero</a></p></section></main>
    """);

    public static string Home(Dictionary<string,int> counts,(string? At,string? FileName,string? User) last,string? message=null,bool error=false,string? databasePath=null) => Page("Administración", $"""
      <main class="admin-shell"><section class="admin-card wide">
      <div class="admin-top"><div><p class="section-kicker">Administración</p><h1>Carga del Excel SISE / SIGE</h1><p>La carga reemplaza en una sola transacción la información vigente de ambas hojas.</p></div><form method="post" action="admin/logout"><button class="button ghost" type="submit">Cerrar sesión</button></form></div>
      {(message is null?"":$"<div class='admin-alert {(error?"error":"success")}'>{WebUtility.HtmlEncode(message)}</div>")}
      <div class="admin-kpis"><div><span>SISE</span><strong>{counts.GetValueOrDefault("SISE")}</strong></div><div><span>SIGE</span><strong>{counts.GetValueOrDefault("SIGE")}</strong></div><div><span>Total</span><strong>{counts.Values.Sum()}</strong></div></div>
      <div class="import-info"><strong>Archivo SQLite en uso</strong><span style="overflow-wrap:anywhere">{E(databasePath ?? "No disponible")}</span><strong>Última carga</strong><span>{WebUtility.HtmlEncode(last.FileName ?? "Carga inicial incluida")}</span><span>{FormatUtc(last.At)}</span></div>
      <form method="post" action="admin/import" enctype="multipart/form-data" class="upload-box"><label>Excel de seguimiento (.xlsx / .xlsm)<input type="file" name="excel" accept=".xlsx,.xlsm" required></label><button class="button primary" type="submit">Validar y actualizar SQLite</button><small>El archivo debe contener las hojas SISE y SIGE y los encabezados: #, Sesión, Áreas, Órgano Jurisdiccional, Contacto, Cto, Tema, Tipo, Detalle tema, Acuerdos, Seguimiento y Estatus.</small></form>
      <div class="admin-actions"><a class="button secondary" href="admin/users">Administrar usuarios</a><a class="button secondary" href="./">Abrir tablero</a><a class="button ghost" href="api/meta">Ver estado API</a><a class="button ghost" href="api/diagnostico">Diagnóstico SQLite</a></div>
      </section></main>
    """);

    public static string Users(IReadOnlyList<UserInfo> users,string? message=null) => Page("Usuarios", $"""
      <main class="admin-shell"><section class="admin-card wide"><div class="admin-top"><div><p class="section-kicker">Administración</p><h1>Usuarios del sistema</h1><p>Las cuentas se almacenan en SQLite. Solo los administradores pueden gestionar usuarios.</p></div><a class="button ghost" href="admin">Volver</a></div>
      {(string.IsNullOrEmpty(message)?"":$"<div class='admin-alert success'>{WebUtility.HtmlEncode(message)}</div>")}
      <p><a class="button primary" href="admin/users/edit/0">Nuevo usuario</a></p>
      <div class="admin-table-wrap"><table class="admin-table"><thead><tr><th>Usuario</th><th>Nombre</th><th>Rol</th><th>Estado</th><th>Último acceso (UTC)</th><th></th></tr></thead><tbody>
      {string.Join("",users.Select(u=>$"<tr><td>{E(u.Username)}</td><td>{E(u.DisplayName)}</td><td>{E(u.Role)}</td><td>{(u.Active?"Activo":"Inactivo")}</td><td>{E(u.LastLoginUtc ?? "—")}</td><td><a href='admin/users/edit/{u.Id}'>Editar</a></td></tr>"))}
      </tbody></table></div><p class="admin-note">Rol Administrador: importa Excel y gestiona usuarios. Rol Capturista: importa Excel, sin administración de usuarios.</p></section></main>
    """);
    public static string UserEdit(UserInfo? u,string? error=null)=>Page(u is null?"Nuevo usuario":"Editar usuario",$"""
      <main class="admin-shell"><section class="admin-card"><div class="admin-top"><div><p class="section-kicker">Usuarios</p><h1>{(u is null?"Nuevo usuario":"Editar usuario")}</h1></div><a href="admin/users">Volver</a></div>
      {(error is null?"":$"<div class='admin-alert error'>{E(error)}</div>")}
      <form method="post" action="admin/users/save" class="admin-form"><input type="hidden" name="id" value="{u?.Id??0}">
      <label>Usuario<input name="username" value="{E(u?.Username??"")}" required minlength="3" maxlength="60" autocomplete="off"></label>
      <label>Nombre para mostrar<input name="displayName" value="{E(u?.DisplayName??"")}" required maxlength="120"></label>
      <label>Rol<select name="role"><option value="Administrador" {(u?.Role is null or "Administrador"?"selected":"")}>Administrador</option><option value="Capturista" {(u?.Role=="Capturista"?"selected":"")}>Capturista</option></select></label>
      <label>{(u is null?"Contraseña":"Nueva contraseña (dejar vacío para conservar)")}<input name="password" type="password" {(u is null?"required":"")} minlength="12" maxlength="256" autocomplete="new-password"></label>
      <label class="admin-check"><input type="checkbox" name="active" {(u is null || u.Active?"checked":"")}> Cuenta activa</label>
      <button class="button primary" type="submit">Guardar usuario</button></form></section></main>
    """);
    static string E(string s)=>WebUtility.HtmlEncode(s);

    static string FormatUtc(string? value)=>DateTimeOffset.TryParse(value,out var d)?d.ToLocalTime().ToString("dd/MM/yyyy HH:mm"):"Base inicial";
    static string Page(string title,string body)=>$"""<!doctype html><html lang="es-MX"><head><meta charset="utf-8"><meta name="viewport" content="width=device-width,initial-scale=1"><title>{title}</title><link rel="stylesheet" href="styles.css"><link rel="stylesheet" href="admin.css"></head><body>{body}</body></html>""";
}
