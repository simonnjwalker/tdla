using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using System.Net.Mail;

namespace Tdla.Web.Controllers;

[Authorize(Roles = "Administrator"), Route("admin")]
public class AdminController(AppDb db, UserManager<AppUser> users, RoleManager<AppRole> roles, IConfiguration configuration) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        var accounts = await users.Users.OrderBy(u => u.Email).ToListAsync();
        var memberships = new Dictionary<Guid, IList<string>>();
        foreach (var account in accounts) memberships[account.Id] = await users.GetRolesAsync(account);
        return View(new AdminModel((await db.Settings.SingleAsync()).RegistrationEnabled, accounts, memberships, await roles.Roles.Select(r => r.Name!).OrderBy(r => r).ToListAsync()));
    }

    [HttpPost("registration")]
    public async Task<IActionResult> Registration(bool enabled)
    {
        (await db.Settings.SingleAsync()).RegistrationEnabled = enabled;
        await db.SaveChangesAsync();
        TempData["Message"] = enabled ? "Public registration enabled." : "Public registration disabled.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("invite")]
    public async Task<IActionResult> Invite(string emails = "")
    {
        var addresses = emails.Split(new[] { ',', ';', '\n', '\r', ' ', '\t' }, StringSplitOptions.RemoveEmptyEntries).Distinct(StringComparer.OrdinalIgnoreCase).ToArray();
        if (addresses.Length is < 1 or > 100) return BadRequest("Enter between 1 and 100 email addresses.");
        if (addresses.Any(e => !MailAddress.TryCreate(e, out var address) || address.Address != e)) return BadRequest("One or more email addresses are invalid. No accounts were created.");
        var results = new List<InviteResult>();
        foreach (var email in addresses)
        {
            var existing = await users.FindByEmailAsync(email);
            if (existing is not null) { results.Add(new(email, null, "Already exists; use its password setup button to issue a new link.")); continue; }
            var user = new AppUser { Email = email, UserName = email };
            var created = await users.CreateAsync(user);
            if (!created.Succeeded) { results.Add(new(email, null, string.Join(" ", created.Errors.Select(e => e.Description)))); continue; }
            var assigned = await users.AddToRoleAsync(user, "Member");
            if (!assigned.Succeeded) { results.Add(new(email, null, "Created, but membership assignment failed. Assign roles on the accounts page.")); continue; }
            results.Add(new(email, await SetupUrl(user), "Created. Share this private link directly with the account owner."));
        }
        return View("Invitations", results);
    }

    [HttpPost("{id:guid}/setup")]
    public async Task<IActionResult> Setup(string id)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null) return NotFound();
        return View("Invitations", new List<InviteResult> { new(user.Email!, await SetupUrl(user), "Share this private password setup/reset link directly with the account owner.") });
    }

    [HttpPost("role")]
    public async Task<IActionResult> CreateRole(string name = "")
    {
        name = name.Trim();
        if (!System.Text.RegularExpressions.Regex.IsMatch(name, "^[A-Za-z][A-Za-z0-9_-]{0,39}$")) return BadRequest("Use 1–40 letters, digits, hyphens or underscores, starting with a letter.");
        var result = await roles.CreateAsync(new AppRole(name));
        TempData["Message"] = result.Succeeded ? "Role created." : string.Join(" ", result.Errors.Select(e => e.Description));
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id:guid}/profile")]
    public async Task<IActionResult> Profile(string id, string fullName = "", string employerName = "", bool verified = false)
    {
        fullName ??= "";
        employerName ??= "";
        if (fullName.Length > 200 || employerName.Length > 200) return BadRequest("Names must be at most 200 characters.");
        var user = await users.FindByIdAsync(id);
        if (user is null) return NotFound();
        user.FullName = fullName.Trim();
        user.EmployerName = employerName.Trim();
        user.Verified = verified;
        var result = await users.UpdateAsync(user);
        if (!result.Succeeded) return BadRequest("The account could not be updated. Please reload and try again.");
        TempData["Message"] = "Account details and verification saved.";
        return RedirectToAction(nameof(Index));
    }

    [HttpPost("{id:guid}/roles")]
    public async Task<IActionResult> UpdateRoles(string id, string[] selectedRoles)
    {
        var user = await users.FindByIdAsync(id);
        if (user is null) return NotFound();
        if (user.Email == "simonwalker0@gmail.com" && !selectedRoles.Contains("Administrator")) return BadRequest("The initial administrator must retain the Administrator role.");
        if (Guid.TryParse(users.GetUserId(User), out var signedInId) && signedInId == user.Id && !selectedRoles.Contains("Administrator")) return BadRequest("You cannot remove your own administrator access.");
        var available = await roles.Roles.Select(r => r.Name!).ToListAsync();
        if (selectedRoles.Any(r => !available.Contains(r))) return BadRequest("Unknown role.");
        await using var transaction = await db.Database.BeginTransactionAsync();
        var current = await users.GetRolesAsync(user);
        var removed = await users.RemoveFromRolesAsync(user, current.Except(selectedRoles));
        if (!removed.Succeeded) return BadRequest("Unable to update roles.");
        var added = await users.AddToRolesAsync(user, selectedRoles.Except(current));
        if (!added.Succeeded) return BadRequest("Unable to update roles.");
        await users.UpdateSecurityStampAsync(user);
        await transaction.CommitAsync();
        TempData["Message"] = "Roles updated. The account must sign in again.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<string> SetupUrl(AppUser user) =>
        $"{configuration["Site:PublicUrl"]!.TrimEnd('/')}/account/setup?email={Uri.EscapeDataString(user.Email!)}&token={Uri.EscapeDataString(await users.GeneratePasswordResetTokenAsync(user))}";
}

public record InviteResult(string Email, string? Url, string Message);
