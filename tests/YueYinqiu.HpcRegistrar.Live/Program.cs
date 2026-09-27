using Microsoft.AspNetCore.Builder;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Hosting;
using YueYinqiu.HpcRegistrar;
using YueYinqiu.HpcRegistrar.E2E;
using YueYinqiu.HpcRegistrar.E2E.MockHpc;
using YueYinqiu.HpcRegistrar.E2E.MockOidc;

var listen = args.ElementAtOrDefault(0) ?? "http://127.0.0.1:8080";
var dataDirectory = Path.Combine(Path.GetTempPath(), "hpcregistrar-live");

Directory.CreateDirectory(dataDirectory);

var users = new List<MockUser>
{
    new("alice", "Alice", Array.Empty<string>()),
    new("bob", "Bob", Array.Empty<string>()),
    new("administrator", "Administrator", new[] { "administrator" }),
};

using var certificate = TestCertificate.Create(dataDirectory);

var oidc = new MockOidcState(users);
var hpc = new MockHpcClient();

var oidcUrl = await StartHttpsServerAsync(certificate, app => app.MapMockOidc(oidc));

var app = new ServeCommand
{
    Listen = listen,
    Data = dataDirectory,
    Administrator = "administrator",
    Title = "测试平台",
    Oidc = oidcUrl,
    OidcId = "test-client",
    OidcSecret = "test-secret",
    OidcCa = certificate.RootCaPem,
    HpcHost = "hpc.example.com",
    HpcUser = "hpcuser",
    HpcKey = "/nonexistent/key",
    HpcHome = "/home/hpcuser",
}.BuildApp(hpc);

Console.WriteLine($"应用:      {listen}");
Console.WriteLine($"Mock OIDC: {oidcUrl}");
Console.WriteLine("按 Ctrl+C 退出。");

await app.RunAsync();

static async Task<string> StartHttpsServerAsync(TestCertificate certificate, Action<WebApplication> configure)
{
    var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions
    {
        Args = new[] { "--urls", "https://127.0.0.1:0" },
    });
    builder.WebHost.UseKestrel(options =>
    {
        options.ConfigureHttpsDefaults(defaults => defaults.ServerCertificate = certificate.ServerCertificate);
    });
    builder.Services.AddRouting();
    var app = builder.Build();
    configure(app);
    await app.StartAsync();
    return app.Urls.First();
}
