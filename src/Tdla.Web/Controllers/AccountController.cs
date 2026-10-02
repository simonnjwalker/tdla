using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.EntityFrameworkCore;

namespace Tdla.Web.Controllers;

[Route("account")]
public class AccountController(UserManager<AppUser> users, SignInManager<AppUser> signIn, AppDb db, MailService mail, IConfiguration configuration) : Controller
{
    [HttpGet("login")]
    public IActionResult Login() => View(new AccountInput());

    [HttpPost("login"), EnableRateLimiting("account")]
    public async Task<IActionResult> Login(AccountInput input)
    {
        if (string.IsNullOrWhiteSpace(input.Username) || string.IsNullOrEmpty(input.Password))
            ModelState.AddModelError("", "Enter your username and password.");
        if (ModelState.IsValid)
        {
            var user = await users.FindByNameAsync(input.Username.Trim());
            var result = user is null ? Microsoft.AspNetCore.Identity.SignInResult.Failed :
                await signIn.PasswordSignInAsync(user, input.Password, false, lockoutOnFailure: true);
            if (result.Succeeded) return Redirect("/files");
            ModelState.AddModelError("", "Unable to sign in. Check your details, or try again later if your account is locked.");
        }
        return View(input);
    }

    [HttpGet("register")]
    public async Task<IActionResult> Register() => (await db.Settings.SingleAsync()).RegistrationEnabled ? View(new AccountInput()) : View("RegistrationClosed");

    [HttpPost("register"), EnableRateLimiting("account")]
    public async Task<IActionResult> Register(AccountInput input)
    {
        input.EmployerName ??= "";
        if (!(await db.Settings.SingleAsync()).RegistrationEnabled) return StatusCode(403);
        if (!System.Net.Mail.MailAddress.TryCreate(input.Email, out var email) || email.Address != input.Email.Trim())
            ModelState.AddModelError("", "Enter a valid email address.");
        if (string.IsNullOrWhiteSpace(input.FullName) || input.FullName.Length > 200) ModelState.AddModelError("FullName", "Enter your full name (up to 200 characters).");
        if (input.EmployerName.Length > 200) ModelState.AddModelError("EmployerName", "Firm/employer name must be at most 200 characters.");
        if (input.Password != input.ConfirmPassword) ModelState.AddModelError("", "Passwords do not match.");
        if (ModelState.IsValid)
        {
            var user = new AppUser { UserName = input.Username.Trim(), Email = input.Email.Trim(), FullName = input.FullName.Trim(), EmployerName = input.EmployerName.Trim(), Verified = false };
            var result = await users.CreateAsync(user, input.Password);
            if (result.Succeeded)
            {
                // New public registrations have no private-file role; an admin must grant membership.
                await signIn.SignInAsync(user, false);
                return Redirect("/account/registered");
            }
            foreach (var error in result.Errors) ModelState.AddModelError("", error.Description);
        }
        return View(input);
    }

    [Authorize, HttpGet("registered")]
    public async Task<IActionResult> Registered()
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        if (user.Verified || User.IsInRole("Administrator")) return Redirect("/files");
        return View();
    }

    [HttpGet("setup")]
    public IActionResult Setup(string email = "", string token = "") => View(new AccountInput { Email = email, Token = token });

    [HttpPost("setup"), EnableRateLimiting("account")]
    public async Task<IActionResult> Setup(AccountInput input)
    {
        if (input.Password != input.ConfirmPassword) ModelState.AddModelError("", "Passwords do not match.");
        var user = await users.FindByEmailAsync(input.Email);
        if (user is null) ModelState.AddModelError("", "Invalid or expired setup link. Ask an administrator for a new link.");
        if (ModelState.IsValid && user is not null)
        {
            var result = await users.ResetPasswordAsync(user, input.Token, input.Password);
            if (result.Succeeded)
            {
                await mail.SendAsync(user.Email!, "TDLA password changed", "Your TDLA account password has been changed. If you did not make this change, contact the TDLA immediately.\n\nSign in: " + configuration["Site:PublicUrl"]!.TrimEnd('/') + "/account/login");
                TempData["Message"] = "Password saved. Sign in with your username (your email address for invited accounts).";
                return Redirect("/account/login");
            }
            foreach (var error in result.Errors) ModelState.AddModelError("", error.Description);
        }
        return View(input);
    }

    [HttpGet("forgot-username")]
    public IActionResult ForgotUsername() => View(new RecoveryInput());

    [HttpPost("forgot-username"), EnableRateLimiting("account")]
    public async Task<IActionResult> ForgotUsername(RecoveryInput input)
    {
        if (!ModelState.IsValid) return View(input);
        var user = await users.FindByEmailAsync(input.Email.Trim());
        if (user is not null)
            await mail.SendAsync(user.Email!, "Your TDLA username", $"You requested a reminder of your TDLA username.\n\nUsername: {user.UserName}\n\nSign in: {configuration["Site:PublicUrl"]!.TrimEnd('/')}/account/login\n\nIf you did not request this email, you can ignore it.");
        return View("RecoverySent");
    }

    [HttpGet("forgot-password")]
    public IActionResult ForgotPassword() => View(new RecoveryInput());

    [HttpPost("forgot-password"), EnableRateLimiting("account")]
    public async Task<IActionResult> ForgotPassword(RecoveryInput input)
    {
        if (!ModelState.IsValid) return View(input);
        var user = await users.FindByEmailAsync(input.Email.Trim());
        if (user is not null)
        {
            var token = await users.GeneratePasswordResetTokenAsync(user);
            var url = $"{configuration["Site:PublicUrl"]!.TrimEnd('/')}/account/setup?email={Uri.EscapeDataString(user.Email!)}&token={Uri.EscapeDataString(token)}";
            await mail.SendAsync(user.Email!, "Reset your TDLA password", $"You requested a password reset. Confirm this request by opening the private link below and choosing a new password:\n\n{url}\n\nThis link expires in 24 hours and can only be used once. Your password will not change until you submit a new one. If you did not request this, ignore this email.");
        }
        return View("RecoverySent");
    }

    [Authorize, HttpGet("profile")]
    public async Task<IActionResult> Profile()
    {
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        return View(new ProfileInput { FullName = user.FullName, EmployerName = user.EmployerName });
    }

    [Authorize, HttpPost("profile")]
    public async Task<IActionResult> Profile(ProfileInput input)
    {
        if (!ModelState.IsValid) return View(input);
        var user = await users.GetUserAsync(User);
        if (user is null) return Challenge();
        user.FullName = input.FullName.Trim();
        user.EmployerName = input.EmployerName?.Trim() ?? "";
        var result = await users.UpdateAsync(user);
        if (!result.Succeeded) { foreach (var error in result.Errors) ModelState.AddModelError("", error.Description); return View(input); }
        TempData["Message"] = "Your details have been saved.";
        return RedirectToAction(nameof(Profile));
    }

    [Authorize, HttpGet("logout")]
    public IActionResult LogoutConfirmation() => View("Logout");

    [Authorize, HttpPost("logout")]
    public async Task<IActionResult> Logout() { await signIn.SignOutAsync(); return Redirect("/"); }

    [HttpGet("denied")]
    public IActionResult Denied() { Response.StatusCode = 403; return View(); }

    [HttpGet("/error")]
    public IActionResult Error() { Response.StatusCode = 500; return View("Error"); }
}
