using System.Globalization;
using System.Reflection;
using Ironmon.Updater.Core;

namespace Ironmon.Updater.Tests;

/// <summary>
/// Checks that packaged installer and updater resources resolve and format through the supported culture fallback.
/// </summary>
public sealed class UpdaterLocalizationTests
{
    private const string EnglishCulture = "en-US";
    private const string GermanCulture = "de-DE";

    /// <summary>
    /// Exercises every resource accessor so missing embedded entries, duplicate keys and malformed placeholders fail before release.
    /// </summary>
    /// <param name="cultureName">The neutral or fallback culture used by the native processes.</param>
    [Theory]
    [InlineData(EnglishCulture)]
    [InlineData(GermanCulture)]
    public void PackagedMessagesResolveAndFormat(string cultureName)
    {
        var originalCulture = CultureInfo.CurrentCulture;
        var originalUiCulture = CultureInfo.CurrentUICulture;
        try
        {
            CultureInfo.CurrentCulture = CultureInfo.GetCultureInfo(cultureName);
            CultureInfo.CurrentUICulture = CultureInfo.GetCultureInfo(cultureName);
            foreach (var property in typeof(UpdaterText).GetProperties(BindingFlags.Public | BindingFlags.Static))
                Assert.False(string.IsNullOrWhiteSpace(Assert.IsType<string>(property.GetValue(null))));

            foreach (var method in typeof(UpdaterText).GetMethods(BindingFlags.Public | BindingFlags.Static).Where(method => !method.IsSpecialName))
            {
                object[] arguments = [.. method.GetParameters().Select(parameter => Convert.ChangeType(1.5, parameter.ParameterType, CultureInfo.InvariantCulture))];
                Assert.False(string.IsNullOrWhiteSpace(Assert.IsType<string>(method.Invoke(null, arguments))));
            }

            Assert.Equal("Ironmon Setup", UpdaterText.SetupWindowIronmonSetup);
            Assert.Contains(cultureName == GermanCulture ? "1,5" : "1.5", UpdaterText.SetupWindowTrackerAndUpdaterMiB(1.5));
        }
        finally
        {
            CultureInfo.CurrentCulture = originalCulture;
            CultureInfo.CurrentUICulture = originalUiCulture;
        }
    }
}
