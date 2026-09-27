using LiteDB;

namespace YueYinqiu.HpcRegistrar.Services;

public sealed record SpaceKey(string Fingerprint, string Key, string KeyLine);

public sealed record SpaceOwnership(
    [property: BsonId] string Name,
    string Owner,
    List<SpaceKey> Keys);
