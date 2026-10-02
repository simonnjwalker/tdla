using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.FileProviders;
using System.Threading.RateLimiting;
using Tdla.Web;

var builder = WebApplication.CreateBuilder(args);
var privateConfig = Environment.GetEnvironmentVariable("TDLA_CONFIG_FILE");
if (!string.IsNullOrWhiteSpace(privateConfig)) builder.Configuration.AddJsonFile(privateConfig, optional: false).AddEnvironmentVariables();
var legacyMailPath = builder.Configuration["Smtp:LegacyConfigPath"];
if (string.IsNullOrWhiteSpace(legacyMailPath))
{
    legacyMailPath = Path.Combine(builder.Environment.ContentRootPath, "php", "info.php");
    if (!File.Exists(legacyMailPath) && builder.Environment.IsDevelopment())
        legacyMailPath = Path.GetFullPath(Path.Combine(builder.Environment.ContentRootPath, "../../php/info.php"));
}
if (File.Exists(legacyMailPath))
{
    builder.Configuration.AddInMemoryCollection(LegacyMailConfiguration.Read(legacyMailPath));
    if (!string.IsNullOrWhiteSpace(privateConfig)) builder.Configuration.AddJsonFile(privateConfig, optional: false);
    builder.Configuration.AddEnvironmentVariables();
}
else if (!string.IsNullOrWhiteSpace(builder.Configuration["Smtp:LegacyConfigPath"]))
    throw new InvalidOperationException("Smtp:LegacyConfigPath does not point to an existing file.");
var dataPath = builder.Configuration["Storage:Path"];
if (string.IsNullOrWhiteSpace(dataPath))
{
    if (!builder.Environment.IsDevelopment())
        throw new InvalidOperationException("Set Storage__Path to a persistent directory outside the published website.");
    dataPath = Path.Combine(builder.Environment.ContentRootPath, "App_Data");
}
dataPath = Path.GetFullPath(dataPath);
var websitePath = Path.GetFullPath(AppContext.BaseDirectory).TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar;
if (!builder.Environment.IsDevelopment() && dataPath.StartsWith(websitePath, StringComparison.OrdinalIgnoreCase))
    throw new InvalidOperationException("Storage:Path must be outside the published website directory.");
var storage = new Storage(dataPath);
Directory.CreateDirectory(storage.Files);
builder.Services.AddSingleton(storage);
builder.Services.AddScoped<MailService>();
builder.Services.AddDataProtection().SetApplicationName("Tdla.Web").PersistKeysToFileSystem(new DirectoryInfo(Path.Combine(dataPath, "keys")));
var connectionString = builder.Configuration.GetConnectionString("Default")
    ?? throw new InvalidOperationException("Set ConnectionStrings__Default to your MySQL connection string.");
builder.Services.AddDbContext<AppDb>(o => o.UseMySQL(connectionString));
builder.Services.AddIdentity<AppUser, AppRole>(o =>
{
    o.User.RequireUniqueEmail = true;
    o.Password.RequiredLength = 10;
    o.Lockout.MaxFailedAccessAttempts = 5;
    o.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(15);
}).AddEntityFrameworkStores<AppDb>().AddDefaultTokenProviders();
builder.Services.AddScoped<IUserStore<AppUser>, AppUserStore>();
builder.Services.Configure<DataProtectionTokenProviderOptions>(o => o.TokenLifespan = TimeSpan.FromHours(24));
builder.Services.ConfigureApplicationCookie(o =>
{
    o.LoginPath = "/account/login";
    o.AccessDeniedPath = "/account/denied";
    o.Cookie.HttpOnly = true;
    o.Cookie.SameSite = SameSiteMode.Lax;
    o.Cookie.SecurePolicy = builder.Environment.IsDevelopment() ? CookieSecurePolicy.SameAsRequest : CookieSecurePolicy.Always;
    o.ExpireTimeSpan = TimeSpan.FromHours(8);
});
builder.Services.Configure<SecurityStampValidatorOptions>(o => o.ValidationInterval = TimeSpan.Zero);
builder.Services.AddControllersWithViews(o => o.Filters.Add(new AutoValidateAntiforgeryTokenAttribute()));
builder.Services.AddRateLimiter(o =>
{
    o.RejectionStatusCode = 429;
    o.AddPolicy("account", context => RateLimitPartition.GetFixedWindowLimiter(
        context.Connection.RemoteIpAddress?.ToString() ?? "unknown", _ => new FixedWindowRateLimiterOptions
        { PermitLimit = 20, Window = TimeSpan.FromMinutes(1), QueueLimit = 0 }));
});
var app = builder.Build();
using (var scope = app.Services.CreateScope())
{
    var db = scope.ServiceProvider.GetRequiredService<AppDb>();
    await db.Database.EnsureCreatedAsync();
    await SchemaUpgrade.ApplyAsync(db);
    var roles = scope.ServiceProvider.GetRequiredService<RoleManager<AppRole>>();
    var legacyAdmin = await roles.FindByNameAsync("Admin");
    if (legacyAdmin is not null && !await roles.RoleExistsAsync("Administrator"))
    {
        legacyAdmin.Name = "Administrator";
        Check(await roles.UpdateAsync(legacyAdmin));
    }
    foreach (var role in new[] { "Member", "Administrator", "President", "Vice-President", "Treasurer", "Secretary" })
        if (!await roles.RoleExistsAsync(role)) Check(await roles.CreateAsync(new AppRole(role)));
    var users = scope.ServiceProvider.GetRequiredService<UserManager<AppUser>>();
    const string adminEmail = "simonwalker0@gmail.com";
    var admin = await users.FindByEmailAsync(adminEmail);
    if (admin is null)
    {
        admin = new AppUser { UserName = adminEmail, Email = adminEmail, Verified = true };
        Check(await users.CreateAsync(admin)); // Deliberately no password; never permits a blank-password login.
        Check(await users.AddToRoleAsync(admin, "Administrator"));
    }
    if (legacyAdmin is not null)
    {
        if (legacyAdmin.Name == "Admin")
        {
            foreach (var account in await users.GetUsersInRoleAsync("Admin"))
            {
                if (!await users.IsInRoleAsync(account, "Administrator")) Check(await users.AddToRoleAsync(account, "Administrator"));
                Check(await users.RemoveFromRoleAsync(account, "Admin"));
                Check(await users.UpdateSecurityStampAsync(account));
            }
            Check(await roles.DeleteAsync(legacyAdmin));
        }
        foreach (var file in await db.Files.ToListAsync())
            file.Roles = string.Join(',', file.Roles.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(r => r == "Admin" ? "Administrator" : r).Distinct());
        admin.Verified = true;
        await db.SaveChangesAsync();
    }
    if (!await db.Settings.AnyAsync()) { db.Settings.Add(new SiteSettings()); await db.SaveChangesAsync(); }
    var bootstrapPassword = builder.Configuration["Bootstrap:AdminPassword"];
    if (!await users.HasPasswordAsync(admin) && !string.IsNullOrEmpty(bootstrapPassword))
        Check(await users.AddPasswordAsync(admin, bootstrapPassword));
    if (args.Contains("--set-admin-password"))
    {
        if (string.IsNullOrEmpty(bootstrapPassword)) throw new InvalidOperationException("Set Bootstrap__AdminPassword first.");
        Check(await users.ResetPasswordAsync(admin, await users.GeneratePasswordResetTokenAsync(admin), bootstrapPassword));
        Console.WriteLine("Administrator password updated.");
        return;
    }
    if (args.Contains("--initialize")) { Console.WriteLine("Database initialized."); return; }
    if (args.Contains("--setup-admin"))
    {
        var token = await users.GeneratePasswordResetTokenAsync(admin);
        var baseUrl = builder.Configuration["Site:PublicUrl"]!.TrimEnd('/');
        Console.WriteLine($"Private admin setup link (expires in 24 hours):\n{baseUrl}/account/setup?email={Uri.EscapeDataString(adminEmail)}&token={Uri.EscapeDataString(token)}");
        return;
    }
}
if (!app.Environment.IsDevelopment()) { app.UseExceptionHandler("/error"); app.UseHsts(); app.UseHttpsRedirection(); }
app.Use(async (context, next) =>
{
    context.Response.Headers.XContentTypeOptions = "nosniff";
    context.Response.Headers["Referrer-Policy"] = "same-origin";
    context.Response.Headers["X-Frame-Options"] = "SAMEORIGIN";
    if (context.Request.Path.StartsWithSegments("/account") || context.Request.Path.StartsWithSegments("/admin") || context.Request.Path.StartsWithSegments("/api") || context.Request.Path.StartsWithSegments("/files"))
        context.Response.Headers.CacheControl = "no-cache, no-store";
    await next();
});
// Serve only the explicit static asset directories, never the repository or data directory.
var publishedRoot = Path.Combine(AppContext.BaseDirectory, "wwwroot");
app.UseDefaultFiles(new DefaultFilesOptions { FileProvider = new PhysicalFileProvider(publishedRoot) });
app.UseStaticFiles(new StaticFileOptions { FileProvider = new PhysicalFileProvider(publishedRoot) });
app.UseRouting();
app.UseAuthentication();
app.UseAuthorization();
app.UseRateLimiter();
app.MapControllers();
app.MapGet("/api/session", (HttpContext context, Microsoft.AspNetCore.Antiforgery.IAntiforgery antiforgery) =>
{
    var user = context.User;
    return Results.Json(new { authenticated = user.Identity?.IsAuthenticated == true, admin = user.IsInRole("Administrator"), logoutToken = user.Identity?.IsAuthenticated == true ? antiforgery.GetAndStoreTokens(context).RequestToken : null });
});
app.MapGet("/health", () => Results.Ok(new { status = "ok" }));
app.Run();

static void Check(IdentityResult result)
{
    if (!result.Succeeded) throw new InvalidOperationException(string.Join(" ", result.Errors.Select(e => e.Description)));
}
