using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.EntityFrameworkCore;
using Microsoft.Data.Sqlite;
using ContosoDashboard.Endpoints;
using ContosoDashboard.Data;
using ContosoDashboard.Models;
using ContosoDashboard.Services;
using ContosoDashboard.Tests.TestSupport;
using Xunit;

namespace ContosoDashboard.Tests.Endpoints;

public sealed class DocumentUploadEndpointTests
{
    [Fact]
    public async Task Upload_and_token_routes_require_authentication()
    {
        var builder = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
                services.AddAuthentication("test")
                    .AddScheme<AuthenticationSchemeOptions, AnonymousTestAuthenticationHandler>("test", _ => { });
                services.AddAuthorization();
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints => endpoints.MapDocumentFileEndpoints());
            });
        using var server = new TestServer(builder);
        using var client = server.CreateClient();

        var tokenResponse = await client.GetAsync("/api/documents/antiforgery");
        var uploadResponse = await client.PostAsync("/api/documents/upload", new MultipartFormDataContent());

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, tokenResponse.StatusCode);
        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, uploadResponse.StatusCode);
    }

    [Theory]
    [InlineData("clean", "report.pdf", System.Net.HttpStatusCode.OK)]
    [InlineData("clean", "payload.exe", System.Net.HttpStatusCode.BadRequest)]
    [InlineData("infected", "report.pdf", System.Net.HttpStatusCode.BadRequest)]
    [InlineData("unavailable", "report.pdf", System.Net.HttpStatusCode.BadRequest)]
    public async Task Upload_route_reports_clean_invalid_infected_and_unavailable_results(
        string verdictName, string fileName, System.Net.HttpStatusCode expectedStatus)
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var storage = new FakeFileStorageService();
        var scanner = new FakeFileScanner
        {
            Verdict = verdictName switch
            {
                "infected" => FileScanVerdict.Infected,
                "unavailable" => FileScanVerdict.Unavailable,
                _ => FileScanVerdict.Clean
            }
        };
        var builder = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddAntiforgery(options => options.HeaderName = "X-CSRF-TOKEN");
                services.AddAuthentication("test")
                    .AddScheme<AuthenticationSchemeOptions, AuthenticatedTestAuthenticationHandler>("test", _ => { });
                services.AddAuthorization();
                services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connection));
                services.AddSingleton<IFileStorageService>(storage);
                services.AddSingleton<IFileScanner>(scanner);
                services.AddScoped<IDocumentService, DocumentService>();
            })
            .Configure(app =>
            {
                app.UseRouting();
                app.UseAuthentication();
                app.UseAuthorization();
                app.UseEndpoints(endpoints => endpoints.MapDocumentFileEndpoints());
            });
        using var server = new TestServer(builder);
        using var client = server.CreateClient();
        using (var scope = server.Services.CreateScope())
            await scope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Database.EnsureCreatedAsync();

        var csrf = await GetCsrfAsync(client);
        using var content = new MultipartFormDataContent
        {
            { new StringContent("Sample upload"), "title" },
            { new StringContent(nameof(DocumentCategory.Reports)), "category" }
        };
        var bytes = System.Text.Encoding.ASCII.GetBytes("%PDF-1.7\nsample");
        var fileContent = new ByteArrayContent(bytes);
        content.Add(fileContent, "file", fileName);
        using var request = new HttpRequestMessage(HttpMethod.Post, "/api/documents/upload") { Content = content };
        request.Headers.TryAddWithoutValidation("Cookie", csrf.Cookie);
        request.Headers.TryAddWithoutValidation("X-CSRF-TOKEN", csrf.RequestToken);
        using var response = await client.SendAsync(request);

        Assert.Equal(expectedStatus, response.StatusCode);
        using var verifyScope = server.Services.CreateScope();
        var savedCount = await verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>().Documents.CountAsync();
        Assert.Equal(expectedStatus == System.Net.HttpStatusCode.OK ? 1 : 0, savedCount);
        Assert.Equal(expectedStatus == System.Net.HttpStatusCode.OK ? 1 : 0, storage.Published.Count);
    }

    private static async Task<(string Cookie, string RequestToken)> GetCsrfAsync(HttpClient client)
    {
        using var response = await client.GetAsync("/api/documents/antiforgery");
        var body = await response.Content.ReadAsStringAsync();
        using var json = System.Text.Json.JsonDocument.Parse(body);
        var requestToken = json.RootElement.GetProperty("requestToken").GetString()!;
        var setCookie = response.Headers.GetValues("Set-Cookie").First();
        return (setCookie.Split(';', 2)[0], requestToken);
    }

    internal sealed class AnonymousTestAuthenticationHandler
        : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public AnonymousTestAuthenticationHandler(
            Microsoft.Extensions.Options.IOptionsMonitor<AuthenticationSchemeOptions> options,
            Microsoft.Extensions.Logging.ILoggerFactory logger,
            System.Text.Encodings.Web.UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync() =>
            Task.FromResult(AuthenticateResult.NoResult());
    }

    private sealed class AuthenticatedTestAuthenticationHandler
        : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public AuthenticatedTestAuthenticationHandler(
            Microsoft.Extensions.Options.IOptionsMonitor<AuthenticationSchemeOptions> options,
            Microsoft.Extensions.Logging.ILoggerFactory logger,
            System.Text.Encodings.Web.UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var identity = new System.Security.Claims.ClaimsIdentity(new[]
            {
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, "4"),
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, nameof(UserRole.Employee))
            }, Scheme.Name);
            var principal = new System.Security.Claims.ClaimsPrincipal(identity);
            return Task.FromResult(AuthenticateResult.Success(new AuthenticationTicket(principal, Scheme.Name)));
        }
    }
}
