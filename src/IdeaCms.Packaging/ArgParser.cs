namespace IdeaCms.Packaging;

/// <summary>Minimal <c>--key value</c> argument parser (no external dependency).</summary>
public static class ArgParser
{
    public static Dictionary<string, string> Parse(ReadOnlySpan<string> args)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < args.Length; i++)
        {
            var a = args[i];
            if (!a.StartsWith("--", StringComparison.Ordinal)) continue;
            var key = a[2..];
            var val = i + 1 < args.Length && !args[i + 1].StartsWith("--", StringComparison.Ordinal)
                ? args[++i]
                : "true";
            map[key] = val;
        }
        return map;
    }
}
