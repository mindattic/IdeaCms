namespace IdeaCms.Core.Sites;

/// <summary>
/// The rule that turns a request's host into a site. Pure and DB-free so the matching itself is
/// testable without a database — the resolver on top of it only supplies candidate rows.
/// <para>
/// A <c>Site.HostBindings</c> value is a comma / semicolon / whitespace separated list, e.g.
/// <c>"mindattic.com, www.mindattic.com, *.mindattic.com"</c>. Matching is case-insensitive, ignores
/// a trailing dot, and ignores the port unless the binding names one — so a binding written for
/// production keeps working on <c>localhost:5199</c> without being rewritten, while
/// <c>localhost:5199</c> can still be bound explicitly when two sites share a hostname in dev.
/// </para>
/// </summary>
public static class HostBinding
{
    private static readonly char[] Separators = [',', ';', ' ', '\t', '\r', '\n'];

    /// <summary>How well a binding matched — the precedence order when several sites match.</summary>
    public enum MatchQuality
    {
        /// <summary>No binding matched.</summary>
        None = 0,
        /// <summary>A bare <c>*</c> catch-all binding.</summary>
        CatchAll = 1,
        /// <summary>A <c>*.example.com</c> wildcard.</summary>
        Wildcard = 2,
        /// <summary>The hostname matched and the binding named no port.</summary>
        Host = 3,
        /// <summary>Hostname AND port both matched an explicit binding.</summary>
        HostAndPort = 4,
    }

    /// <summary>Splits a stored HostBindings value into its individual bindings.</summary>
    public static IEnumerable<string> Split(string? bindings) =>
        (bindings ?? "")
            .Split(Separators, StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(Normalize)
            .Where(b => b.Length > 0);

    /// <summary>
    /// Scores <paramref name="requestHost"/> (as it arrived, e.g. <c>WWW.Example.com:5199</c>)
    /// against one site's HostBindings value. The best single binding wins.
    /// </summary>
    public static MatchQuality Match(string? bindings, string? requestHost)
    {
        var (host, port) = SplitHostPort(Normalize(requestHost));
        if (host.Length == 0) return MatchQuality.None;

        var best = MatchQuality.None;
        foreach (var binding in Split(bindings))
        {
            var quality = MatchOne(binding, host, port);
            if (quality > best) best = quality;
            if (best == MatchQuality.HostAndPort) break;   // nothing outranks it
        }
        return best;
    }

    private static MatchQuality MatchOne(string binding, string host, string port)
    {
        if (binding == "*") return MatchQuality.CatchAll;

        var (bindHost, bindPort) = SplitHostPort(binding);
        if (bindHost.Length == 0) return MatchQuality.None;

        // A binding that names a port must match it; one that does not is port-agnostic, so a
        // production binding keeps working against localhost:5199.
        if (bindPort.Length > 0 && !string.Equals(bindPort, port, StringComparison.Ordinal))
            return MatchQuality.None;

        if (bindHost.StartsWith("*.", StringComparison.Ordinal))
        {
            // "*.example.com" covers any subdomain (including a nested one) but NOT the apex — bind
            // the apex explicitly if you want it, so a wildcard can never silently claim the bare
            // domain another site owns.
            var suffix = bindHost[1..];                    // ".example.com"
            return host.EndsWith(suffix, StringComparison.Ordinal) && host.Length > suffix.Length
                ? MatchQuality.Wildcard
                : MatchQuality.None;
        }

        if (!string.Equals(bindHost, host, StringComparison.Ordinal)) return MatchQuality.None;
        return bindPort.Length > 0 ? MatchQuality.HostAndPort : MatchQuality.Host;
    }

    /// <summary>Lowercases, trims, drops a scheme/path if a whole URL was pasted in, and drops a trailing dot.</summary>
    public static string Normalize(string? value)
    {
        var v = (value ?? "").Trim().ToLowerInvariant();
        if (v.Length == 0) return "";

        // Tolerate a pasted URL — "https://mindattic.com/" is what someone copies out of a browser.
        var scheme = v.IndexOf("://", StringComparison.Ordinal);
        if (scheme >= 0) v = v[(scheme + 3)..];
        var slash = v.IndexOf('/');
        if (slash >= 0) v = v[..slash];

        return v.TrimEnd('.');
    }

    /// <summary>Splits "host:port" into its parts, leaving a bracketed IPv6 literal intact.</summary>
    public static (string Host, string Port) SplitHostPort(string value)
    {
        if (value.Length == 0) return ("", "");

        // "[::1]:5199" — the colons inside the brackets are part of the address, not a port.
        if (value[0] == '[')
        {
            var close = value.IndexOf(']');
            if (close < 0) return (value, "");
            var rest = value[(close + 1)..];
            return (value[..(close + 1)], rest.StartsWith(':') ? rest[1..] : "");
        }

        var colon = value.LastIndexOf(':');
        if (colon < 0) return (value, "");
        var port = value[(colon + 1)..];
        // A bare IPv6 address has several colons and no port; only a numeric tail is a port.
        return port.Length > 0 && port.All(char.IsAsciiDigit)
            ? (value[..colon], port)
            : (value, "");
    }
}
