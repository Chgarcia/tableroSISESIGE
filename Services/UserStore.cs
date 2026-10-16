using System.Security.Cryptography;
using Microsoft.Data.Sqlite;

namespace SigeSiseEjecutivo.Services;

public sealed record UserInfo(long Id, string Username, string DisplayName, string Role, bool Active, string CreatedUtc, string? LastLoginUtc);
public sealed class UserStore(Database db, IConfiguration config)
{
    private string Conn => new SqliteConnectionStringBuilder {DataSource=db.DatabasePath,Mode=SqliteOpenMode.ReadWriteCreate}.ToString();
    public async Task InitializeAsync()
    {
        await using var c = new SqliteConnection(Conn); await c.OpenAsync();
        await using var cmd=c.CreateCommand(); cmd.CommandText="""
        CREATE TABLE IF NOT EXISTS AdminUsers (
          Id INTEGER PRIMARY KEY AUTOINCREMENT,
          Username TEXT NOT NULL COLLATE NOCASE UNIQUE,
          DisplayName TEXT NOT NULL,
          Role TEXT NOT NULL DEFAULT 'Administrador',
          PasswordHash TEXT NOT NULL,
          Active INTEGER NOT NULL DEFAULT 1,
          CreatedUtc TEXT NOT NULL,
          LastLoginUtc TEXT
        );
        """; await cmd.ExecuteNonQueryAsync();
        cmd.CommandText="SELECT COUNT(*) FROM AdminUsers";
        var count=Convert.ToInt64(await cmd.ExecuteScalarAsync());
        if(count==0)
        {
            var username=config["Dashboard:AdminUser"]??"admin";
            var password=config["Dashboard:AdminPassword"];
            if(string.IsNullOrWhiteSpace(password))
                throw new InvalidOperationException("Configure Dashboard__AdminPassword antes de crear el administrador inicial.");
            await InsertAsync(c,username,"Administrador general","Administrador",password);
        }
    }
    private static string Hash(string password)
    {
        var salt=RandomNumberGenerator.GetBytes(16);
        var hash=Rfc2898DeriveBytes.Pbkdf2(password,salt,210000,HashAlgorithmName.SHA256,32);
        return $"PBKDF2-SHA256$210000${Convert.ToBase64String(salt)}${Convert.ToBase64String(hash)}";
    }
    private static bool Verify(string password,string stored)
    {
        try { var parts=stored.Split('$'); if(parts.Length!=4||parts[0]!="PBKDF2-SHA256")return false;
          var rounds=int.Parse(parts[1]); if(rounds<100000||rounds>1000000)return false;
          var salt=Convert.FromBase64String(parts[2]); var expected=Convert.FromBase64String(parts[3]);
          var actual=Rfc2898DeriveBytes.Pbkdf2(password,salt,rounds,HashAlgorithmName.SHA256,expected.Length);
          return CryptographicOperations.FixedTimeEquals(expected,actual);
        } catch { return false; }
    }
    private static async Task InsertAsync(SqliteConnection c,string username,string name,string role,string password,SqliteTransaction? transaction=null)
    {
        await using var cmd=c.CreateCommand();cmd.Transaction=transaction;cmd.CommandText="INSERT INTO AdminUsers(Username,DisplayName,Role,PasswordHash,Active,CreatedUtc) VALUES($u,$n,$r,$p,1,$d)";
        cmd.Parameters.AddWithValue("$u",username);cmd.Parameters.AddWithValue("$n",name);cmd.Parameters.AddWithValue("$r",role);
        cmd.Parameters.AddWithValue("$p",Hash(password));cmd.Parameters.AddWithValue("$d",DateTimeOffset.UtcNow.ToString("O"));await cmd.ExecuteNonQueryAsync();
    }
    public async Task<UserInfo?> AuthenticateAsync(string username,string password)
    {
        await using var c=new SqliteConnection(Conn);await c.OpenAsync();await using var cmd=c.CreateCommand();
        cmd.CommandText="SELECT Id,Username,DisplayName,Role,Active,CreatedUtc,LastLoginUtc,PasswordHash FROM AdminUsers WHERE Username=$u AND Active=1";
        cmd.Parameters.AddWithValue("$u",username.Trim());
        await using var reader=await cmd.ExecuteReaderAsync();
        if(!await reader.ReadAsync())return null;
        var user=Read(reader); var hash=reader.GetString(7);await reader.CloseAsync();
        if(!Verify(password,hash))return null;
        cmd.CommandText="UPDATE AdminUsers SET LastLoginUtc=$now WHERE Id=$id";cmd.Parameters.Clear();cmd.Parameters.AddWithValue("$now",DateTimeOffset.UtcNow.ToString("O"));cmd.Parameters.AddWithValue("$id",user.Id);await cmd.ExecuteNonQueryAsync();return user;
    }
    private static UserInfo Read(SqliteDataReader r)=>new(r.GetInt64(0),r.GetString(1),r.GetString(2),r.GetString(3),r.GetInt64(4)==1,r.GetString(5),r.IsDBNull(6)?null:r.GetString(6));
    public async Task<List<UserInfo>> ListAsync()
    {
        var result=new List<UserInfo>();await using var c=new SqliteConnection(Conn);await c.OpenAsync();await using var cmd=c.CreateCommand();
        cmd.CommandText="SELECT Id,Username,DisplayName,Role,Active,CreatedUtc,LastLoginUtc FROM AdminUsers ORDER BY Username";
        await using var r=await cmd.ExecuteReaderAsync();while(await r.ReadAsync())result.Add(Read(r));return result;
    }
    public async Task<UserInfo?> GetAsync(long id)
    {
        await using var c=new SqliteConnection(Conn);await c.OpenAsync();await using var cmd=c.CreateCommand();cmd.CommandText="SELECT Id,Username,DisplayName,Role,Active,CreatedUtc,LastLoginUtc FROM AdminUsers WHERE Id=$id";cmd.Parameters.AddWithValue("$id",id);
        await using var r=await cmd.ExecuteReaderAsync();return await r.ReadAsync()?Read(r):null;
    }
    public async Task SaveAsync(long id,string username,string name,string role,string? password,bool active,long actorId)
    {
        username=username.Trim(); name=name.Trim();
        if(username.Length is < 3 or > 60||name.Length is < 2 or > 120||!System.Text.RegularExpressions.Regex.IsMatch(username,"^[a-zA-Z0-9._-]+$"))throw new ArgumentException("Usuario o nombre inválido. Usuario: 3 a 60 caracteres, letras, números, punto, guion o guion bajo.");
        if(role is not ("Administrador" or "Capturista"))throw new ArgumentException("Rol inválido.");
        if(id==actorId && (!active||role!="Administrador"))throw new ArgumentException("No puede desactivar su cuenta ni quitarse el rol administrador.");
        if(id==0 && string.IsNullOrWhiteSpace(password))throw new ArgumentException("La contraseña es obligatoria para un usuario nuevo.");
        if(password is not null && password.Length>0 && (password.Length<12 || password.Length>256))throw new ArgumentException("La contraseña debe tener de 12 a 256 caracteres.");
        await using var c=new SqliteConnection(Conn);await c.OpenAsync();await using var tx=await c.BeginTransactionAsync();
        try
        {
            if(id>0)
            {
                var existing=await GetAsync(id)??throw new ArgumentException("El usuario no existe.");
                if(existing.Active && existing.Role=="Administrador" && (!active||role!="Administrador"))
                {
                    await using var count=c.CreateCommand();count.Transaction=(SqliteTransaction)tx;count.CommandText="SELECT COUNT(*) FROM AdminUsers WHERE Active=1 AND Role='Administrador'";
                    if(Convert.ToInt64(await count.ExecuteScalarAsync())<=1)throw new ArgumentException("Debe permanecer al menos un administrador activo.");
                }
                await using var cmd=c.CreateCommand();cmd.Transaction=(SqliteTransaction)tx;
                cmd.CommandText="UPDATE AdminUsers SET Username=$u, DisplayName=$n, Role=$r, Active=$a"+(string.IsNullOrEmpty(password)?"":" ,PasswordHash=$p")+" WHERE Id=$id";
                cmd.Parameters.AddWithValue("$id",id);cmd.Parameters.AddWithValue("$u",username);cmd.Parameters.AddWithValue("$n",name);cmd.Parameters.AddWithValue("$r",role);cmd.Parameters.AddWithValue("$a",active?1:0);
                if(!string.IsNullOrEmpty(password))cmd.Parameters.AddWithValue("$p",Hash(password));await cmd.ExecuteNonQueryAsync();
            }
            else await InsertAsync(c,username,name,role,password!,(SqliteTransaction)tx);
            await tx.CommitAsync();
        }
        catch(SqliteException ex) when(ex.SqliteErrorCode==19){await tx.RollbackAsync();throw new ArgumentException("Ya existe una cuenta con ese nombre de usuario.");}
        catch{await tx.RollbackAsync();throw;}
    }
}
