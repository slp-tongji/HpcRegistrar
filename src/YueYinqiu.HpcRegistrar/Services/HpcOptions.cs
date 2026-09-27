namespace YueYinqiu.HpcRegistrar.Services;

public sealed record HpcOptions(
    string Host,
    int Port,
    string Username,
    string PrivateKeyPath,
    string Home,
    string SsdfsDataHome)
{
    public static string DefaultSsdfsDataHome => "/ssdfs/datahome";
}
