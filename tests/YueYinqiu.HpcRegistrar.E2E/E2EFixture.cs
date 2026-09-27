using YueYinqiu.HpcRegistrar;
using YueYinqiu.HpcRegistrar.E2E.MockHpc;
using YueYinqiu.HpcRegistrar.E2E.MockOidc;

namespace YueYinqiu.HpcRegistrar.E2E;

public sealed class E2EFixture : IAsyncLifetime
{
    private readonly List<WebApplication> servers = new();

    private TestCertificate? certificate;

    public HttpClient AppClient { get; private set; } = null!;

    public string AppBaseUrl { get; private set; } = null!;

    public MockHpcClient Hpc { get; private set; } = null!;

    public HttpClientHandler CreateTrustingHandler() => certificate!.CreateTrustingHandler();

    public async Task InitializeAsync()
    {
        Hpc = new MockHpcClient();

        var users = new List<MockUser>
        {
            new("alice", "Alice", Array.Empty<string>()),
            new("bob", "Bob", Array.Empty<string>()),
            new("administrator", "Administrator", new[] { "administrator" }),
        };
        var oidcState = new MockOidcState(users);

        certificate = TestCertificate.Create(Path.GetTempPath());

        var oidcUrl = await StartHttpsServerAsync(app => app.MapMockOidc(oidcState));

        var dataPath = Path.Combine(Path.GetTempPath(), $"hpcregistrar-e2e-{Guid.NewGuid():N}");

        var app = new ServeCommand
        {
            Listen = "http://127.0.0.1:0",
            Data = dataPath,
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
        }.BuildApp(Hpc);

        await app.StartAsync();
        servers.Add(app);

        AppBaseUrl = app.Urls.First();
        AppClient = new HttpClient(certificate.CreateTrustingHandler())
        {
            BaseAddress = new Uri(AppBaseUrl),
        };
        AppClient.DefaultRequestHeaders.UserAgent.Clear();
    }

    public async Task DisposeAsync()
    {
        AppClient.Dispose();
        foreach (var server in servers)
        {
            await server.StopAsync();
            await server.DisposeAsync();
        }
        certificate?.Dispose();
    }

    private async Task<string> StartHttpsServerAsync(Action<WebApplication> configure)
    {
        var builder = WebApplication.CreateEmptyBuilder(new WebApplicationOptions
        {
            Args = new[] { "--urls", "https://127.0.0.1:0" },
        });
        builder.WebHost.UseKestrel(options =>
        {
            options.ConfigureHttpsDefaults(defaults => defaults.ServerCertificate = certificate!.ServerCertificate);
        });
        builder.Services.AddRouting();
        var app = builder.Build();
        configure(app);
        await app.StartAsync();
        servers.Add(app);
        return app.Urls.First();
    }
}
