using System.Net.Mail;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace Tdla.Web.Controllers;

public class ContactController(IConfiguration configuration, MailService mail) : Controller
{
    // Keeps the existing static HTML form action working, entirely in C#.
    [HttpPost("/php/contact.php"), IgnoreAntiforgeryToken, EnableRateLimiting("account"), RequestSizeLimit(32768)]
    public async Task<IActionResult> Send(string name = "", string email = "", string number = "", string subject = "", string message = "")
    {
        var origin = Request.Headers.Origin.ToString();
        if (!Uri.TryCreate(configuration["Site:PublicUrl"], UriKind.Absolute, out var site)) return StatusCode(503);
        if (origin.Length > 0 && (!Uri.TryCreate(origin, UriKind.Absolute, out var source) || source.Authority != site.Authority)) return StatusCode(403);
        if (name.Length is < 1 or > 200 || subject.Length is < 1 or > 200 || number.Length > 100 || message.Length is < 1 or > 10000 ||
            !MailAddress.TryCreate(email, out var replyTo) || email.Contains('\r') || email.Contains('\n') || subject.Contains('\r') || subject.Contains('\n'))
        { Response.StatusCode = 422; return View("Result", "Please enter a name, valid email address, subject and message within the permitted lengths."); }
        var smtp = configuration.GetSection("Smtp");
        if (string.IsNullOrWhiteSpace(smtp["To"]))
        { Response.StatusCode = 503; return View("Result", "The contact service is not yet configured. Please contact secretary.tdla@gmail.com directly."); }
        if (await mail.SendAsync(smtp["To"]!, "TDLA contact: " + subject, $"Name: {name}\nEmail: {email}\nPhone: {number}\n\n{message}", replyTo.Address))
            return View("Result", "Thank you. Your message has been sent.");
        Response.StatusCode = 503;
        return View("Result", "Your message could not be sent. Please try again later or contact secretary.tdla@gmail.com directly.");
    }
}
