using FuaPay.Web.BuildingBlocks.Persistence;
using FuaPay.Web.Development;
using FuaPay.Web.Hosting;
using FuaPay.Web.Modules.Access;
using FuaPay.Web.Modules.Access.Application;
using FuaPay.Web.Modules.Access.Development;
using FuaPay.Web.Modules.Access.Infrastructure.Entra;
using FuaPay.Web.Modules.Access.Web;
using FuaPay.Web.Modules.Audit;
using FuaPay.Web.Modules.Credits;
using FuaPay.Web.Modules.Jobs;
using FuaPay.Web.Modules.Notifications;
using FuaPay.Web.Modules.Payments;
using FuaPay.Web.Modules.Payments.Infrastructure.Csob;
using FuaPay.Web.Modules.Receipts;
using FuaPay.Web.Modules.Receipts.Application;
using FuaPay.Web.Modules.Reporting;
using FuaPay.Web.Modules.ServiceUnits;

using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.DataProtection;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.AspNetCore.StaticAssets;
using Microsoft.EntityFrameworkCore;

var builder = WebApplication.CreateBuilder(args);

var runtimeFeatures =
    RuntimeFeatureSelection.Resolve(
        builder.Environment.EnvironmentName,
        builder.Configuration);
var entraAuthenticationConfiguration =
    EntraAuthenticationConfiguration.Resolve(
        builder.Configuration,
        builder.Environment.EnvironmentName,
        runtimeFeatures.InteractiveTestSignInEnabled);
var hostingConfiguration =
    FuaPayHostingConfiguration.Resolve(
        builder.Configuration);
hostingConfiguration.ValidateForEnvironment(
    builder.Environment.EnvironmentName,
    builder.Configuration);
var applicationRoot =
    hostingConfiguration.PathBase.HasValue
        ? $"{hostingConfiguration.PathBase}/"
        : "/";
var applyMigrationsOnStart =
    builder.Configuration.GetValue<bool>(
        "Database:ApplyMigrationsOnStart");
var csobGatewayConfiguration =
    CsobGatewayConfiguration.Resolve(
        builder.Configuration,
        builder.Environment.EnvironmentName);
var paymentProviderSelection =
    PaymentProviderSelection.Resolve(
        builder.Environment.EnvironmentName,
        builder.Configuration,
        runtimeFeatures,
        csobGatewayConfiguration.Enabled);
var csobReconciliationConfiguration =
    CsobReconciliationConfiguration.Resolve(
        builder.Configuration,
        csobGatewayConfiguration);

var receiptConfiguration =
    ReceiptConfiguration.Resolve(
        builder.Configuration,
        builder.Environment.EnvironmentName,
        builder.Environment.WebRootPath ??
            Path.Combine(builder.Environment.ContentRootPath, "wwwroot"));

builder.Services.AddSingleton(
    new DevelopmentSignInAvailability(
        runtimeFeatures.InteractiveTestSignInEnabled));

var dataProtection =
    builder.Services
        .AddDataProtection()
        .SetApplicationName("FuaPay");

if (
    !string.IsNullOrWhiteSpace(
        hostingConfiguration.DataProtectionKeyRingPath))
{
    dataProtection.PersistKeysToFileSystem(
        new DirectoryInfo(
            hostingConfiguration.DataProtectionKeyRingPath));
}

if (hostingConfiguration.ForwardedHeadersEnabled)
{
    builder.Services.Configure<ForwardedHeadersOptions>(
        options =>
        {
            options.ForwardedHeaders =
                ForwardedHeaders.XForwardedFor |
                ForwardedHeaders.XForwardedHost |
                ForwardedHeaders.XForwardedProto;
            options.ForwardLimit = 1;
            options.KnownIPNetworks.Clear();
            options.KnownProxies.Clear();
            foreach (
                var knownProxy in
                hostingConfiguration.KnownProxies)
            {
                options.KnownProxies.Add(knownProxy);
            }
        });
}

builder.Services.AddHsts(
    options =>
        options.MaxAge = TimeSpan.FromDays(365));

builder.Services
    .AddAuthentication(
        CookieAuthenticationDefaults.AuthenticationScheme)
    .AddCookie(
        options =>
        {
            options.LoginPath =
                runtimeFeatures.InteractiveTestSignInEnabled
                    ? "/Development/SignIn"
                    : "/";
            options.AccessDeniedPath = "/";
            options.Cookie.Name = "FuaPay.Session";
            options.Cookie.HttpOnly = true;
            options.Cookie.Path =
                hostingConfiguration.PathBase.HasValue
                    ? hostingConfiguration.PathBase.Value
                    : "/";
            options.Cookie.SameSite = SameSiteMode.Lax;
            options.Cookie.SecurePolicy =
                builder.Environment.IsDevelopment()
                    ? CookieSecurePolicy.SameAsRequest
                    : CookieSecurePolicy.Always;
            options.ExpireTimeSpan = TimeSpan.FromHours(8);
            options.SlidingExpiration = false;
        })
    .AddEntraAuthentication(
        entraAuthenticationConfiguration,
        applicationRoot);

builder.Services.AddAuthorization(
    options =>
    {
        options.FallbackPolicy =
            new AuthorizationPolicyBuilder()
                .RequireAuthenticatedUser()
                .Build();
    });

builder.Services.AddAntiforgery(
    options =>
    {
        options.Cookie.Name = "FuaPay.Antiforgery";
        options.Cookie.HttpOnly = true;
        options.Cookie.Path =
            hostingConfiguration.PathBase.HasValue
                ? hostingConfiguration.PathBase.Value
                : "/";
        options.Cookie.SameSite = SameSiteMode.Strict;
        options.Cookie.SecurePolicy =
            builder.Environment.IsDevelopment()
                ? CookieSecurePolicy.SameAsRequest
                : CookieSecurePolicy.Always;
    });

builder.Services
    .AddFuaPayPersistence(builder.Configuration)
    .AddAuditModule()
    .AddAccessModule(
        runtimeFeatures.InteractiveTestSignInEnabled)
    .AddCreditsModule()
    .AddJobsModule()
    .AddServiceUnitsModule()
    .AddPaymentsModule(
        paymentProviderSelection.Provider,
        paymentProviderSelection.DevelopmentPaymentUiEnabled)
    .AddReceiptsModule(receiptConfiguration)
    .AddCsobPaymentGateway(
        csobGatewayConfiguration,
        csobReconciliationConfiguration,
        activateProviderInitiator:
            paymentProviderSelection.Provider ==
            FuaPay.Web.Modules.Payments.Domain.PaymentProvider.Csob)
    .AddNotificationsModule()
    .AddReportingModule()
    .AddDevelopmentData(
        runtimeFeatures.TestDataEnabled)
    .AddRazorPages(
        options =>
        {
            options.Conventions.AllowAnonymousToPage(
                "/Index");
            options.Conventions.AllowAnonymousToPage(
                "/Privacy");
            options.Conventions.AllowAnonymousToPage(
                "/Terms");
            options.Conventions.AllowAnonymousToPage(
                "/Error");

            if (runtimeFeatures.InteractiveTestSignInEnabled)
            {
                options.Conventions.AllowAnonymousToPage(
                    "/Development/SignIn");
            }
            else
            {
                options.Conventions
                    .AddFolderRouteModelConvention(
                        "/Development",
                        model => model.Selectors.Clear());
            }
        });

var app = builder.Build();

if (runtimeFeatures.IsStagingTestMode)
{
    app.Logger.LogWarning(
        "Staging test mode is enabled. " +
        "Interactive test identities or simulated payments " +
        "must never be enabled in Production.");
}

if (hostingConfiguration.ForwardedHeadersEnabled)
{
    app.UseForwardedHeaders();
}

if (hostingConfiguration.PathBase.HasValue)
{
    app.Use(
        async (context, next) =>
        {
            if (
                !context.Request.Path.StartsWithSegments(
                    hostingConfiguration.PathBase))
            {
                context.Response.StatusCode =
                    StatusCodes.Status404NotFound;
                return;
            }

            await next();
        });

    app.UsePathBase(hostingConfiguration.PathBase);
}

if (
    applyMigrationsOnStart ||
    runtimeFeatures.TestDataEnabled)
{
    await using var scope =
        app.Services.CreateAsyncScope();

    if (applyMigrationsOnStart)
    {
        var dbContext =
            scope.ServiceProvider
                .GetRequiredService<FuaPayDbContext>();

        await dbContext.Database.MigrateAsync();
    }

    if (runtimeFeatures.TestDataEnabled)
    {
        var seeder =
            scope.ServiceProvider
                .GetRequiredService<DevelopmentDataSeeder>();

        await seeder.SeedAsync(
            runtimeFeatures.ResetTestDataOnStart);
    }
}

if (!app.Environment.IsDevelopment())
{
    app.UseExceptionHandler("/Error");
    app.UseHsts();
}

app.UseHttpsRedirection();

app.Use(
    async (context, next) =>
    {
        context.Response.Headers["X-Content-Type-Options"] = "nosniff";
        context.Response.Headers["X-Frame-Options"] = "DENY";
        context.Response.Headers["Referrer-Policy"] = "strict-origin-when-cross-origin";
        context.Response.Headers["Permissions-Policy"] =
            "camera=(), geolocation=(), microphone=()";
        context.Response.Headers["Content-Security-Policy"] =
            "default-src 'self'; " +
            "base-uri 'none'; " +
            "form-action 'self'; " +
            "frame-ancestors 'none'; " +
            "img-src 'self' data:; " +
            "object-src 'none'; " +
            "script-src 'self'; " +
            "style-src 'self'";

        await next();
    });

app.UseRouting();

app.UseRateLimiter();

app.UseAuthentication();

app.Use(
    async (context, next) =>
    {
        var isStaticAsset =
            context.GetEndpoint()?.Metadata
                .GetMetadata<StaticAssetDescriptor>() is not null;

        if (
            !isStaticAsset &&
            context.User.Identity?.IsAuthenticated == true)
        {
            context.Response.Headers.CacheControl = "no-store";

            var synchronizer =
                context.RequestServices
                    .GetRequiredService<AccessSessionSynchronizer>();

            var synchronization =
                await synchronizer.SynchronizeAsync(
                    context.User,
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    context.RequestAborted);

            if (!synchronization.IsValid)
            {
                await context.SignOutAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme);
                context.Response.Redirect(applicationRoot);
                return;
            }

            if (synchronization.ShouldRenew)
            {
                context.User = synchronization.Principal!;

                var authentication = await context.AuthenticateAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme);

                await context.SignInAsync(
                    CookieAuthenticationDefaults.AuthenticationScheme,
                    context.User,
                    authentication.Properties);
            }
        }

        await next();
    });

app.UseAuthorization();

app.MapGet(
        "/health/live",
        () => Results.Json(
            new
            {
                status = "Healthy"
            }))
    .AllowAnonymous();

app.MapGet(
        "/health/ready",
        async (
            FuaPayDbContext dbContext,
            CancellationToken cancellationToken) =>
        {
            try
            {
                var canConnect =
                    await dbContext.Database.CanConnectAsync(
                        cancellationToken);

                return Results.Json(
                    new
                    {
                        status = canConnect
                            ? "Healthy"
                            : "Unhealthy"
                    },
                    statusCode: canConnect
                        ? StatusCodes.Status200OK
                        : StatusCodes.Status503ServiceUnavailable);
            }
            catch (OperationCanceledException)
                when (cancellationToken.IsCancellationRequested)
            {
                throw;
            }
            catch (Exception exception)
            {
                app.Logger.LogWarning(
                    exception,
                    "Database readiness probe failed.");

                return Results.Json(
                    new
                    {
                        status = "Unhealthy"
                    },
                    statusCode:
                        StatusCodes.Status503ServiceUnavailable);
            }
        })
    .AllowAnonymous();

app.MapGet(
        "/health/workers/csob-reconciliation",
        (
            CsobPaymentReconciliationHealth health,
            TimeProvider timeProvider) =>
        {
            var snapshot = health.GetSnapshot(
                timeProvider.GetUtcNow(),
                csobReconciliationConfiguration.Enabled,
                csobReconciliationConfiguration.PollInterval);
            var isHealthy = snapshot.Status is
                CsobPaymentReconciliationHealthStatus.Disabled or
                CsobPaymentReconciliationHealthStatus.Healthy;

            return Results.Json(
                new
                {
                    status = snapshot.Status.ToString(),
                    lastSuccessfulCycleAt =
                        snapshot.LastSuccessfulCycleAt,
                    lastFailedCycleAt = snapshot.LastFailedCycleAt,
                    staleAfterSeconds =
                        snapshot.StaleAfter.TotalSeconds
                },
                statusCode: isHealthy
                    ? StatusCodes.Status200OK
                    : StatusCodes.Status503ServiceUnavailable);
        })
    .AllowAnonymous();

if (csobGatewayConfiguration.Enabled)
{
    app.MapCsobPaymentReturn();
}

app.MapStaticAssets()
    .AllowAnonymous();
app.MapRazorPages()
    .WithStaticAssets();

app.MapFallback(
        () => Results.NotFound())
    .AllowAnonymous();

app.Run();

public partial class Program
{
}
