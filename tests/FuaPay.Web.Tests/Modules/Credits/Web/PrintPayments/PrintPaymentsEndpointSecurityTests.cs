using System.Net;
using System.Net.Http.Headers;
using System.Security.Cryptography;
using System.Text;
using System.Text.Json;

using FuaPay.Web.Tests.Testing;

using Microsoft.AspNetCore.Mvc.Testing;

namespace FuaPay.Web.Tests.Modules.Credits.Web.PrintPayments;

public sealed class PrintPaymentsEndpointSecurityTests
{
    private const string Credential =
        "abcdefghijklmnopqrstuvwxyzABCDEFGHIJKLMNOPQ";

    [Fact]
    public async Task Endpoint_WithoutServiceCredentialReturnsStable401()
    {
        using var factory = CreateEnabledFactory();
        using var client = CreateClient(factory);

        using var response = await client.GetAsync(
            "/api/print-payments/reservations?jobUuid=" +
            $"urn:uuid:{Guid.NewGuid():D}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(
            "application/problem+json",
            response.Content.Headers.ContentType?.MediaType);
        Assert.Equal(
            "service_authentication_failed",
            await ReadCodeAsync(response));
    }

    [Fact]
    public async Task Endpoint_WrongCredentialReturns401WithoutCredentialLeak()
    {
        using var factory = CreateEnabledFactory();
        using var client = CreateClient(factory);
        var wrongCredential = new string('Z', Credential.Length);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue(
                "Bearer",
                wrongCredential);

        using var response = await client.GetAsync(
            "/api/print-payments/reservations?jobUuid=" +
            $"urn:uuid:{Guid.NewGuid():D}");
        var content = await response.Content.ReadAsStringAsync();

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Contains(
            "service_authentication_failed",
            content,
            StringComparison.Ordinal);
        Assert.DoesNotContain(
            wrongCredential,
            content,
            StringComparison.Ordinal);
    }

    [Fact]
    public async Task Endpoint_DisabledFeatureRejectsCredential()
    {
        using var factory = new ConfiguredWebApplicationFactory();
        using var client = CreateClient(factory);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Credential);

        using var response = await client.GetAsync(
            "/api/print-payments/reservations?jobUuid=" +
            $"urn:uuid:{Guid.NewGuid():D}");

        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
        Assert.Equal(
            "service_authentication_failed",
            await ReadCodeAsync(response));
    }

    [Fact]
    public async Task Reserve_ClientSuppliedPrintSourceIdIsRejected()
    {
        using var factory = CreateEnabledFactory();
        using var client = CreateClient(factory);
        client.DefaultRequestHeaders.Authorization =
            new AuthenticationHeaderValue("Bearer", Credential);
        var body = $$"""
            {
              "reserveCommandId": "{{Guid.NewGuid():D}}",
              "jobUuid": "urn:uuid:{{Guid.NewGuid():D}}",
              "userIdentity": {
                "provider": "microsoft-entra",
                "tenantId": "{{Guid.NewGuid():D}}",
                "objectId": "{{Guid.NewGuid():D}}"
              },
              "amountMinorUnits": 400,
              "currency": "CZK",
              "printSourceId": "{{Guid.NewGuid():D}}"
            }
            """;
        using var content = new StringContent(
            body,
            Encoding.UTF8,
            "application/json");

        using var response = await client.PostAsync(
            "/api/print-payments/reservations",
            content);

        Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
        Assert.Equal("invalid_request", await ReadCodeAsync(response));
    }

    private static ConfiguredWebApplicationFactory CreateEnabledFactory()
    {
        var digest = Convert.ToHexString(
                SHA256.HashData(
                    Encoding.ASCII.GetBytes(Credential)))
            .ToLowerInvariant();

        return new ConfiguredWebApplicationFactory(
            "Development",
            new Dictionary<string, string?>
            {
                ["ConnectionStrings:FuaPay"] =
                    "Host=localhost;Database=unused;" +
                    "Username=unused;Password=unused",
                ["PrintPayments:Enabled"] = "true",
                ["PrintPayments:Sources:0:PrintSourceId"] =
                    Guid.NewGuid().ToString("D"),
                ["PrintPayments:Sources:0:CredentialSha256"] =
                    digest
            });
    }

    private static HttpClient CreateClient(
        WebApplicationFactory<Program> factory)
    {
        return factory.CreateClient(
            new WebApplicationFactoryClientOptions
            {
                AllowAutoRedirect = false,
                BaseAddress = new Uri("https://localhost")
            });
    }

    private static async Task<string> ReadCodeAsync(
        HttpResponseMessage response)
    {
        using var document = JsonDocument.Parse(
            await response.Content.ReadAsStringAsync());
        return document.RootElement
            .GetProperty("code")
            .GetString()!;
    }
}
