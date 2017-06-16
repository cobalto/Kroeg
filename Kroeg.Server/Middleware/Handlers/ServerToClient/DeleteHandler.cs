using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kroeg.ActivityStreams;
using Kroeg.Server.Models;
using Kroeg.Server.Services.EntityStore;

namespace Kroeg.Server.Middleware.Handlers.ServerToClient
{
    public class DeleteHandler : BaseHandler
    {
        private static readonly HashSet<string> DeleteWhitelist = new HashSet<string> { "id", "type", "created", "updated" };

        public DeleteHandler(StagingEntityStore entityStore, APEntity mainObject, APEntity actor, APEntity targetBox) : base(entityStore, mainObject, actor, targetBox)
        {
        }

        public override async Task<bool> Handle()
        {
            if (MainObject.Type != "Delete") return true;

            var oldObject = await EntityStore.GetEntity((string) MainObject.Data["object"].Single().Primitive, true);
            var newData = oldObject.Data;
            foreach (var kv in newData)
            {
                if (!DeleteWhitelist.Contains(kv.Key))
                    kv.Value.Clear();
            }

            newData.Replace("type", new ASTerm("Tombstone"));
            newData.Replace("deleted", new ASTerm(DateTime.Now.ToString("o")));

            var newObject = APEntity.From(newData);
            await EntityStore.StoreEntity(newObject);

            return true;
        }
    }
}
