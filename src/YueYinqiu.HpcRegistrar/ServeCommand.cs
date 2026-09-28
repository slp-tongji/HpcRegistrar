using System.Security.Cryptography.X509Certificates;
using CliFx;
using CliFx.Binding;
using CliFx.Infrastructure;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Authentication.Cookies;
using Microsoft.AspNetCore.Authentication.OpenIdConnect;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.IdentityModel.Protocols.OpenIdConnect;
using Microsoft.IdentityModel.Tokens;
using YueYinqiu.HpcRegistrar.Services;

namespace YueYinqiu.HpcRegistrar;

[Command]
public sealed partial class ServeCommand : ICommand
{
    [CommandOption("listen")]
    public required string Listen { get; set; }

    [CommandOption("oidc")]
    public required string Oidc { get; set; }

    [CommandOption("oidc-id")]
    public required string OidcId { get; set; }

    [CommandOption("oidc-secret", EnvironmentVariable = "HPC_REGISTRAR_ARGUMENT_OIDC_SECRET")]
    public required string OidcSecret { get; set; }

    [CommandOption("oidc-ca")]
    public FileInfo? OidcCa { get; set; } = null;

    [CommandOption("hpc-host")]
    public required string HpcHost { get; set; }

    [CommandOption("hpc-port")]
    public required int HpcPort { get; set; }

    [CommandOption("hpc-user")]
    public required string HpcUser { get; set; }

    [CommandOption("hpc-key")]
    public required FileInfo HpcKey { get; set; }

    [CommandOption("hpc-host-key")]
    public required string HpcHostKey { get; set; }

    public async ValueTask ExecuteAsync(IConsole console)
    {
        await using var app = BuildApp();
        await app.RunAsync();
    }

    public WebApplication BuildApp()
    {
        var builder = WebApplication.CreateBuilder(new WebApplicationOptions
        {
            ApplicationName = typeof(ServeCommand).Assembly.GetName().Name,
            Args = ["--urls", Listen],
        });

        var spaceService = new IsolationSpaceService(
            () => new SshNetHpcClient(HpcHost, HpcPort, HpcUser, HpcKey, HpcHostKey),
            HpcUser);

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
                // OIDC provider（Dex）会定期轮换签名密钥，而 OpenIdConnect 中间件会按较长周期
                //（默认 AutomaticRefreshInterval = 1 天）缓存 discovery/JWKS 元数据。当缓存的 JWKS
                // 落后于 Dex 的轮换时，重新登录会收到一个用新 kid 签名的 token，验签时按 kid 在旧
                // 缓存里匹配不到任何密钥，抛出 SecurityTokenSignatureKeyNotFoundException。
                //
                // 中间件对此的处理（RefreshOnIssuerKeyNotFound 默认开启）只是调用 RequestRefresh()
                // 把配置标记为「下次要重新拉取」，但当前这次请求仍然会失败，最终表现为一个裸 500。
                // 更糟的是，登录回调 /signin-oidc?code=... 里的 authorization code 是单次有效的，且在
                // 验签失败之前就已被消费（先 RedeemAuthorizationCode 再 ValidateToken），所以刷新同一个
                // 回调地址会拿死 code 去换 token，再次失败，用户被迫手动重输地址才能恢复。
                //
                // 因此在这里精确拦截「密钥轮换导致的验签失败」这一种情况：标记已处理并重定向回登录页，
                // 让浏览器重新走一遍授权流程。由于上面已经触发过 RequestRefresh，这次重登录会拉到最新的
                // JWKS 并直接成功，用户无感。其它类型的登录失败（取消登录、code 过期、网络错误等）保持
                // 默认行为（500），便于排查真正的问题。
                options.Events.OnRemoteFailure = context =>
                {
                    if (context.Failure is SecurityTokenSignatureKeyNotFoundException)
                    {
                        context.HandleResponse();
                        context.Response.Redirect("/Account/Login");
                    }
                    return Task.CompletedTask;
                };

                if (OidcCa is not null)
                {
                    var trustedCa = X509Certificate2.CreateFromPem(File.ReadAllText(OidcCa.FullName));
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

        builder.Services.AddAuthorization();
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
