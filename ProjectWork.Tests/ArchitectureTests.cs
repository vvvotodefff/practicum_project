using System.Reflection;
using ProjectWork.Application.Services;
using ProjectWork.Domain.Entities;
using ProjectWork.Infrastructure.Persistence;

namespace ProjectWork.Tests;

public class ArchitectureTests
{
    private static IEnumerable<string> References(Assembly assembly) =>
        assembly.GetReferencedAssemblies().Select(reference => reference.Name!);

    private static bool IsPlatform(string name) =>
        name is "System" or "mscorlib" or "netstandard" || name.StartsWith("System.", StringComparison.Ordinal);

    [Fact]
    public void Domain_ReferencesOnlyDotNetPlatform() =>
        Assert.All(References(typeof(Event).Assembly), name =>
            Assert.True(IsPlatform(name), $"Domain must not reference {name}."));

    [Fact]
    public void Application_ReferencesOnlyDomainPlatformAndDiAbstractions() =>
        Assert.All(References(typeof(EventService).Assembly), name =>
            Assert.True(IsPlatform(name) || name is "ProjectWork.Domain" or "Microsoft.Extensions.DependencyInjection.Abstractions",
                $"Application must not reference {name}."));

    [Fact]
    public void Infrastructure_DoesNotReferencePresentation() =>
        Assert.DoesNotContain(References(typeof(AppDbContext).Assembly), name =>
            name is "ProjectWork" or "ProjectWork.Presentation" || name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));

    [Fact]
    public void CoreTests_DoNotReferencePresentation() =>
        Assert.DoesNotContain(References(typeof(ArchitectureTests).Assembly), name =>
            name is "ProjectWork" or "ProjectWork.Presentation" || name.StartsWith("Microsoft.AspNetCore", StringComparison.Ordinal));
}
