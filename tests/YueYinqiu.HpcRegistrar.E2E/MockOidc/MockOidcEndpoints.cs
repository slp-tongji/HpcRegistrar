using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;

namespace YueYinqiu.HpcRegistrar.E2E.MockOidc;

public static class MockOidcEndpoints
{
    private sealed record PendingCode(string Sub, string Nonce, IReadOnlyList<string> Groups);

    public static void MapMockOidc(this WebApplication app, MockOidcState state)
    {
        var codes = new ConcurrentDictionary<string, PendingCode>();
        var accessTokens = new ConcurrentDictionary<string, string>();
        var issuer = new Lazy<string>(() => app.Urls.First());

        app.MapGet("/.well-known/openid-configuration", (HttpContext context) =>
        {
            var baseUrl = $"{context.Request.Scheme}://{context.Request.Host}";
            return Results.Json(new
            {
                issuer = issuer.Value,
                authorization_endpoint = $"{baseUrl}/authorize",
                token_endpoint = $"{baseUrl}/token",
                userinfo_endpoint = $"{baseUrl}/userinfo",
                end_session_endpoint = $"{baseUrl}/endsession",
            });
        });

        app.MapGet("/authorize", (HttpContext context) =>
        {
            var redirectUri = context.Request.Query["redirect_uri"].ToString();
            var stateQuery = context.Request.Query["state"].ToString();
            var nonce = context.Request.Query["nonce"].ToString();

            var optionsHtml = string.Join("", state.Users.Select(user =>
                $"<option value=\"{user.Sub}\">{user.Name} ({user.Sub})</option>"));

            var html = """
                <!doctype html>
                <html><body>
                  <h2>Mock OIDC 登录</h2>
                  <form method="post" action="/authorize/choose">
                    <input type="hidden" name="redirect_uri" value="__REDIRECT__" />
                    <input type="hidden" name="state" value="__STATE__" />
                    <input type="hidden" name="nonce" value="__NONCE__" />
                    <label>选择用户:</label>
                    <select name="sub">__OPTIONS__</select>
                    <button type="submit">登录</button>
                  </form>
                </body></html>
                """;

            html = html
                .Replace("__REDIRECT__", redirectUri)
                .Replace("__STATE__", stateQuery)
                .Replace("__NONCE__", nonce)
                .Replace("__OPTIONS__", optionsHtml);

            return Results.Content(html, "text/html; charset=utf-8");
        });

        app.MapPost("/authorize/choose", async (HttpContext context) =>
        {
            var form = await context.Request.ReadFormAsync();
            var redirectUri = form["redirect_uri"].ToString();
            var stateVal = form["state"].ToString();
            var nonce = form["nonce"].ToString();
            var sub = form["sub"].ToString();
            var user = state.Users.FirstOrDefault(candidate => candidate.Sub == sub);

            var code = Guid.NewGuid().ToString("N");
            codes[code] = new PendingCode(sub, nonce, user?.Groups ?? Array.Empty<string>());

            var separator = redirectUri.Contains('?') ? "&" : "?";
            return Results.Redirect($"{redirectUri}{separator}code={code}&state={Uri.EscapeDataString(stateVal)}");
        });

        app.MapPost("/token", async (HttpContext context) =>
        {
            var form = await context.Request.ReadFormAsync();
            var code = form["code"].ToString();

            if (!codes.TryRemove(code, out var pending))
            {
                return Results.Json(new { error = "invalid_grant" }, statusCode: 400);
            }

            var accessToken = Guid.NewGuid().ToString("N");
            accessTokens[accessToken] = pending.Sub;

            var idToken = BuildUnsignedIdToken(issuer.Value, pending, accessToken);

            return Results.Json(new
            {
                id_token = idToken,
                access_token = accessToken,
                token_type = "Bearer",
            });
        });

        app.MapGet("/userinfo", (HttpContext context) =>
        {
            var authorization = context.Request.Headers.Authorization.ToString();
            if (!authorization.StartsWith("Bearer ", StringComparison.OrdinalIgnoreCase))
            {
                return Results.Json(new { error = "invalid_token" }, statusCode: 401);
            }

            var token = authorization["Bearer ".Length..].Trim();
            if (!accessTokens.TryGetValue(token, out var sub))
            {
                return Results.Json(new { error = "invalid_token" }, statusCode: 401);
            }

            var user = state.Users.FirstOrDefault(candidate => candidate.Sub == sub);
            return Results.Json(new { sub, name = user?.Name ?? sub, groups = user?.Groups ?? Array.Empty<string>() });
        });

        app.MapGet("/endsession", (HttpContext context) =>
        {
            var postLogoutRedirectUri = context.Request.Query["post_logout_redirect_uri"].ToString();
            return string.IsNullOrEmpty(postLogoutRedirectUri)
                ? Results.Content("<html><body>已登出</body></html>", "text/html")
                : Results.Redirect(postLogoutRedirectUri);
        });
    }

    private static string BuildUnsignedIdToken(string issuer, PendingCode pending, string accessToken)
    {
        var now = DateTimeOffset.UtcNow;
        var header = new { alg = "none", typ = "JWT" };
        var payload = new Dictionary<string, object?>
        {
            ["iss"] = issuer,
            ["sub"] = pending.Sub,
            ["aud"] = "test-client",
            ["nonce"] = pending.Nonce,
            ["iat"] = now.ToUnixTimeSeconds(),
            ["exp"] = now.AddMinutes(10).ToUnixTimeSeconds(),
            ["groups"] = pending.Groups,
        };

        var headerB64 = Base64Url(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(header)));
        var payloadB64 = Base64Url(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(payload)));

        return $"{headerB64}.{payloadB64}.";
    }

    private static string Base64Url(byte[] data)
    {
        return Convert.ToBase64String(data).TrimEnd('=').Replace('+', '-').Replace('/', '_');
    }
}
