using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kroeg.Server.Models;

namespace Kroeg.Server.Services.EntityStore
{
    public class StagingEntityStore : IEntityStore
    {
        private readonly Dictionary<string, APEntity> _entities = new Dictionary<string, APEntity>();

        public StagingEntityStore(IEntityStore next)
        {
            Bypass = next;
        }

        public async Task<APEntity> GetEntity(string id, bool doRemote)
        {
            if (_entities.ContainsKey(id)) return _entities[id];

            return await Bypass.GetEntity(id, doRemote);
        }

        public Task<APEntity> StoreEntity(APEntity entity)
        {
            _entities[entity.Id] = entity;
            return Task.FromResult(entity);
        }

        public async Task CommitChanges()
        {
            foreach (var item in _entities.ToList())
                await Bypass.StoreEntity(item.Value);

            _entities.Clear();

            await Bypass.CommitChanges();
        }

        public void TrimDown(string prefix)
        {
            foreach (var item in _entities.Keys.ToList())
            {
                if (item.StartsWith(prefix)) continue;
                var data = _entities[item].Data;
                if (data["_:origin"].Any((a) => (string) a.Primitive == "atom") && data["_:atomRetrieveUrl"].Any((a) => ((string) a.Primitive).StartsWith(prefix))) continue;

                _entities.Remove(item);
            }
                
        }

        public IEntityStore Bypass { get; }
    }
}
