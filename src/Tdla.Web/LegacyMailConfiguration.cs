using System.Text.RegularExpressions;

namespace Tdla.Web;

public static class LegacyMailConfiguration
{
    private static readonly Dictionary<string, string> Names = new()
    {
        ["SMTP_HOST"] = "Host", ["SMTP_PORT"] = "Port", ["SMTP_USER"] = "Username",
        ["SMTP_PASS"] = "Password", ["SMTP_SECURE"] = "Secure", ["MAIL_FROM"] = "From",
        ["MAIL_FROM_NAME"] = "FromName", ["MAIL_TO"] = "To", ["MAIL_TO_NAME"] = "ToName"
    };

    public static Dictionary<string, string?> Read(string path)
    {
        var text = File.ReadAllText(path);
        var result = new Dictionary<string, string?>();
        // Read literal PHP constants only. Never execute PHP or evaluate arbitrary expressions.
        var pattern = "(?m)^\\s*const\\s+(SMTP_[A-Z_]+|MAIL_[A-Z_]+)\\s*=\\s*(?:'(?<single>(?:\\\\.|[^'\\\\])*)'|\"(?<double>(?:\\\\.|[^\"\\\\])*)\"|(?<number>[0-9]+))\\s*;";
        foreach (Match match in Regex.Matches(text, pattern, RegexOptions.None, TimeSpan.FromSeconds(1)))
        {
            if (!Names.TryGetValue(match.Groups[1].Value, out var key)) continue;
            var value = match.Groups["single"].Success ? match.Groups["single"].Value.Replace("\\'", "'").Replace("\\\\", "\\") :
                match.Groups["double"].Success ? match.Groups["double"].Value.Replace("\\\"", "\"").Replace("\\$", "$").Replace("\\\\", "\\") : match.Groups["number"].Value;
            result["Smtp:" + key] = value;
        }
        if (!result.ContainsKey("Smtp:Host") || !result.ContainsKey("Smtp:Password"))
            throw new InvalidOperationException("The PHP mail configuration must contain literal SMTP_HOST and SMTP_PASS constants.");
        return result;
    }
}
