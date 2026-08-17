using System.Text.Json;

namespace FuaPay.Web.Tests.Hosting;

public sealed class LaunchSettingsTests
{
    [Fact]
    public void LaunchSettings_ExposeOnlyHttpsDevelopmentEndpoint()
    {
        using var document = JsonDocument.Parse(
            File.ReadAllText(FindLaunchSettings()));

        var profiles = document
            .RootElement
            .GetProperty("profiles");

        var profileNames = profiles
            .EnumerateObject()
            .Select(profile => profile.Name)
            .ToArray();

        Assert.Equal(
            new[] { "https" },
            profileNames);

        var httpsProfile =
            profiles.GetProperty("https");

        Assert.Equal(
            "https://localhost:7029",
            httpsProfile
                .GetProperty("applicationUrl")
                .GetString());
    }

    private static string FindLaunchSettings()
    {
        DirectoryInfo? directory =
            new(AppContext.BaseDirectory);

        while (directory is not null)
        {
            var candidate = Path.Combine(
                directory.FullName,
                "src",
                "FuaPay.Web",
                "Properties",
                "launchSettings.json");

            if (File.Exists(candidate))
            {
                return candidate;
            }

            directory = directory.Parent;
        }

        throw new FileNotFoundException(
            "Soubor launchSettings.json nebyl nalezen.");
    }
}
