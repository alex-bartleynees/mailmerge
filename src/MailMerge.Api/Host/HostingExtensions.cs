using System.Text.Json;
using System.Text.Json.Serialization;
using FluentValidation;
using MailMerge.Api.Shared;
using MailMerge.Api.Shared.Auth;
using MailMerge.Api.Shared.Persistence;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using SharedKernel.AspNetCore;

namespace MailMerge.Api.Host;

public static class HostingExtensions
{
    public static WebApplicationBuilder AddMailMerge(this WebApplicationBuilder builder)
    {
        var services = builder.Services;

        builder.AddObservability();

        services.AddOptions<AppSettings>().BindConfiguration(AppSettings.SectionName)
            .ValidateDataAnnotations().ValidateOnStart();
        services.AddOptions<AuthSettings>().BindConfiguration(AuthSettings.SectionName)
            .ValidateDataAnnotations().ValidateOnStart();

        services.AddSingleton(TimeProvider.System);

        // Connection string is read lazily so tests can supply it through configuration.
        services.AddDbContext<MailMergeDbContext>((sp, options) => options
            .UseNpgsql(sp.GetRequiredService<IConfiguration>().GetConnectionString("MailMerge")
                       ?? throw new InvalidOperationException("ConnectionStrings:MailMerge is not configured"))
            .UseSnakeCaseNamingConvention());

        services.AddDataProtection()
            .SetApplicationName("mailmerge")
            .PersistKeysToDbContext<MailMergeDbContext>();

        if (DevelopmentAuthentication.IsEnabled(builder.Environment, builder.Configuration))
        {
            DevelopmentAuthentication.Add(builder);
            AddAuthorization(services);
        }
        else
        {
            AddAuth(services);
        }

        services.AddScoped<CurrentUser>();
        services.AddSingleton<WorkspaceDirectory>();
        services.AddScoped<WorkspaceFilter>();

        services.AddValidatorsFromAssemblyContaining<Program>(ServiceLifetime.Singleton, includeInternalTypes: true);
        AddHandlers(services);
        services.AddMailMergeFeatures();

        services.ConfigureHttpJsonOptions(o =>
            o.SerializerOptions.Converters.Add(new JsonStringEnumConverter(JsonNamingPolicy.CamelCase)));
        services.AddProblemDetails();
        services.AddOpenApi();
        services.AddHealthChecks()
            .AddCheck<DatabaseHealthCheck>("database", timeout: TimeSpan.FromSeconds(5));

        return builder;
    }

    public static WebApplication UseMailMerge(this WebApplication app)
    {
        using (var scope = app.Services.CreateScope())
        {
            scope.ServiceProvider.GetRequiredService<MailMergeDbContext>().Database.Migrate();
        }

        app.UseExceptionHandler();
        app.UseStatusCodePages();

        app.UseDefaultFiles();
        app.UseStaticFiles();

        if (DevelopmentAuthentication.IsEnabled(app.Environment, app.Configuration))
        {
            app.Logger.LogWarning("DEVELOPMENT SIGN-IN IS ON: every request is treated as a signed-in staff member");
        }

        app.UseAuthentication();
        app.UseAuthorization();

        if (app.Environment.IsDevelopment())
        {
            app.MapOpenApi();
        }

        app.MapHealthChecks("/health").AllowAnonymous();
        // Dependency outages remove the pod from service without triggering restart loops.
        app.MapHealthChecks("/liveness", new HealthCheckOptions { Predicate = _ => false }).AllowAnonymous();
        MapEndpointDefinitions(app);
        return app;
    }

    private static void AddAuth(IServiceCollection services)
    {
        services.AddAuthentication(JwtBearerDefaults.AuthenticationScheme).AddJwtBearer();
        services.AddOptions<JwtBearerOptions>(JwtBearerDefaults.AuthenticationScheme)
            .Configure<IOptions<AuthSettings>>((jwt, settings) =>
            {
                jwt.Authority = settings.Value.Authority;
                jwt.Audience = settings.Value.Audience;
                jwt.RequireHttpsMetadata = settings.Value.RequireHttpsMetadata;
                jwt.MapInboundClaims = false;
                jwt.TokenValidationParameters.NameClaimType = "preferred_username";
            });

        AddAuthorization(services);
    }

    private static void AddAuthorization(IServiceCollection services)
    {
        services.AddSingleton<IAuthorizationHandler, MailMergeRoleHandler>();
        services.AddAuthorizationBuilder()
            .AddPolicy(MailMergeAuthorization.Policy, policy => policy
                .RequireAuthenticatedUser()
                .AddRequirements(new MailMergeRoleRequirement()));
    }

    private static void AddHandlers(IServiceCollection services)
    {
        var handlers = typeof(Program).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IHandler).IsAssignableFrom(t));
        foreach (var handler in handlers)
        {
            services.AddScoped(handler);
        }
    }

    private static void MapEndpointDefinitions(WebApplication app)
    {
        var definitions = typeof(Program).Assembly.GetTypes()
            .Where(t => t is { IsClass: true, IsAbstract: false } && typeof(IEndpointDefinition).IsAssignableFrom(t))
            .Select(Activator.CreateInstance)
            .Cast<IEndpointDefinition>();

        foreach (var definition in definitions)
        {
            definition.RegisterEndpoints(app);
        }
    }
}
