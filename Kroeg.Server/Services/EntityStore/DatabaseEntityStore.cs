using System.Threading.Tasks;
using Kroeg.Server.Models;
using Microsoft.EntityFrameworkCore;
using System;

namespace Kroeg.Server.Services.EntityStore
{
    public class DatabaseEntityStore : IEntityStore
    {
        private readonly APContext _context;

        public DatabaseEntityStore(APContext context)
        {
            _context = context;
        }

        public IEntityStore Bypass => null;

        public async Task<APEntity> GetEntity(string id, bool doRemote)
        {
            var entity = await _context.Entities.FirstOrDefaultAsync(a => a.Id == id);
            if (entity == null || (!entity.IsOwner && doRemote && entity.Id.StartsWith("http") && (DateTime.Now - entity.Updated).TotalDays > 7)) return null; // mini-cache

            return entity;
        }

        public async Task<APEntity> StoreEntity(APEntity entity)
        {
            var exists = await _context.Entities.FirstOrDefaultAsync(a => a.Id == entity.Id);
            if (exists == null)
            {
                entity.Updated = DateTime.Now;
                await _context.Entities.AddAsync(entity);
            }
            else
            {
                exists.SerializedData = entity.SerializedData;
                exists.Updated = DateTime.Now;
                exists.Type = entity.Type;
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
