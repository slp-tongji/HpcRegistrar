using System.Security.Cryptography.X509Certificates;
using CliFx;
using CliFx.Binding;
using CliFx.Infrastructure;
using LiteDB;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using YueYinqiu.HpcRegistrar.Services;

namespace YueYinqiu.HpcRegistrar;

[Command]
public sealed partial class ServeCommand : ICommand
{
    [CommandOption("listen")]
    public required string Listen { get; set; }

    [CommandOption("title")]
    public required string Title { get; set; }

    [CommandOption("data")]
    public required string Data { get; set; }

    [CommandOption("administrator")]
    public required string Administrator { get; set; }

    [CommandOption("oidc")]
    public required string Oidc { get; set; }

    [CommandOption("oidc-id")]
    public required string OidcId { get; set; }

    [CommandOption("oidc-secret", EnvironmentVariable = "HPC_REGISTRAR_ARGUMENT_OIDC_SECRET")]
    public required string OidcSecret { get; set; }

    [CommandOption("oidc-ca")]
    public string? OidcCa { get; set; } = null;

    [CommandOption("hpc-host")]
    public required string HpcHost { get; set; }

    [CommandOption("hpc-port")]
    public int HpcPort { get; set; } = 22;

    [CommandOption("hpc-user")]
    public required string HpcUser { get; set; }

    [CommandOption("hpc-key", EnvironmentVariable = "HPC_REGISTRAR_ARGUMENT_HPC_KEY")]
    public required string HpcKey { get; set; }

    [CommandOption("hpc-home")]
    public required string HpcHome { get; set; }

    [CommandOption("hpc-ssdfs-root")]
    public string HpcSsdfsRoot { get; set; } = HpcOptions.DefaultSsdfsDataHome;

    public async ValueTask ExecuteAsync(IConsole console)
    {
        await using var app = BuildApp();
        await app.RunAsync();
    }

    public WebApplication BuildApp() => BuildApp(null);

    public WebApplication BuildApp(IHpcClient? hpcClient)
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(ServeCommand).Assembly.GetName().Name,
            Args = ["--urls", Listen],
        });

        Directory.CreateDirectory(Data);

        var hpcOptions = new HpcOptions(HpcHost, HpcPort, HpcUser, HpcKey, HpcHome, HpcSsdfsRoot);
        hpcClient ??= new SshNetHpcClient(hpcOptions);

        var spaceService = new IsolationSpaceService(
            hpcClient,
            new SpaceRepository(new LiteDatabase(Path.Combine(Data, "spaces.db"))),
            hpcOptions);

        builder.Services.AddSingleton(new AppOptions(Title));
        builder.Services.AddSingleton(hpcOptions);
        builder.Services.AddSingleton<IHpcClient>(hpcClient);
        builder.Services.AddSingleton(spaceService);

        builder.Services
            .AddAuthentication(CookieAuthenticationDefaults.AuthenticationScheme)
            .AddCookie(options =>
            {
                options.LoginPath = "/Account/Login";
                options.Events.OnRedirectToAccessDenied = context =>
                {
                    context.Response.StatusCode = StatusCodes.Status403Forbidden;
                    return Task.CompletedTask;
                };
            })
            .AddOpenIdConnect("oidc", options =>
            {
                options.Authority = Oidc;
                options.ClientId = OidcId;
                options.ClientSecret = OidcSecret;
                options.ResponseType = OpenIdConnectResponseType.Code;
                options.GetClaimsFromUserInfoEndpoint = true;
                options.Scope.Add("profile");
                options.Scope.Add("groups");
                options.CorrelationCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;
                options.NonceCookie.SecurePolicy = CookieSecurePolicy.SameAsRequest;

                if (OidcCa is not null)
                {
                    var trustedCa = X509Certificate2.CreateFromPem(File.ReadAllText(OidcCa));
                    var handler = new SocketsHttpHandler();
                    handler.SslOptions.RemoteCertificateValidationCallback = (_, cert, _, errors) =>
                    {
                        if (cert is null)
                        {
                            return false;
                        }

                        using var chain = new X509Chain();
                        chain.ChainPolicy.TrustMode = X509ChainTrustMode.CustomRootTrust;
                        chain.ChainPolicy.RevocationMode = X509RevocationMode.NoCheck;
                        chain.ChainPolicy.VerificationFlags = X509VerificationFlags.AllowUnknownCertificateAuthority;
                        chain.ChainPolicy.CustomTrustStore.Add(trustedCa);
                        return chain.Build(new X509Certificate2(cert));
                    };
                    options.BackchannelHttpHandler = handler;
                }
            });

        builder.Services.AddAuthorization(options =>
        {
            options.AddPolicy("Administrator", policy => policy.RequireAssertion(context =>
                context.User.FindAll("groups").Any(claim => claim.Value == Administrator)));
        });
        builder.Services.AddRazorPages();

        builder.Services.Configure<ForwardedHeadersOptions>(options =>
        {
            options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;
        });

        var app = builder.Build();

        app.UseForwardedHeaders();

        app.UseAuthentication();
        app.UseAuthorization();

        app.MapRazorPages();

        app.MapGet("/Account/Login", (string? returnUrl) =>
            Results.Challenge(
                new AuthenticationProperties
                {
                    RedirectUri = returnUrl ?? "/",
                },
                authenticationSchemes: ["oidc"]));

        app.MapPost("/Account/Logout", async (HttpContext context) =>
        {
            await context.SignOutAsync(CookieAuthenticationDefaults.AuthenticationScheme);
            return Results.Redirect("/Account/Login");
        });

        return app;
    }
}
