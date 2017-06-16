using System.Net.Http;
using System.Threading.Tasks;
using Kroeg.ActivityStreams;
using Kroeg.Server.Models;
using Kroeg.Server.Tools;

namespace Kroeg.Server.Services.EntityStore
{
    public class RetrievingEntityStore : IEntityStore
    {
        public IEntityStore Next { get; }

        private readonly EntityFlattener _entityFlattener;

        public RetrievingEntityStore(IEntityStore next, EntityFlattener entityFlattener)
        {
            Next = next;
            _entityFlattener = entityFlattener;
        }

        public async Task<APEntity> GetEntity(string id, bool doRemote)
        {
            if (id == "https://www.w3.org/ns/activitystreams#Public")
            {
                var aso = new ASObject();
                aso.Replace("type", new ASTerm("Collection"));
                aso.Replace("id", new ASTerm("https://www.w3.org/ns/activitystreams#Public"));

                var ent = APEntity.From(aso);
                return ent;
            }

            APEntity entity = null;
            if (Next != null) entity = await Next.GetEntity(id, doRemote);

            if (entity != null || !doRemote) return entity;

            var htc = new HttpClient();
            htc.DefaultRequestHeaders.TryAddWithoutValidation("Accept", "application/ld+json; profile=\"https://www.w3.org/ns/activitystreams\", application/activity+json");

            var @object = ASObject.Parse(await htc.GetStringAsync(id));
            await _entityFlattener.FlattenAndStore(Next, @object);
            await Next.CommitChanges();

            return await Next.GetEntity(id, true);
        }

        public async Task<APEntity> StoreEntity(APEntity entity) => Next == null ? entity : await Next.StoreEntity(entity);

        public async Task CommitChanges()
        {
            if (Next != null) await Next.CommitChanges();
        }
    }
}
