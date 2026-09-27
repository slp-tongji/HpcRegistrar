using System.Net;
using System.Text.RegularExpressions;

namespace YueYinqiu.HpcRegistrar.E2E;

public sealed class E2ETests : IClassFixture<E2EFixture>
{
    private const string TestKey = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIGZvb2Jhcg==";

    private readonly E2EFixture fixture;

    public E2ETests(E2EFixture fixture)
    {
        this.fixture = fixture;
    }

    [Fact]
    public async Task Unauthenticated_request_is_redirected_to_login()
    {
        var response = await fixture.AppClient.GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task User_can_create_list_and_delete_own_space()
    {
        var client = await LoginAsync("alice");

        var createHtml = await CreateSpaceAsync(client, "alpha", "alice@example.com", TestKey);
        Assert.Contains("alpha", createHtml);

        var homeHtml = await GetHomeHtmlAsync(client);
        Assert.Contains("alpha", homeHtml);

        var sshCommandPath = "/home/hpcuser/data/alpha/.ssh-command";
        Assert.Contains(sshCommandPath, fixture.Hpc.Files.Keys);
        var sshCommand = fixture.Hpc.Files[sshCommandPath];
        Assert.Contains("NEW_HOME=\"/home/hpcuser/data/alpha\"", sshCommand);
        Assert.Contains(sshCommandPath, fixture.Hpc.Executables);

        Assert.Contains("/home/hpcuser/.ssh/authorized_keys", fixture.Hpc.Files.Keys);
        var authorizedKeys = fixture.Hpc.Files["/home/hpcuser/.ssh/authorized_keys"];
        Assert.Contains("alpha", authorizedKeys);
        Assert.Contains("alice@example.com", authorizedKeys);

        Assert.Contains("/home/hpcuser/data/alpha/ssdfs", fixture.Hpc.SymbolicLinks.Keys);
        Assert.Equal("/ssdfs/datahome/hpcuser/alpha", fixture.Hpc.SymbolicLinks["/home/hpcuser/data/alpha/ssdfs"]);

        var token = ExtractAntiforgeryToken(homeHtml);
        var deleteResp = await client.PostAsync(
            "/?handler=Delete",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["name"] = "alpha",
            }));
        deleteResp.EnsureSuccessStatusCode();

        var afterDeleteHtml = await GetHomeHtmlAsync(client);
        Assert.DoesNotContain("alpha", afterDeleteHtml);
    }

    [Fact]
    public async Task User_cannot_see_another_users_space()
    {
        var alice = await LoginAsync("alice");
        await CreateSpaceAsync(alice, "bravo", "alice@example.com", TestKey);

        var bob = await LoginAsync("bob");
        var bobHtml = await GetHomeHtmlAsync(bob);

        Assert.DoesNotContain("bravo", bobHtml);
    }

    [Fact]
    public async Task Administrator_can_see_and_delete_other_users_space()
    {
        var alice = await LoginAsync("alice");
        await CreateSpaceAsync(alice, "charlie", "alice@example.com", TestKey);

        var administrator = await LoginAsync("administrator");
        var administratorHtml = await GetAdministratorHtmlAsync(administrator);
        Assert.Contains("charlie", administratorHtml);

        var token = ExtractAntiforgeryToken(administratorHtml);
        var deleteResp = await administrator.PostAsync(
            "/administration?handler=Delete",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["name"] = "charlie",
            }));
        deleteResp.EnsureSuccessStatusCode();

        var aliceHtml = await GetHomeHtmlAsync(alice);
        Assert.DoesNotContain("charlie", aliceHtml);
    }

    [Fact]
    public async Task Non_admin_cannot_access_admin_page()
    {
        var bob = await LoginAsync("bob");

        var response = await bob.GetAsync("/administration");

        Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
    }

    [Fact]
    public async Task Non_admin_cannot_delete_other_users_space()
    {
        var alice = await LoginAsync("alice");
        await CreateSpaceAsync(alice, "delta", "alice@example.com", TestKey);

        var bob = await LoginAsync("bob");
        var bobHtml = await GetHomeHtmlAsync(bob);
        var token = ExtractAntiforgeryToken(bobHtml);

        var deleteResp = await bob.PostAsync(
            "/?handler=Delete",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["name"] = "delta",
            }));
        deleteResp.EnsureSuccessStatusCode();

        var aliceHtml = await GetHomeHtmlAsync(alice);
        Assert.Contains("delta", aliceHtml);
    }

    private async Task<string> CreateSpaceAsync(HttpClient client, string name, string contact, string key)
    {
        var html = await GetHomeHtmlAsync(client);
        var token = ExtractAntiforgeryToken(html);

        var createResp = await client.PostAsync(
            "/?handler=Create",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["name"] = name,
                ["contact"] = contact,
                ["key"] = key,
            }));
        createResp.EnsureSuccessStatusCode();

        return await createResp.Content.ReadAsStringAsync();
    }

    private async Task<string> GetHomeHtmlAsync(HttpClient client)
    {
        var response = await client.GetAsync("/");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    private async Task<string> GetAdministratorHtmlAsync(HttpClient client)
    {
        var response = await client.GetAsync("/administration");
        response.EnsureSuccessStatusCode();
        return await response.Content.ReadAsStringAsync();
    }

    private async Task<HttpClient> LoginAsync(string sub)
    {
        var client = new HttpClient(fixture.CreateTrustingHandler())
        {
            BaseAddress = new Uri(fixture.AppBaseUrl),
        };

        var response = await client.GetAsync("/");
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var loginUrl = response.Headers.Location!;

        response = await client.GetAsync(loginUrl.ToString());
        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var authorizeUrl = response.Headers.Location!;

        response = await client.GetAsync(authorizeUrl.ToString());
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var form = await response.Content.ReadAsStringAsync();

        var redirectUri = ExtractHidden(form, "redirect_uri");
        var state = ExtractHidden(form, "state");
        var nonce = ExtractHidden(form, "nonce");

        response = await client.PostAsync(
            new Uri(authorizeUrl, "/authorize/choose").ToString(),
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["redirect_uri"] = redirectUri,
                ["state"] = state,
                ["nonce"] = nonce,
                ["sub"] = sub,
            }));

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        var callbackUrl = response.Headers.Location!;

        response = await client.GetAsync(callbackUrl.ToString());

        while (response.StatusCode == HttpStatusCode.Redirect)
        {
            response = await client.GetAsync(response.Headers.Location!.ToString());
        }

        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        return client;
    }

    private static string ExtractAntiforgeryToken(string html)
    {
        var m = Regex.Match(html, "name=\"__RequestVerificationToken\" type=\"hidden\" value=\"([^\"]*)\"");
        return m.Success ? m.Groups[1].Value : throw new Exception("no antiforgery token");
    }

    private static string ExtractHidden(string html, string name)
    {
        var m = Regex.Match(html, $"name=\"{name}\" value=\"([^\"]*)\"");
        return m.Success ? m.Groups[1].Value : throw new Exception($"missing hidden {name}");
    }
}
