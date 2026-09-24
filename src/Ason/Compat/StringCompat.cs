namespace Ason.Compat;

/// <summary>
/// Polyfills for string APIs that netstandard2.0 lacks. This library ships the netstandard2.0 asset only (that
/// is what the legacy .NET Framework hosts consume), so it has to carry them itself.
///
/// An extension method is only consulted when the runtime offers no applicable instance method, so on modern
/// .NET the built-in overload still wins and this file changes nothing there. That matters here: the default
/// <c>Replace(string, string)</c> is ordinal *and case sensitive*, while the call sites ask for
/// <c>OrdinalIgnoreCase</c> - rewriting them to the two-argument overload would silently change behaviour
/// (for example a ```JSON fence would stop being stripped).
/// </summary>
internal static class StringCompat {

    /// <summary><c>string.Replace(string, string, StringComparison)</c> was added in netstandard2.1.</summary>
    internal static string Replace(this string text, string oldValue, string newValue, System.StringComparison comparison) {
        if (string.IsNullOrEmpty(oldValue) || string.IsNullOrEmpty(text)) return text;

        var found = text.IndexOf(oldValue, comparison);
        if (found < 0) return text;

        var builder = new System.Text.StringBuilder(text.Length);
        var index = 0;
        while (found >= 0) {
            builder.Append(text, index, found - index).Append(newValue);
            index = found + oldValue.Length;
            found = index < text.Length ? text.IndexOf(oldValue, index, comparison) : -1;
        }

        return builder.Append(text, index, text.Length - index).ToString();
    }
}
