using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kroeg.ActivityStreams;
using Kroeg.Server.Models;
using Kroeg.Server.Services.EntityStore;

namespace Kroeg.Server.Tools
{
    public class EntityFlattener
    {
        private readonly EntityData _configuration;

        public EntityFlattener(EntityData configuration)
        {
            _configuration = configuration;
        }

        public async Task<APEntity> FlattenAndStore(IEntityStore store, ASObject @object, Dictionary<string, APEntity> dict = null)
        {
            dict = dict ?? new Dictionary<string, APEntity>();
            var main = await Flatten(@object, dict);

            foreach (var entity in dict.ToArray())
                dict[entity.Key] = await store.StoreEntity(entity.Value);

            return dict[main.Id];
        }

        public async Task<APEntity> Flatten(ASObject @object, Dictionary<string, APEntity> flattened = null)
        {
            if (flattened == null)
                flattened = new Dictionary<string, APEntity>();

            var mainEntity = await _flatten(@object, flattened);

            return flattened[mainEntity.Id];
        }

        public async Task<ASObject> Unflatten(IEntityStore store, APEntity entity, int depth = 3, Dictionary<string, APEntity> mapped = null)
        {
            if (mapped == null)
                mapped = new Dictionary<string, APEntity>();
            var unflattened = await _unflatten(store, entity, depth, mapped);

            return unflattened;
        }

        private static readonly HashSet<string> IdHolding = new HashSet<string>
        {
            "subject", "relationship", "actor", "attributedTo", "attachment", "bcc", "bto", "cc", "context", "current", "first", "generator", "icon", "image", "inReplyTo", "items", "instrument", "orderedItems", "last", "location", "next", "object", "oneOf", "anyOf", "origin", "prev", "preview", "replies", "result", "audience", "partOf", "tag", "target", "to", "describes", "formerType", "streams", "endpoints", "uploadMedia", "proxyUrl"
        };

        private static readonly HashSet<string> MayNotFlatten = new HashSet<string>
        {
            "next", "prev", "first", "last", "bcc", "bto", "cc", "to", "audience"
        };

        private async Task<APEntity> _flatten(ASObject @object, IDictionary<string, APEntity> entities)
        {

            var entity = new APEntity();

            if (@object["id"].Count == 0)
            {
                @object["id"].Add(new ASTerm(_configuration.UriFor(@object)));
                entity.IsOwner = true;
            }

            entity.Id = (string) @object["id"].First().Primitive;
            var t = (string)@object["type"].FirstOrDefault()?.Primitive;
            if (t?.StartsWith("_") != false) t = "Unknown";
            entity.Type = t;

            foreach (var kv in @object)
            {
                if (!IdHolding.Contains(kv.Key)) continue;
                foreach (var value in kv.Value)
                {
                    if (value.SubObject == null) continue;
                    if (value.SubObject["id"].Any(a => a.Primitive == null)) continue; // transient object

                    var subObject = await _flatten(value.SubObject, entities);

                    value.Primitive = subObject.Id;
                    value.SubObject = null;
                }
            }

            entity.Data = @object;
            entities[(string)@object["id"].First().Primitive] = entity;

            return entity;
        }

        private static async Task<ASObject> _unflatten(IEntityStore store, APEntity entity, int depth, IDictionary<string, APEntity> alreadyMapped)
        {
            if (depth == 0)
                return entity.Data;

            var @object = entity.Data;
            alreadyMapped[(string)@object["id"].First().Primitive] = entity;

            foreach (var kv in @object)
            {
                if (!IdHolding.Contains(kv.Key) || MayNotFlatten.Contains(kv.Key)) continue;
                foreach (var value in kv.Value)
                {
                    if (value.Primitive == null) continue;
                    var id = (string)value.Primitive;

                    if (alreadyMapped.ContainsKey(id)) continue;

                    var obj = await store.GetEntity(id, false);
                    if (obj == null) continue;
                    value.Primitive = null;
                    value.SubObject = await _unflatten(store, obj, depth - 1, alreadyMapped);
                }
            }

            return @object;
        }
    }
}
