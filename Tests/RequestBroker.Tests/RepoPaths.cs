namespace valheimCLI.Tests;

/// <summary>Files of this repository, found from the test binaries so tests read the source tree on any OS.</summary>
internal static class RepoPaths
{
    /// <summary>The repository root: the nearest directory above the test binaries that holds valheimCLI.csproj.</summary>
    public static string Root()
    {
        for (DirectoryInfo? dir = new(AppContext.BaseDirectory); dir != null; dir = dir.Parent)
            if (File.Exists(Path.Combine(dir.FullName, "valheimCLI.csproj"))) return dir.FullName;
        throw new InvalidOperationException("No directory above " + AppContext.BaseDirectory + " contains valheimCLI.csproj");
    }

    /// <summary>A path under the repository root, given with '/' separators.</summary>
    public static string Of(string relative) =>
        Path.Combine(new[] { Root() }.Concat(relative.Split('/')).ToArray());

    /// <summary>The repository-relative form of a path, with '/' separators.</summary>
    public static string Relative(string path) => Path.GetRelativePath(Root(), path).Replace('\\', '/');
}
