using System.Reflection;

namespace Brows;

internal static class AssemblyUnderTest {
    private const string TestAssemblySuffix = ".Tests";

    private static Assembly Value => field ??= Load();

    private static Assembly Load() {
        var testAssemblyName = typeof(AssemblyUnderTest).Assembly.GetName().Name;
        if (testAssemblyName?.EndsWith(TestAssemblySuffix, StringComparison.Ordinal) != true) {
            throw new InvalidOperationException(
                $"The test assembly name '{testAssemblyName}' does not end with '{TestAssemblySuffix}'.");
        }
        var assemblyName = testAssemblyName.Substring(
            startIndex: 0,
            length: testAssemblyName.Length - TestAssemblySuffix.Length);
        return Assembly.Load(new AssemblyName(assemblyName));
    }

    public static Type Type(string fullName) {
        return Value.GetType(fullName, throwOnError: true);
    }
}
