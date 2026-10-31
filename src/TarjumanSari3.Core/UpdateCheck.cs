namespace TarjumanSari3.Core;

public static class UpdateCheck
{
    /// <summary>True when a release tag such as "v1.0.1" is newer than the running version.</summary>
    public static bool IsNewer(string? tag, Version? current)
    {
        if (current == null || string.IsNullOrWhiteSpace(tag)) return false;
        var text = tag.Trim().TrimStart('v', 'V');
        var dash = text.IndexOfAny(new[] { '-', '+' }); // ignore "-beta" style suffixes
        if (dash >= 0) text = text[..dash];
        if (!Version.TryParse(text, out var latest)) return false;
        // "1.0.1" has no revision (-1) while the assembly reports 1.0.1.0; compare them on equal footing
        static Version Norm(Version v) => new(v.Major, v.Minor, Math.Max(v.Build, 0), Math.Max(v.Revision, 0));
        return Norm(latest) > Norm(current);
    }
}