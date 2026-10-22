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

namespace ContosoDashboard.Tests.Endpoints;

public sealed class DocumentAccessEndpointTests
{
    [Theory]
    [InlineData("/api/documents/12/download")]
    [InlineData("/api/documents/12/preview")]
    public async Task Content_endpoints_require_authentication(string path)
    {
        var builder = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddAntiforgery();
                services.AddAuthentication("test")
                    .AddScheme<AuthenticationSchemeOptions, DocumentUploadEndpointTests.AnonymousTestAuthenticationHandler>("test", _ => { });
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

        var response = await client.GetAsync(path);

        Assert.Equal(System.Net.HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Authorized_download_returns_file_and_denied_user_gets_no_content()
    {
        await using var connection = new SqliteConnection("Data Source=:memory:");
        await connection.OpenAsync();
        var storage = new FakeFileStorageService();
        var builder = new WebHostBuilder()
            .ConfigureServices(services =>
            {
                services.AddRouting();
                services.AddAntiforgery();
                services.AddAuthentication("test")
                    .AddScheme<AuthenticationSchemeOptions, DocumentUserAuthenticationHandler>("test", _ => { });
                services.AddAuthorization();
                services.AddDbContext<ApplicationDbContext>(options => options.UseSqlite(connection));
                services.AddSingleton<IFileStorageService>(storage);
                services.AddSingleton<IFileScanner>(new FakeFileScanner());
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
        int documentId;
        using (var scope = server.Services.CreateScope())
        {
            var db = scope.ServiceProvider.GetRequiredService<ApplicationDbContext>();
            await db.Database.EnsureCreatedAsync();
            db.Users.Add(new User
            {
                UserId = 99, Email = "outside@example.test", DisplayName = "Outside",
                Department = "Sales", Role = UserRole.Employee
            });
            await db.SaveChangesAsync();
            var service = scope.ServiceProvider.GetRequiredService<IDocumentService>();
            await using var input = new MemoryStream(System.Text.Encoding.ASCII.GetBytes("%PDF-1.7\nendpoint content"));
            var upload = await service.UploadAsync(4, new DocumentUploadRequest
            {
                FileName = "safe.pdf", Content = input, Title = "Endpoint test", Category = DocumentCategory.Reports
            });
            Assert.True(upload.Succeeded);
            documentId = upload.DocumentId!.Value;
        }

        var allowed = await client.GetAsync($"/api/documents/{documentId}/download");
        Assert.Equal(System.Net.HttpStatusCode.OK, allowed.StatusCode);
        Assert.Equal("nosniff", allowed.Headers.GetValues("X-Content-Type-Options").Single());
        Assert.Contains("attachment", allowed.Content.Headers.ContentDisposition!.DispositionType);
        var allowedContent = await allowed.Content.ReadAsByteArrayAsync();
        Assert.Equal("%PDF-1.7\nendpoint content", System.Text.Encoding.ASCII.GetString(allowedContent));

        client.DefaultRequestHeaders.Add("X-Test-User", "99");
        var denied = await client.GetAsync($"/api/documents/{documentId}/download");
        Assert.Equal(System.Net.HttpStatusCode.NotFound, denied.StatusCode);
        Assert.Empty(await denied.Content.ReadAsByteArrayAsync());

        using var verifyScope = server.Services.CreateScope();
        var deniedAudit = await verifyScope.ServiceProvider.GetRequiredService<ApplicationDbContext>()
            .DocumentActivities.AnyAsync(a => a.Action == DocumentActivityAction.DeniedAccess);
        Assert.True(deniedAudit);
    }

    internal sealed class DocumentUserAuthenticationHandler
        : AuthenticationHandler<AuthenticationSchemeOptions>
    {
        public DocumentUserAuthenticationHandler(
            Microsoft.Extensions.Options.IOptionsMonitor<AuthenticationSchemeOptions> options,
            Microsoft.Extensions.Logging.ILoggerFactory logger,
            System.Text.Encodings.Web.UrlEncoder encoder)
            : base(options, logger, encoder)
        {
        }

        protected override Task<AuthenticateResult> HandleAuthenticateAsync()
        {
            var userId = Context.Request.Headers["X-Test-User"].FirstOrDefault() ?? "4";
            var identity = new System.Security.Claims.ClaimsIdentity(new[]
            {
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.NameIdentifier, userId),
                new System.Security.Claims.Claim(System.Security.Claims.ClaimTypes.Role, nameof(UserRole.Employee))
            }, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new System.Security.Claims.ClaimsPrincipal(identity), Scheme.Name)));
        }
    }
}
