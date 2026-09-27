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

    public void Insert(SpaceOwnership ownership) => collection.Insert(ownership);

    public SpaceOwnership? FindByName(string name) => collection.FindById(name);

    public IEnumerable<SpaceOwnership> FindByOwner(string owner) =>
        collection.Query().Where(ownership => ownership.Owner == owner).ToEnumerable();

    public IEnumerable<SpaceOwnership> FindAll() => collection.FindAll();

    public bool Delete(string name) => collection.Delete(name);
}
