using System.Threading.Tasks;
using Kroeg.Server.Models;

namespace Kroeg.Server.Services.EntityStore
{
    public interface IEntityStore
    {
        Task<APEntity> GetEntity(string id, bool doRemote);
        Task<APEntity> StoreEntity(APEntity entity);

        Task CommitChanges();
    }
}
