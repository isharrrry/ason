using System;
using System.Collections.Generic;
using System.Linq;

namespace Ason.Client.Execution;

public interface IScriptValidator {
    string? Validate(string script);
}

/// <summary>
/// Rejects a script that mentions a forbidden API. It is a keyword filter, not a sandbox, and it is public so
/// hosts that accept scripts from elsewhere - the bridge, for one - apply exactly the same rule set as a
/// local <see cref="AsonClient"/> instead of maintaining a second list.
/// </summary>
public sealed class KeywordScriptValidator : IScriptValidator {
    readonly string[] _forbidden;
    public KeywordScriptValidator(IEnumerable<string> forbidden) { _forbidden = forbidden.ToArray(); }
    public string? Validate(string script) {
        if (string.IsNullOrWhiteSpace(script)) return "Empty script";
        foreach (var pattern in _forbidden) if (script.Contains(pattern, StringComparison.Ordinal)) return $"Forbidden usage detected: {pattern}";
        return null;
    }
}
