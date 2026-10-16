using System.Security.Claims;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Http.Features;
using SigeSiseEjecutivo.Services;

var builder = WebApplication.CreateBuilder(args);
builder.Services.AddSingleton<Database>();
builder.Services.AddSingleton<XlsxImporter>();
builder.Services.AddSingleton<UserStore>();
builder.Services.AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme).AddCookie(o => { o.LoginPath="/admin/login"; o.Cookie.Name="SigeSiseAdmin"; o.ExpireTimeSpan=TimeSpan.FromHours(8); o.SlidingExpiration=true; o.Cookie.HttpOnly=true; o.Cookie.SameSite=SameSiteMode.Strict;
    o.Events.OnValidatePrincipal=async ctx=>{
        var raw=ctx.Principal?.FindFirstValue(ClaimTypes.NameIdentifier);
        var store=ctx.HttpContext.RequestServices.GetRequiredService<UserStore>();
        if(!long.TryParse(raw,out var uid)){ctx.RejectPrincipal();await ctx.HttpContext.SignOutAsync();return;}
        var user=await store.GetAsync(uid);
        if(user is null||!user.Active||user.Username!=ctx.Principal?.Identity?.Name||!ctx.Principal.IsInRole(user.Role)){
            ctx.RejectPrincipal();await ctx.HttpContext.SignOutAsync();
        }
    }; });
builder.Services.AddAuthorization();
builder.Services.Configure<FormOptions>(o=>o.MultipartBodyLengthLimit=(builder.Configuration.GetValue<long?>("Dashboard:MaxUploadMb")??25)*1024*1024);
var app = builder.Build();
var db=app.Services.GetRequiredService<Database>(); await db.InitializeAsync();
await app.Services.GetRequiredService<UserStore>().InitializeAsync();
// IIS sub-application: PathBase is provided by ASP.NET Core Module.
// Normalize /TableroSISESIGE to /TableroSISESIGE/ so relative assets resolve.
app.Use(async (ctx, next) => {
    if (ctx.Request.Path == PathString.Empty && ctx.Request.PathBase.HasValue) {
        ctx.Response.Redirect(ctx.Request.PathBase.Value + "/", permanent: false);
        return;
    }
    await next();
});
app.UseDefaultFiles(); app.UseStaticFiles();
app.Use(async (ctx,next)=>{
    if(ctx.Request.Method=="POST" && ctx.Request.Path.StartsWithSegments("/admin")){
        var origin=ctx.Request.Headers.Origin.ToString();
        if(!string.IsNullOrWhiteSpace(origin) && (!Uri.TryCreate(origin,UriKind.Absolute,out var uri)||!string.Equals(uri.Host,ctx.Request.Host.Host,StringComparison.OrdinalIgnoreCase)||uri.Port!=(ctx.Request.Host.Port??(ctx.Request.IsHttps?443:80)))){
            ctx.Response.StatusCode=403;return;
        }
    }
    await next();
});
app.UseAuthentication(); app.UseAuthorization();

app.MapGet("/api/meta", async (Database data) => { var counts=await data.GetCountsAsync(); var last=await data.GetLastImportAsync(); return Results.Ok(new { counts, total=counts.Values.Sum(), lastImport=last.At, lastFile=last.FileName, databasePath=data.DatabasePath, databaseExists=File.Exists(data.DatabasePath), processId=Environment.ProcessId }); });
app.MapGet("/api/diagnostico", async (Database data) => {
    var counts=await data.GetCountsAsync(); var last=await data.GetLastImportAsync();
    return Results.Ok(new { databasePath=data.DatabasePath, databaseExists=File.Exists(data.DatabasePath),
       total=counts.Values.Sum(), counts, lastImport=last.At, lastFile=last.FileName,
       processId=Environment.ProcessId, environment=app.Environment.EnvironmentName,
       contentRoot=app.Environment.ContentRootPath, timestamp=DateTimeOffset.UtcNow });
}).RequireAuthorization(p=>p.RequireRole("Administrador"));
app.MapGet("/api/dashboard", async (Database data) => {
    var sise=await data.GetRecordsAsync("SISE"); var sige=await data.GetRecordsAsync("SIGE"); var last=await data.GetLastImportAsync();
    object Map(SigeSiseEjecutivo.Models.TrackingRecord r)=>new { uid=$"{r.System}-{r.RowId}", id=r.ItemNumber, session=r.Session, areas=r.Areas, organ=r.Organ, contact=r.Contact, circuit=r.Circuit, topic=r.Topic, type=r.Type, request=r.Request, agreements=r.Agreements, followup=r.Followup, status=string.IsNullOrWhiteSpace(r.Status)?"Sin estatus":r.Status };
    return Results.Ok(new { datasets=new { SISE=sise.Select(Map), SIGE=sige.Select(Map) }, lastImport=last.At, lastFile=last.FileName });
});

// HTML admin pages use a base URL scoped to this IIS application, never the parent site.
IResult AdminHtml(HttpContext ctx, string html, int statusCode=200) {
    var prefix = ctx.Request.PathBase.Value ?? string.Empty;
    var tag = "<base href=\"" + System.Net.WebUtility.HtmlEncode(prefix + "/") + "\">";
    return Results.Content(html.Replace("<head>", "<head>" + tag, StringComparison.OrdinalIgnoreCase), "text/html", statusCode: statusCode);
}
string AppUrl(HttpContext ctx, string path) => (ctx.Request.PathBase.Value ?? "") + path;

app.MapGet("/admin/login", (HttpContext ctx) => ctx.User.Identity?.IsAuthenticated==true ? Results.Redirect(AppUrl(ctx, "/admin")) : AdminHtml(ctx, AdminPages.Login()));
app.MapPost("/admin/login", async (HttpContext ctx, UserStore store) => {
    var form=await ctx.Request.ReadFormAsync();
    var user=await store.AuthenticateAsync(form["username"].ToString(),form["password"].ToString());
    if(user is null) return AdminHtml(ctx, AdminPages.Login("Usuario o contraseña incorrectos."), 401);
    var principal=new ClaimsPrincipal(new ClaimsIdentity([
      new Claim(ClaimTypes.NameIdentifier,user.Id.ToString()),
      new Claim(ClaimTypes.Name,user.Username),
      new Claim(ClaimTypes.Role,user.Role)
    ],CookieAuthenticationDefaults.AuthenticationScheme));
    await ctx.SignInAsync(CookieAuthenticationDefaults.AuthenticationScheme,principal);return Results.Redirect(AppUrl(ctx, "/admin"));
});
app.MapPost("/admin/logout", async (HttpContext ctx)=>{await ctx.SignOutAsync(); return Results.Redirect(AppUrl(ctx, "/admin/login"));}).RequireAuthorization();
app.MapGet("/admin", async (Database data, HttpContext ctx)=>AdminHtml(ctx, AdminPages.Home(await data.GetCountsAsync(),await data.GetLastImportAsync(), databasePath:data.DatabasePath))).RequireAuthorization();
app.MapPost("/admin/import", async (HttpContext ctx, Database data, XlsxImporter importer, IConfiguration cfg) => {
    var max=(cfg.GetValue<long?>("Dashboard:MaxUploadMb")??25)*1024*1024; var form=await ctx.Request.ReadFormAsync(); var file=form.Files.GetFile("excel");
    if(file is null||file.Length==0) return AdminHtml(ctx, AdminPages.Home(await data.GetCountsAsync(),await data.GetLastImportAsync(),"Seleccione un archivo Excel.",true,data.DatabasePath),400);
    if(file.Length>max) return AdminHtml(ctx, AdminPages.Home(await data.GetCountsAsync(),await data.GetLastImportAsync(),"El archivo excede el tamaño permitido.",true,data.DatabasePath),400);
    var ext=Path.GetExtension(file.FileName).ToLowerInvariant(); if(ext is not (".xlsx" or ".xlsm")) return AdminHtml(ctx, AdminPages.Home(await data.GetCountsAsync(),await data.GetLastImportAsync(),"Formato no permitido. Use .xlsx o .xlsm.",true,data.DatabasePath),400);
    try { await using var s=file.OpenReadStream(); var parsed=importer.Parse(s); await data.ReplaceAllAsync(parsed.Rows,Path.GetFileName(file.FileName),ctx.User.Identity?.Name??"admin"); var counts=await data.GetCountsAsync();
        if(counts.GetValueOrDefault("SISE") != parsed.Counts["SISE"] || counts.GetValueOrDefault("SIGE") != parsed.Counts["SIGE"])
            throw new InvalidOperationException("La verificación posterior a la importación no coincide con los registros procesados.");
        var msg=$"Carga verificada en SQLite: {counts["SISE"]} registros SISE y {counts["SIGE"]} registros SIGE. Abra el tablero y actualice (Ctrl+F5).";
        return AdminHtml(ctx, AdminPages.Home(counts,await data.GetLastImportAsync(),msg,false,data.DatabasePath)); }
    catch(Exception ex){ return AdminHtml(ctx, AdminPages.Home(await data.GetCountsAsync(),await data.GetLastImportAsync(),"Error de importación: "+ex.Message,true,data.DatabasePath),400); }
}).RequireAuthorization();

app.MapGet("/admin/users", async (UserStore users, HttpContext ctx, string? message) =>
    AdminHtml(ctx, AdminPages.Users(await users.ListAsync(),message))).RequireAuthorization(p=>p.RequireRole("Administrador"));
app.MapGet("/admin/users/edit/{id:long}", async (long id,UserStore users, HttpContext ctx) =>
    AdminHtml(ctx, AdminPages.UserEdit(id==0?null:await users.GetAsync(id)))).RequireAuthorization(p=>p.RequireRole("Administrador"));
app.MapPost("/admin/users/save", async (HttpContext ctx,UserStore users) => {
    var f=await ctx.Request.ReadFormAsync();long.TryParse(f["id"],out var id);
    var currentId=long.Parse(ctx.User.FindFirstValue(ClaimTypes.NameIdentifier)!);
    try {
       await users.SaveAsync(id,f["username"].ToString(),f["displayName"].ToString(),f["role"].ToString(),f["password"].ToString(),f["active"]=="on",currentId);
       return Results.Redirect(AppUrl(ctx, "/admin/users?message=")+Uri.EscapeDataString("Usuario guardado correctamente."));
    } catch(Exception ex) when(ex is ArgumentException or Microsoft.Data.Sqlite.SqliteException) {
       return AdminHtml(ctx, AdminPages.UserEdit(id==0?null:await users.GetAsync(id),ex.Message),400);
    }
}).RequireAuthorization(p=>p.RequireRole("Administrador"));

app.MapFallbackToFile("index.html");
app.Run();

