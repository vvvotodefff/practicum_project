using Microsoft.Extensions.Configuration;
using ProjectWork.Infrastructure.Security;

namespace ProjectWork.PresentationTests;

public class JwtConfigurationTests
{
    private static string PresentationDirectory()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ProjectWork.slnx")))
            directory = directory.Parent;
        return Path.Combine(directory?.FullName ?? throw new InvalidOperationException("Solution not found."),
            "ProjectWork.Presentation");
    }

    private static IConfigurationBuilder Configuration(string environment) =>
        new ConfigurationBuilder().SetBasePath(PresentationDirectory())
            .AddJsonFile("appsettings.json", optional: false)
            .AddJsonFile($"appsettings.{environment}.json", optional: true);

    [Theory]
    [InlineData("Production")]
    [InlineData("Staging")]
    public void NonDevelopment_WithoutExplicitSecret_RefusesStartup(string environment)
    {
        var configuration = Configuration(environment).Build();
        Assert.True(string.IsNullOrEmpty(configuration["Jwt:Secret"]));
        var error = Assert.Throws<InvalidOperationException>(() => JwtOptions.FromConfiguration(configuration));
        Assert.Contains("Jwt:Secret", error.Message);
    }

    [Fact]
    public void Development_HasSeparateLocalKey()
    {
        var settings = JwtOptions.FromConfiguration(Configuration("Development").Build());
        Assert.True(System.Text.Encoding.UTF8.GetByteCount(settings.Secret) >= 32);
    }

    [Fact]
    public void Production_ExplicitKeyOverridesEmptyDefault()
    {
        var key = Convert.ToBase64String(System.Security.Cryptography.RandomNumberGenerator.GetBytes(32));
        var configuration = Configuration("Production").AddInMemoryCollection(
            new Dictionary<string, string?> { ["Jwt:Secret"] = key }).Build();
        Assert.Equal(key, JwtOptions.FromConfiguration(configuration).Secret);
    }
}
