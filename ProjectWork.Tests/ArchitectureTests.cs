using System.Xml.Linq;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ProjectWork.Tests;

public class ArchitectureTests
{
    public static string Root()
    {
        var current = new DirectoryInfo(AppContext.BaseDirectory);
        while (current is not null && !File.Exists(Path.Combine(current.FullName, "ProjectWork.slnx")))
            current = current.Parent;
        return current?.FullName ?? throw new InvalidOperationException("Solution root not found");
    }

    [Theory]
    [InlineData("Users")]
    [InlineData("Events")]
    [InlineData("Bookings")]
    public void ServiceReferences_StayInsideTheirBoundaryAndRespectLayers(string service)
    {
        var root = Root();
        foreach (var layer in new[] { "Domain", "Application", "Infrastructure", "Presentation" })
        {
            var project = Path.Combine(root, "Services", service, $"ProjectWork.{service}.{layer}", $"ProjectWork.{service}.{layer}.csproj");
            var allowed = layer switch
            {
                "Domain" => Array.Empty<string>(),
                "Application" => new[] { "Domain" },
                "Infrastructure" => new[] { "Domain", "Application" },
                _ => new[] { "Application", "Infrastructure" }
            };
            foreach (var reference in XDocument.Load(project).Descendants("ProjectReference"))
            {
                var include = reference.Attribute("Include")!.Value.Replace('\\', '/');
                var full = Path.GetFullPath(Path.Combine(Path.GetDirectoryName(project)!, include));
                Assert.StartsWith(Path.Combine(root, "Services", service), full);
                Assert.Contains(Path.GetFileNameWithoutExtension(full).Split('.').Last(), allowed);
            }
            if (layer == "Domain")
                Assert.Empty(XDocument.Load(project).Descendants("PackageReference"));
        }
    }

    [Fact]
    public void Models_ContainOnlyOwnedEntityAndHaveNoCrossServiceRelationships()
    {
        using var db = new TestDatabase();
        using var scope = db.CreateScope();
        DbContext[] contexts =
        [
            scope.ServiceProvider.GetRequiredService<ProjectWork.Users.Infrastructure.Persistence.UsersDbContext>(),
            scope.ServiceProvider.GetRequiredService<ProjectWork.Events.Infrastructure.Persistence.EventsDbContext>(),
            scope.ServiceProvider.GetRequiredService<ProjectWork.Bookings.Infrastructure.Persistence.BookingsDbContext>()
        ];
        var names = new[] { "User", "Event", "Booking" };
        for (var i = 0; i < contexts.Length; i++)
        {
            var entity = Assert.Single(contexts[i].Model.GetEntityTypes());
            Assert.Equal(names[i], entity.ClrType.Name);
            Assert.Empty(entity.GetForeignKeys());
            Assert.Empty(entity.GetNavigations());
        }
    }
}
