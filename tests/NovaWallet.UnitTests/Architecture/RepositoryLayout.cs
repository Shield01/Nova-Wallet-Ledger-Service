namespace NovaWallet.UnitTests.Architecture;

internal static class RepositoryLayout
{
    public static string Root { get; } = FindRoot();

    public static string ProjectFile(string projectName) =>
        Path.Combine(Root, "src", projectName, $"{projectName}.csproj");

    private static string FindRoot()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "NovaWallet.sln")))
        {
            directory = directory.Parent;
        }

        return directory?.FullName
            ?? throw new InvalidOperationException("Could not find NovaWallet.sln above the test output directory.");
    }
}
