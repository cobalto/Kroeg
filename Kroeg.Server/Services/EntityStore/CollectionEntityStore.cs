
using System;
using System.Collections.Generic;
using System.Linq;
using System.Security.Claims;
using System.Threading.Tasks;
using Kroeg.ActivityStreams;
using Kroeg.Server.Configuration;
using Kroeg.Server.Models;
using Microsoft.AspNetCore.Http;

namespace Kroeg.Server.Services.EntityStore
{
    public class CollectionEntityStore : IEntityStore
    {
        private readonly Dictionary<string, APEntity> _entities = new Dictionary<string, APEntity>();
        private readonly CollectionTools _collectionTools;

        private async Task<ASObject> _buildPage(APEntity entity, int from_id)
        {
            var collection = entity.Data;
            var items = await _collectionTools.GetItems(entity.Id, from_id, 10);
            var hasItems = items.Any();
            var page = new ASObject();
            page["type"].Add(new ASTerm("OrderedCollectionPage"));
            page["summary"].Add(new ASTerm("A collection"));
            page["id"].Add(new ASTerm(entity.Id + "?from_id=" + (hasItems ? from_id : 0)));
            page["partOf"].Add(new ASTerm(collection));
            if (collection["attributedTo"].Any())
                page["attributedTo"].Add(collection["attributedTo"].First());
            if (items.Count > 0)
                page["next"].Add(new ASTerm(entity.Id + "?from_id=" + (items.Last().CollectionItemId - 1).ToString()));
            page["orderedItems"].AddRange(items.Select(a => new ASTerm(a.ElementId)));
            return page;
        }

        private async Task<ASObject> _buildCollection(APEntity entity)
        {
            var collection = entity.Data;
            collection["current"].Add(new ASTerm(entity.Id));
            collection["totalItems"].Add(new ASTerm(await _collectionTools.Count(entity.Id)));
            var item = await _collectionTools.GetItems(entity.Id, count: 1);
            if (item.Any())
                collection["first"].Add(new ASTerm(entity.Id + $"?from_id={item.First().CollectionItemId + 1}"));
            else
                collection["first"].Add(new ASTerm(entity.Id + $"?from_id=0"));
            return collection;
        }

        public CollectionEntityStore(CollectionTools collectionTools, IEntityStore next)
        {
            _collectionTools = collectionTools;
            Bypass = next;
        }

        public async Task<APEntity> GetEntity(string id, bool doRemote)
        {
            string query = null;
            string parsedId = null;

            if (id.Contains("?"))
            {
                var split = id.Split(new char[] { '?' }, 2);
                query = split[1];
                parsedId = split[0];
            }

            var entity = await Bypass.GetEntity(id, doRemote);
            if (entity == null) entity = await Bypass.GetEntity(parsedId, doRemote);
            if (entity?.Type.StartsWith("_") != true) return entity;

            if (query == null)
            {
                return APEntity.From(await _buildCollection(entity), true);
            }
            else
            {
                int from_id = 0;
                foreach (var item in query.Split('&'))
                {
                    var kv = item.Split('=');
                    if (kv[0] == "from_id" && kv.Length > 1)
                        from_id = int.Parse(kv[1]);
                }

                return APEntity.From(await _buildPage(entity, from_id));
            }
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