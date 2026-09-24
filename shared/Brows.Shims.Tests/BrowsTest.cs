using System;
using System.Linq;

namespace Brows;

internal static class BrowsTest {
    public static BrowsTestEnv Env() {
        var value = Environment.GetEnvironmentVariable("BROWS_TEST_ENV");
        return Enum.TryParse<BrowsTestEnv>(value, out var result)
            ? result
            : BrowsTestEnv.Default;
    }

    public static void Ignore(params BrowsTestEnv[] envs) {
        if (envs is null) {
            return;
        }
        var envCurrent = Env();
        if (envs.Any(env => env == envCurrent)) {
            Assert.Ignore($"Ignored in env '{envCurrent}'");
        }
    }
}
