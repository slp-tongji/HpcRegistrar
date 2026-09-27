using LiteDB;

namespace YueYinqiu.HpcRegistrar.Services;

public sealed record SpaceOwnership(
    [property: BsonId] string Name,
    string Owner,
    string Contact,
    string Fingerprint,
    string Key,
    string KeyLine);
