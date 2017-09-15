using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kroeg.ActivityStreams;
using Kroeg.Server.Models;
using Kroeg.Server.Services.EntityStore;
using Microsoft.AspNetCore.Http;
using System;
using Kroeg.Server.Salmon;

namespace Kroeg.Server.Tools
{
    public class EntityFlattener
    {
        private readonly EntityData _configuration;
        private readonly APContext _context;

        public EntityFlattener(APContext context, EntityData configuration)
        {
            _configuration = configuration;
            _context = context;
        }

        public async Task<APEntity> FlattenAndStore(IEntityStore store, ASObject @object, bool generateId = true, Dictionary<string, APEntity> dict = null)
        {
            dict = dict ?? new Dictionary<string, APEntity>();
            var main = await Flatten(store, @object, generateId, dict);

            foreach (var entity in dict.ToArray())
                dict[entity.Key] = await store.StoreEntity(entity.Value);

            return dict[main.Id];
        }

        public async Task<APEntity> Flatten(IEntityStore store, ASObject @object, bool generateId = true, Dictionary<string, APEntity> flattened = null)
        {
            if (flattened == null)
                flattened = new Dictionary<string, APEntity>();

            var mainEntity = await _flatten(store, @object, generateId, flattened);

            return flattened[mainEntity.Id];
        }

        public async Task<ASObject> Unflatten(IEntityStore store, APEntity entity, int depth = 3, Dictionary<string, APEntity> mapped = null)
        {
            if (mapped == null)
                mapped = new Dictionary<string, APEntity>();

            if (entity.Id != null)
            {
                var e = await store.GetEntity(entity.Id, false);
                if (e?.IsOwner == true) entity.IsOwner = true;
            }

            var unflattened = await _unflatten(store, entity, depth, mapped, _configuration.UnflattenRemotely);

            return unflattened;
        }

        private static readonly HashSet<string> IdHolding = new HashSet<string>
        {
            "subject", "relationship", "actor", "attributedTo", "attachment", "bcc", "bto", "cc", "context", "current", "first", "generator", "icon", "image", "inReplyTo", "items", "instrument", "orderedItems", "last", "location", "next", "object", "oneOf", "anyOf", "origin", "prev", "preview", "replies", "result", "audience", "partOf", "tag", "target", "to", "describes", "formerType", "streams", "publicKey"
        };

        private static readonly HashSet<string> MayNotFlatten = new HashSet<string>
        {
            "next", "prev", "first", "last", "bcc", "bto", "cc", "to", "audience", "endpoints"
        };


        private async Task<ASObject> _getEndpoints(APEntity entity)
        {
            var data = entity.Data;
            var idu = new Uri(entity.Id);

            var basePath = $"{idu.Scheme}://{idu.Host}{_configuration.BasePath}";

            var endpoints = new ASObject();
            endpoints.Replace("oauthAuthorizationEndpoint", new ASTerm(basePath + "auth/oauth?id=" + Uri.EscapeDataString(entity.Id)));
            endpoints.Replace("oauthTokenEndpoint", new ASTerm(basePath + "auth/token?"));
            endpoints.Replace("settingsEndpoint", new ASTerm(basePath + "settings/auth"));
            endpoints.Replace("uploadMedia", new ASTerm((string)data["outbox"].Single().Primitive));
            endpoints.Replace("relevantObjects", new ASTerm(basePath + "settings/relevant"));
            endpoints.Replace("proxyUrl", new ASTerm(basePath + "auth/proxy"));
            endpoints.Replace("jwks", new ASTerm(basePath + "auth/jwks?id=" + Uri.EscapeDataString(entity.Id)));
            endpoints.Replace("id", new ASTerm(entity.Id + "#endpoints"));

            data.Replace("endpoints", new ASTerm(endpoints));

            var key = await _context.GetKey(entity.Id);
            var salm = new MagicKey(key.PrivateKey);
            var pemData = salm.AsPEM;

            var keyObj = new ASObject();
            keyObj.Replace("owner", new ASTerm(entity.Id));
            keyObj.Replace("publicKeyPem", new ASTerm(pemData));
            keyObj.Replace("id", new ASTerm($"{entity.Id}#key"));

            data.Replace("publicKey", new ASTerm(keyObj));
            data.Replace("sharedInbox", new ASTerm(_configuration.BaseUri + "/auth/sharedInbox"));
            return data;
        }

        private async Task<APEntity> _flatten(IEntityStore store, ASObject @object, bool generateId, IDictionary<string, APEntity> entities, string parentId = null)
        {

            var entity = new APEntity();

            if (@object["id"].Count == 0)
            {
                if (!generateId) return null;
                @object["id"].Add(new ASTerm(await _configuration.FindUnusedID(store, @object, null, parentId)));
                entity.IsOwner = true;
            }

            entity.Id = (string) @object["id"].First().Primitive;
            var t = (string)@object["type"].FirstOrDefault()?.Primitive;
            if (t?.StartsWith("_") != false && t?.StartsWith("_:") != true) t = "Unknown";
            entity.Type = t;

            foreach (var kv in @object)
            {
                if (!IdHolding.Contains(kv.Key)) continue;
                foreach (var value in kv.Value)
                {
                    if (value.SubObject == null) continue;
                    if (value.SubObject["id"].Any(a => a.Primitive == null)) continue; // transient object

                    var subObject = await _flatten(store, value.SubObject, generateId, entities, entity.Id);
                    if (subObject == null) continue;

                    value.Primitive = subObject.Id;
                    value.SubObject = null;
                }
            }

            entity.Data = @object;
            entities[entity.Id] = entity;

            return entity;
        }


        private static HashSet<string> _avoidFlatteningTypes = new HashSet<string> { "OrderedCollection", "Collection", "_replies", "_likes", "_shares", "_:LazyLoad" };

        private async Task<ASObject> _unflatten(IEntityStore store, APEntity entity, int depth, IDictionary<string, APEntity> alreadyMapped, bool remote)
        {
            if (depth == 0)
                return entity.Data;

            var @object = entity.Data;
            if (_configuration.IsActor(@object) && entity.IsOwner)
                @object = await _getEndpoints(entity);

            var myid = (string)@object["id"].FirstOrDefault()?.Primitive;
            if (myid != null)
                alreadyMapped[myid] = entity;

            @object["bto"].Clear();
            @object["bcc"].Clear();

            foreach (var kv in @object)
            {
                foreach (var value in kv.Value)
                {
                    if (value.SubObject != null) value.SubObject = await _unflatten(store, APEntity.From(value.SubObject), depth - 1, alreadyMapped, remote);
                    if (value.Primitive == null) continue;
                    if (!IdHolding.Contains(kv.Key) || MayNotFlatten.Contains(kv.Key)) continue;
                    var id = (string)value.Primitive;

                    if (alreadyMapped.ContainsKey(id)) continue;

                    var obj = await store.GetEntity(id, false);
                    if (obj == null || _avoidFlatteningTypes.Contains(obj.Type) || (!remote && !obj.IsOwner)) continue;
                    value.Primitive = null;
                    value.SubObject = await _unflatten(store, obj, depth - 1, alreadyMapped, remote);
                }
            }

            return @object;
        }
    }
}
