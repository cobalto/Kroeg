using System.Threading.Tasks;
using Kroeg.Server.Models;

namespace Kroeg.Server.Services.EntityStore
{
    public interface IEntityStore
    {
        Task<APEntity> GetEntity(string id, bool doRemote);
        Task<APEntity> StoreEntity(APEntity entity);

        Task CommitChanges();

        IEntityStore Bypass { get; }
    }

    public static class EntityStoreHelpers
    {
        public static T Find<T>(this IEntityStore entityStore) where T : IEntityStore
        {
            while (entityStore != null)
            {
                if (entityStore.Bypass.GetType() == typeof(T)) return (T) entityStore.Bypass;
                entityStore = entityStore.Bypass;
            }

            return default(T);
        }
    }
}
