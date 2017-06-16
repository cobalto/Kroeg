using System.Collections.Generic;
using System.Linq;
using Microsoft.EntityFrameworkCore;
using System.Threading.Tasks;
using Kroeg.ActivityStreams;
using Kroeg.Server.Models;
using Kroeg.Server.Services.EntityStore;
using Kroeg.Server.Tools;

namespace Kroeg.Server.Services
{
    public class CollectionTools
    {
        private readonly APContext _context;
        private readonly IEntityStore _entityStore;
        private readonly EntityData _configuration;

        public CollectionTools(APContext context, IEntityStore entityStore, EntityData configuration)
        {
            _context = context;
            _entityStore = entityStore;
            _configuration = configuration;
        }

        public async Task<int> Count(string id)
        {
            var entity = await _entityStore.GetEntity(id, true);
            if (entity.IsOwner)
                return await _context.CollectionItems.CountAsync(a => a.CollectionId == id);

            var data = entity.Data;
            if (data["totalItems"].Any())
                return (int) data["totalItems"].Single().Primitive;

            return -1;
        }

        public async Task<List<APEntity>> GetItems(string id, int fromId = int.MaxValue, int count = 10)
            => await _context.CollectionItems.Where(a => a.CollectionId == id && a.CollectionItemId < fromId).OrderByDescending(a => a.CollectionItemId).Take(count).Include(a => a.Element).Select(a => a.Element).ToListAsync();

        public async Task<List<APEntity>> GetAll(string id)
            => await _context.CollectionItems.Include(a => a.Element).OrderByDescending(a => a.CollectionItemId).Select(a => a.Element).ToListAsync();

        public async Task<CollectionItem> AddToCollection(APEntity collection, APEntity entity)
        {
            var ci = new CollectionItem
            {
                Collection = collection,
                Element = entity
            };

            await _context.CollectionItems.AddAsync(ci);

            return ci;
        }

        public async Task RemoveFromCollection(APEntity collection, string id)
        {
            var item = await _context.CollectionItems.FirstOrDefaultAsync(a => a.CollectionId == collection.Id && a.ElementId == id);
            if (item != null)
                _context.CollectionItems.Remove(item);
        }

        public async Task RemoveFromCollection(APEntity collection, APEntity entity)
        {
            await RemoveFromCollection(collection, entity.Id);
        }

        public APEntity NewCollection(ASObject mold = null, string type = null)
        {
            if (mold == null) mold = new ASObject();
            mold["type"].Add(new ASTerm("OrderedCollection"));
            var owner = mold["id"].Count < 1;
            if (owner)
                mold["id"].Add(new ASTerm(_configuration.UriFor(mold)));

            var entity = new APEntity
            {
                Id = (string)mold["id"].First().Primitive,
                Data = mold,
                Type = type ?? "OrderedCollection",
                IsOwner = owner
            };

            return entity;
        }
    }
}
