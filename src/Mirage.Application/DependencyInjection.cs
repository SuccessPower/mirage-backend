using Microsoft.Extensions.DependencyInjection;

namespace Mirage.Application;

public static class DependencyInjection
{
    public static IServiceCollection AddApplication(this IServiceCollection services) => services;
}
