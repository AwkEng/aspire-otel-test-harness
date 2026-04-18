using Xunit.v3;

namespace Tests.Integration.Infrastructure;

/// <summary>
/// Writes per-test log content to a _Logs/ folder next to the test source file.
/// Complementary to TestOutputHelper — same content, but on disk, so failed-
/// test investigation is a matter of opening a .testlog file instead of
/// fishing through VS Test Explorer output.
/// </summary>
public static class TestLogWriter
{
    private const string LogsFolderName = "_Logs";
    private const string NamespacePrefix = "Tests.Integration.";

    /// <summary>
    /// Writes the content to {testProjectDir}/{relativePathFromNamespace}/_Logs/{methodName}.testlog.
    /// Silently no-ops if the test method name cannot be resolved.
    /// </summary>
    public static void Write(ITestContext testContext, Type testClass, string content)
    {
        var methodName = testContext.TestMethod?.MethodName;
        if (string.IsNullOrEmpty(methodName))
            return;

        var ns = testClass.Namespace ?? "";
        var relativePath = ns.StartsWith(NamespacePrefix, StringComparison.Ordinal)
            ? ns[NamespacePrefix.Length..].Replace('.', Path.DirectorySeparatorChar)
            : string.Empty;

        // assembly.Location → .../bin/Debug/net10.0/Tests.Integration.dll
        // walk up four levels → test project root
        var assemblyLocation = testClass.Assembly.Location;
        var testProjectDir = Path.GetFullPath(
            Path.Combine(assemblyLocation, "..", "..", "..", ".."));

        var logsDir = Path.Combine(testProjectDir, relativePath, LogsFolderName);
        Directory.CreateDirectory(logsDir);

        File.WriteAllText(
            Path.Combine(logsDir, $"{methodName}.testlog"),
            content);
    }
}
