using Microsoft.Extensions.Configuration;

namespace ProjectWork.PresentationTests;

public class JwtConfigurationTests
{
    private static string Root()
    {
        var directory = new DirectoryInfo(AppContext.BaseDirectory);
        while (directory is not null && !File.Exists(Path.Combine(directory.FullName, "ProjectWork.slnx")))
            directory = directory.Parent;
        return directory?.FullName ?? throw new InvalidOperationException("Solution not found");
    }

    [Theory]
    [InlineData("Users")]
    [InlineData("Events")]
    [InlineData("Bookings")]
    public void ProductionRequiresExplicitKey_DevelopmentCanStart(string service)
    {
        var folder = Path.Combine(Root(), "Services", service, $"ProjectWork.{service}.Presentation");
        IConfiguration config = new ConfigurationBuilder().AddJsonFile(Path.Combine(folder, "appsettings.json")).Build();
        Assert.Throws<InvalidOperationException>(() => Validate(service, config));
        config = new ConfigurationBuilder().AddJsonFile(Path.Combine(folder, "appsettings.json"))
            .AddJsonFile(Path.Combine(folder, "appsettings.Development.json")).Build();
        Validate(service, config);
        config = new ConfigurationBuilder().AddJsonFile(Path.Combine(folder, "appsettings.json"))
            .AddInMemoryCollection(new Dictionary<string, string?> { ["Jwt:Secret"] = "Explicit-Test-Only-Secret-At-Least-32-Bytes" }).Build();
        Validate(service, config);
    }

    [Theory]
    [InlineData("Development")]
    [InlineData("Production")]
    public void TokenValidationParameters_AreIdenticalInAllServices(string environment)
    {
        // Токен от Users пройдёт проверку только при совпадении секрета, издателя и аудитории.
        var configs = new[] { "Users", "Events", "Bookings" }.Select(service =>
        {
            var folder = Path.Combine(Root(), "Services", service, $"ProjectWork.{service}.Presentation");
            return new ConfigurationBuilder().AddJsonFile(Path.Combine(folder, "appsettings.json"))
                .AddJsonFile(Path.Combine(folder, $"appsettings.{environment}.json"), optional: true).Build();
        }).ToList();
        foreach (var key in new[] { "Jwt:Secret", "Jwt:Issuer", "Jwt:Audience" })
            Assert.Single(configs.Select(c => c[key]).Distinct());
    }

    private static void Validate(string service, IConfiguration config)
    {
        switch (service)
        {
            case "Users": ProjectWork.Users.Infrastructure.Security.JwtOptions.FromConfiguration(config); break;
            case "Events": ProjectWork.Events.Infrastructure.Security.JwtOptions.FromConfiguration(config); break;
            case "Bookings": ProjectWork.Bookings.Infrastructure.Security.JwtOptions.FromConfiguration(config); break;
        }
    }
}
