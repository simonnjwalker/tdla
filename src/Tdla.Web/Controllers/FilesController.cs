using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;

namespace Tdla.Web.Controllers;

[Route("files")]
public class FilesController(AppDb db, Storage storage, RoleManager<AppRole> roles, UserManager<AppUser> users) : Controller
{
    [HttpGet("")]
    public async Task<IActionResult> Index()
    {
        ViewBag.AwaitingVerification = await AwaitingVerification();
        ViewBag.Roles = await roles.Roles.Select(r => r.Name!).OrderBy(r => r).ToListAsync();
        if (ViewBag.AwaitingVerification) return View(new List<ManagedFile>());
        var files = await db.Files.OrderByDescending(f => f.UploadedUtc).ToListAsync();
        return View(files.Where(f => User.IsInRole("Administrator") || (f.Listed && f.Allows(User))).ToList());
    }

    [HttpGet("{id:guid}/download")]
    public async Task<IActionResult> Download(Guid id)
    {
        if (await AwaitingVerification()) return NotFound();
        var file = await db.Files.FindAsync(id);
        if (file is null || !file.Allows(User) || !System.IO.File.Exists(storage.File(id))) return NotFound();
        return PhysicalFile(storage.File(id), "application/octet-stream", file.Name, enableRangeProcessing: false);
    }

    [Authorize(Roles = "Administrator"), HttpPost("upload"), RequestSizeLimit(26_214_400), RequestFormLimits(MultipartBodyLengthLimit = 26_214_400)]
    public async Task<IActionResult> Upload(IFormFile? upload, bool isPublic, bool listed, string[] allowedRoles, string? description)
    {
        description ??= "";
        if (description.Length > 4000) return BadRequest("Description must be at most 4,000 characters.");
        if (upload is null || upload.Length == 0 || upload.Length > 25_000_000)
            return Problem("Select a non-empty file up to 25 MB.", statusCode: 400);
        var selected = await ValidRoles(allowedRoles);
        if (selected is null) return BadRequest("Unknown role.");
        var file = new ManagedFile { Name = Path.GetFileName(upload.FileName.Replace('\\', '/')), Description = description.Trim(), Size = upload.Length, IsPublic = isPublic, Listed = listed, Roles = selected };
        if (string.IsNullOrWhiteSpace(file.Name) || file.Name.Length > 200 || file.Name.Any(char.IsControl)) return BadRequest("Invalid filename.");
        try
        {
            await using (var output = new FileStream(storage.File(file.Id), FileMode.CreateNew)) await upload.CopyToAsync(output);
            db.Files.Add(file);
            await db.SaveChangesAsync();
        }
        catch { System.IO.File.Delete(storage.File(file.Id)); throw; }
        TempData["Message"] = "File uploaded.";
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = "Administrator"), HttpPost("{id:guid}/description")]
    public async Task<IActionResult> Description(Guid id, string? description)
    {
        description ??= "";
        if (description.Length > 4000) return BadRequest("Description must be at most 4,000 characters.");
        var file = await db.Files.FindAsync(id);
        if (file is null) return NotFound();
        file.Description = description.Trim();
        await db.SaveChangesAsync();
        TempData["Message"] = "File description updated.";
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = "Administrator"), HttpPost("{id:guid}/permissions")]
    public async Task<IActionResult> Permissions(Guid id, bool isPublic, bool listed, string[] allowedRoles)
    {
        var file = await db.Files.FindAsync(id);
        if (file is null) return NotFound();
        var selected = await ValidRoles(allowedRoles);
        if (selected is null) return BadRequest("Unknown role.");
        file.IsPublic = isPublic; file.Listed = listed; file.Roles = selected;
        await db.SaveChangesAsync();
        TempData["Message"] = "File permissions updated.";
        return RedirectToAction(nameof(Index));
    }

    [Authorize(Roles = "Administrator"), HttpPost("{id:guid}/delete")]
    public async Task<IActionResult> Delete(Guid id)
    {
        var file = await db.Files.FindAsync(id);
        if (file is null) return NotFound();
        db.Files.Remove(file);
        await db.SaveChangesAsync(); // Revoke access before removing bytes.
        System.IO.File.Delete(storage.File(id));
        TempData["Message"] = "File deleted.";
        return RedirectToAction(nameof(Index));
    }

    private async Task<string?> ValidRoles(string[] selected)
    {
        var available = await roles.Roles.Select(r => r.Name!).ToListAsync();
        return selected.All(available.Contains) ? string.Join(',', selected.Distinct()) : null;
    }

    private async Task<bool> AwaitingVerification() =>
        User.Identity?.IsAuthenticated == true && !User.IsInRole("Administrator") &&
        (await users.GetUserAsync(User))?.Verified != true;
}
