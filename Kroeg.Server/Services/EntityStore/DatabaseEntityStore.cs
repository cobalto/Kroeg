using System.Threading.Tasks;
using Kroeg.Server.Models;
using Microsoft.EntityFrameworkCore;

namespace Kroeg.Server.Services.EntityStore
{
    public class DatabaseEntityStore : IEntityStore
    {
        private readonly APContext _context;

        public DatabaseEntityStore(APContext context)
        {
            _context = context;
        }


        public async Task<APEntity> GetEntity(string id, bool doRemote) => await _context.Entities.FirstOrDefaultAsync(a => a.Id == id);

        public async Task<APEntity> StoreEntity(APEntity entity)
        {
            var exists = await GetEntity(entity.Id, false);
            if (exists == null)
            {
                await _context.Entities.AddAsync(entity);
            }
            else
            {
                exists.IsOwner = entity.IsOwner;
                exists.SerializedData = entity.SerializedData;
                entity = exists;
            }

            return entity;
        }

        public async Task CommitChanges()
        {
            await _context.SaveChangesAsync();
        }
    }
}
