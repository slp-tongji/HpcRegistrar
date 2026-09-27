using LiteDB;

namespace YueYinqiu.HpcRegistrar.Services;

public sealed class SpaceRepository
{
    private readonly ILiteCollection<SpaceOwnership> collection;

    public SpaceRepository(LiteDatabase database)
    {
        this.collection = database.GetCollection<SpaceOwnership>();
        this.collection.EnsureIndex(ownership => ownership.Owner);
    }

    public SpaceOwnership? FindByOwner(string owner) =>
        collection.Query().Where(ownership => ownership.Owner == owner).FirstOrDefault();

    public IEnumerable<SpaceOwnership> FindAll() => collection.FindAll();

    public void Upsert(SpaceOwnership ownership) => collection.Upsert(ownership);
}
