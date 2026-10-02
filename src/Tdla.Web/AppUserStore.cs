using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;

namespace Tdla.Web;

// Identity's default role/token lookups assume composite primary keys. Our schema uses GUID primary keys.
public class AppUserStore(AppDb db) : UserStore<AppUser, AppRole, AppDb, Guid, AppUserClaim, AppUserRole, AppUserLogin, AppUserToken, AppRoleClaim>(db)
{
    public override string? ConvertIdToString(Guid id) => id.ToString("N");

    protected override Task<AppUserRole?> FindUserRoleAsync(Guid userId, Guid roleId, CancellationToken cancellationToken) =>
        Context.Set<AppUserRole>().SingleOrDefaultAsync(r => r.UserId == userId && r.RoleId == roleId, cancellationToken);

    protected override Task<AppUserToken?> FindTokenAsync(AppUser user, string loginProvider, string name, CancellationToken cancellationToken) =>
        Context.Set<AppUserToken>().SingleOrDefaultAsync(t => t.UserId == user.Id && t.LoginProvider == loginProvider && t.Name == name, cancellationToken);
}
