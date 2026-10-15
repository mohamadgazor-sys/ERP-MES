using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.Json.Serialization;
using ERP.MES.Application.Security;

namespace ERP.MES.Infrastructure.Security;

/// <summary>
/// Reads the authentication settings (Milestone 1 plan 6.1) from <c>appsettings.json</c> (non-secret defaults, committed) and then, if present,
/// <c>appsettings.Local.json</c> (ignored by Git), using only the base library. The settings live in an "Authentication" section. An unknown setting name is an
/// error rather than being ignored (a misspelt security setting must not silently fall back to a default), and a value below its safety floor stops start-up.
/// </summary>
public static class AuthenticationPolicyFile
{
    public const string SectionName = "Authentication";

    private static readonly JsonSerializerOptions Options = new()
    {
        PropertyNameCaseInsensitive = true,
        UnmappedMemberHandling = JsonUnmappedMemberHandling.Disallow,
        ReadCommentHandling = JsonCommentHandling.Skip,
        AllowTrailingCommas = true,
    };

    /// <summary>Later files override earlier ones, setting by setting. Missing files are skipped. Throws <see cref="InvalidAuthenticationPolicyException"/> for an invalid result.</summary>
    public static AuthenticationPolicy Load(params string[] paths)
    {
        var merged = new JsonObject();
        foreach (var path in paths.Where(File.Exists))
        {
            var root = JsonNode.Parse(File.ReadAllText(path), documentOptions: new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true }) as JsonObject
                ?? throw new InvalidOperationException("The settings file '" + path + "' is not a JSON object.");
            if (root[SectionName] is not JsonObject section)
            {
                continue;
            }

            foreach (var (name, value) in section.ToList())
            {
                section.Remove(name);
                merged[name] = value;
            }
        }

        var policy = merged.Deserialize<AuthenticationPolicy>(Options) ?? AuthenticationPolicy.Default;
        var problems = policy.Validate();
        return problems.Count == 0 ? policy : throw new InvalidAuthenticationPolicyException(problems);
    }
}
