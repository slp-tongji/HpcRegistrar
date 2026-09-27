namespace YueYinqiu.HpcRegistrar.E2E.MockOidc;

public sealed record MockUser(string Sub, string Name, IReadOnlyList<string> Groups);

public sealed class MockOidcState
{
    public IReadOnlyList<MockUser> Users { get; }

    public MockOidcState(IReadOnlyList<MockUser> users)
    {
        Users = users;
    }
}
