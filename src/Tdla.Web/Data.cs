using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Tdla.Web;

public class AppUser : IdentityUser<Guid>
{
    public string FullName { get; set; } = "";
    public string EmployerName { get; set; } = "";
    public bool Verified { get; set; }
    public AppUser() { Id = Guid.NewGuid(); SecurityStamp = Guid.NewGuid().ToString("N"); }
}
public class AppRole : IdentityRole<Guid>
{
    public AppRole() { Id = Guid.NewGuid(); }
    public AppRole(string name) : this() { Name = name; }
}
public class AppUserClaim : IdentityUserClaim<Guid> { public Guid Key { get; set; } = Guid.NewGuid(); }
public class AppRoleClaim : IdentityRoleClaim<Guid> { public Guid Key { get; set; } = Guid.NewGuid(); }
public class AppUserLogin : IdentityUserLogin<Guid> { public Guid Id { get; set; } = Guid.NewGuid(); }
public class AppUserToken : IdentityUserToken<Guid> { public Guid Id { get; set; } = Guid.NewGuid(); }
public class AppUserRole : IdentityUserRole<Guid> { public Guid Id { get; set; } = Guid.NewGuid(); }

public class AppDb(DbContextOptions<AppDb> options) : IdentityDbContext<AppUser, AppRole, Guid, AppUserClaim, AppUserRole, AppUserLogin, AppRoleClaim, AppUserToken>(options)
{
    public DbSet<ManagedFile> Files => Set<ManagedFile>();
    public DbSet<SiteSettings> Settings => Set<SiteSettings>();
    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        // This application uses passwords, not passkeys; omit the unused passkey/JSON model.
        builder.Ignore<IdentityUserPasskey<Guid>>();
        builder.Ignore<IdentityPasskeyData>();
        builder.Entity<AppUser>().Property(u => u.FullName).HasMaxLength(200);
        builder.Entity<AppUser>().Property(u => u.EmployerName).HasMaxLength(200);
        builder.Entity<ManagedFile>().Property(f => f.Description).HasMaxLength(4000);
        builder.Entity<AppUserClaim>().Ignore(c => c.Id).HasKey(c => c.Key);
        builder.Entity<AppRoleClaim>().Ignore(c => c.Id).HasKey(c => c.Key);
        builder.Entity<AppUserRole>().HasKey(c => c.Id);
        builder.Entity<AppUserRole>().HasIndex(c => new { c.UserId, c.RoleId }).IsUnique();
        builder.Entity<AppUserLogin>().HasKey(c => c.Id);
        builder.Entity<AppUserLogin>().HasIndex(c => new { c.LoginProvider, c.ProviderKey }).IsUnique();
        builder.Entity<AppUserToken>().HasKey(c => c.Id);
        builder.Entity<AppUserToken>().HasIndex(c => new { c.UserId, c.LoginProvider, c.Name }).IsUnique();
        builder.Entity<AppUserLogin>().Property(c => c.LoginProvider).HasMaxLength(128);
        builder.Entity<AppUserLogin>().Property(c => c.ProviderKey).HasMaxLength(128);
        builder.Entity<AppUserToken>().Property(c => c.LoginProvider).HasMaxLength(128);
        builder.Entity<AppUserToken>().Property(c => c.Name).HasMaxLength(128);
        var names = new Dictionary<Type, string>
        {
            [typeof(AppUser)] = "User", [typeof(AppRole)] = "Role",
            [typeof(AppUserClaim)] = "UserClaim", [typeof(AppRoleClaim)] = "RoleClaim",
            [typeof(AppUserRole)] = "UserRole", [typeof(AppUserLogin)] = "UserLogin",
            [typeof(AppUserToken)] = "UserToken", [typeof(ManagedFile)] = "File", [typeof(SiteSettings)] = "Setting"
        };
        foreach (var entity in builder.Model.GetEntityTypes())
        {
            var table = names[entity.ClrType];
            entity.SetTableName(table);
            foreach (var property in entity.GetProperties())
            {
                property.SetColumnName(table + (property.Name == "Key" ? "Id" : property.Name));
                if (property.ClrType == typeof(Guid))
                {
                    property.SetValueConverter(new Microsoft.EntityFrameworkCore.Storage.ValueConversion.ValueConverter<Guid, string>(v => v.ToString("N"), v => Guid.Parse(v)));
                    property.SetColumnType("char(32)");
                }
            }
        }
    }
}

public class ManagedFile
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public string Name { get; set; } = "";
    public string Description { get; set; } = "";
    public long Size { get; set; }
    public bool IsPublic { get; set; }
    public bool Listed { get; set; }
    public string Roles { get; set; } = "Member";
    public DateTime UploadedUtc { get; set; } = DateTime.UtcNow;
    public bool Allows(System.Security.Claims.ClaimsPrincipal user) =>
        IsPublic || user.IsInRole("Administrator") || Roles.Split(',', StringSplitOptions.RemoveEmptyEntries).Any(user.IsInRole);
}

public class SiteSettings
{
    public Guid Id { get; set; } = Guid.NewGuid();
    public bool RegistrationEnabled { get; set; } = true;
    public int SchemaVersion { get; set; } = 3;
}

public record Storage(string Root)
{
    public string Files => Path.Combine(Root, "uploads");
    public string File(Guid id) => Path.Combine(Files, id.ToString("N"));
}

public class AccountInput
{
    public string FullName { get; set; } = "";
    public string? EmployerName { get; set; } = "";
    public string Email { get; set; } = "";
    public string Username { get; set; } = "";
    public string Password { get; set; } = "";
    public string ConfirmPassword { get; set; } = "";
    public string Token { get; set; } = "";
}
public record AdminModel(bool RegistrationEnabled, List<AppUser> Users, Dictionary<Guid, IList<string>> UserRoles, List<string> Roles);

public class RecoveryInput
{
    [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.EmailAddress, System.ComponentModel.DataAnnotations.StringLength(256)]
    public string Email { get; set; } = "";
}

public class ProfileInput
{
    [System.ComponentModel.DataAnnotations.Required, System.ComponentModel.DataAnnotations.StringLength(200)]
    public string FullName { get; set; } = "";
    [System.ComponentModel.DataAnnotations.StringLength(200)]
    public string? EmployerName { get; set; } = "";
}
