using System.Net;
using System.Text.RegularExpressions;
using YueYinqiu.HpcRegistrar.Services;

namespace YueYinqiu.HpcRegistrar.E2E;

public sealed class E2ETests : IClassFixture<E2EFixture>
{
    private const string Key1 = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIGZvb2Jhcg==";
    private const string Key2 = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIGJhcmJheg==";
    private const string Key3 = "ssh-ed25519 AAAAC3NzaC1lZDI1NTE5AAAAIHF1eHV4eQ==";

    private readonly E2EFixture fixture;

    public E2ETests(E2EFixture fixture)
    {
        this.fixture = fixture;
    }

    private static string Fp(string key) => SshPublicKeyParser.GetFingerprint(key)!;

    [Fact]
    public async Task Unauthenticated_request_is_redirected_to_login()
    {
        var response = await fixture.AppClient.GetAsync("/");

        Assert.Equal(HttpStatusCode.Redirect, response.StatusCode);
        Assert.Contains("/Account/Login", response.Headers.Location!.ToString());
    }

    [Fact]
    public async Task User_can_add_and_remove_own_key()
    {
        var alice = await LoginAsync("alice");

        var addHtml = await AddKeyAsync(alice, Key1);
        Assert.Contains(Fp(Key1), addHtml);

        var spaceName = SpaceName.FromSub("alice");
        var sshCommandPath = $"/home/hpcuser/data/{spaceName}/.ssh-command";
        Assert.Contains(sshCommandPath, fixture.Hpc.Files.Keys);
        Assert.Contains($"/home/hpcuser/data/{spaceName}", fixture.Hpc.Files[sshCommandPath]);
        Assert.Contains(sshCommandPath, fixture.Hpc.Executables);

        Assert.Contains("/home/hpcuser/.ssh/authorized_keys", fixture.Hpc.Files.Keys);
        var authorizedKeys = fixture.Hpc.Files["/home/hpcuser/.ssh/authorized_keys"];
        Assert.Contains(Key1, authorizedKeys);

        Assert.Contains($"/home/hpcuser/data/{spaceName}/ssdfs", fixture.Hpc.SymbolicLinks.Keys);

        var removeHtml = await RemoveKeyAsync(alice, Fp(Key1));
        Assert.DoesNotContain(Fp(Key1), removeHtml);

        var afterRemove = fixture.Hpc.Files["/home/hpcuser/.ssh/authorized_keys"];
        Assert.DoesNotContain(Key1, afterRemove);
    }

    [Fact]
    public async Task Users_have_independent_spaces()
    {
        var alice = await LoginAsync("alice");
        await AddKeyAsync(alice, Key2);

        var bob = await LoginAsync("bob");
        var bobHtml = await GetHomeHtmlAsync(bob);
        Assert.DoesNotContain(Fp(Key2), bobHtml);

        await AddKeyAsync(bob, Key3);

        var authorizedKeys = fixture.Hpc.Files["/home/hpcuser/.ssh/authorized_keys"];
        Assert.Contains(Key2, authorizedKeys);
        Assert.Contains(Key3, authorizedKeys);
        Assert.Contains($"/home/hpcuser/data/{SpaceName.FromSub("alice")}/.ssh-command", authorizedKeys);
        Assert.Contains($"/home/hpcuser/data/{SpaceName.FromSub("bob")}/.ssh-command", authorizedKeys);
    }

    private async Task<string> AddKeyAsync(HttpClient client, string key)
    {
        var html = await GetHomeHtmlAsync(client);
        var token = ExtractAntiforgeryToken(html);

        var resp = await client.PostAsync(
            "/?handler=AddKey",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["key"] = key,
            }));
        resp.EnsureSuccessStatusCode();

        return await resp.Content.ReadAsStringAsync();
    }

    private async Task<string> RemoveKeyAsync(HttpClient client, string fingerprint)
    {
        var html = await GetHomeHtmlAsync(client);
        var token = ExtractAntiforgeryToken(html);

        var resp = await client.PostAsync(
            "/?handler=RemoveKey",
            new FormUrlEncodedContent(new Dictionary<string, string>
            {
                ["__RequestVerificationToken"] = token,
                ["fingerprint"] = fingerprint,
            }));
        resp.EnsureSuccessStatusCode();

        return await resp.Content.ReadAsStringAsync();
    }

    private async Task<string> GetHomeHtmlAsync(HttpClient client)
    {
        var response = await client.GetAsync("/");
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
