using Microsoft.Extensions.DependencyInjection;
using ProjectWork.Application;
using ProjectWork.Application.Services;

namespace ProjectWork.Tests;

public class ApplicationRegistrationTests
{
    [Fact]
    public void AddApplicationServices_RegistersOnlyScopedApplicationServices()
    {
        var services = new ServiceCollection();

        Assert.Same(services, services.AddApplicationServices());

        Assert.Equal(2, services.Count);
        var events = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IEventService));
        Assert.Equal(typeof(EventService), events.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, events.Lifetime);
        var bookings = Assert.Single(services, descriptor => descriptor.ServiceType == typeof(IBookingService));
        Assert.Equal(typeof(BookingService), bookings.ImplementationType);
        Assert.Equal(ServiceLifetime.Scoped, bookings.Lifetime);
    }

    [Fact]
    public void AddApplicationServices_ResolvesSameServicesWithinScopeAndDifferentAcrossScopes()
    {
        using var database = new TestDatabase();
        using var first = database.CreateScope();
        using var second = database.CreateScope();

        var events = first.ServiceProvider.GetRequiredService<IEventService>();
        var bookings = first.ServiceProvider.GetRequiredService<IBookingService>();

        Assert.Same(events, first.ServiceProvider.GetRequiredService<IEventService>());
        Assert.Same(bookings, first.ServiceProvider.GetRequiredService<IBookingService>());
        Assert.NotSame(events, second.ServiceProvider.GetRequiredService<IEventService>());
        Assert.NotSame(bookings, second.ServiceProvider.GetRequiredService<IBookingService>());
    }
}
