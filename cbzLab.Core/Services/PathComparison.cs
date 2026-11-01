namespace cbzLab.Services;

/// <summary>
/// How two paths on this machine compare. Windows and macOS filesystems are case-insensitive by
/// default, Linux's aren't: comparing case-insensitively everywhere treated "Saga.cbz" and
/// "saga.cbz" in one Linux folder as the same file, so the second never opened and both shared
/// one autosave draft.
/// </summary>
public static class PathComparison
{
    public static readonly StringComparison Comparison =
        OperatingSystem.IsLinux() ? StringComparison.Ordinal : StringComparison.OrdinalIgnoreCase;

    public static readonly StringComparer Comparer =
        OperatingSystem.IsLinux() ? StringComparer.Ordinal : StringComparer.OrdinalIgnoreCase;

    public static bool Same(string? a, string? b) => string.Equals(a, b, Comparison);

    /// <summary>Case folded only where the filesystem folds it - for keys that must match however a path was typed.</summary>
    public static string Key(string path) => OperatingSystem.IsLinux() ? path : path.ToLowerInvariant();
}
