namespace YueYinqiu.HpcRegistrar.Services;

public sealed record HpcOptions(
    string Host,
    int Port,
    string Username,
    FileInfo PrivateKeyPath,
    string Home,
    string SsdfsDataHome)
{
    public static string DefaultSsdfsDataHome => "/ssdfs/datahome";
}
