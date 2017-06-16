using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kroeg.ActivityStreams;
using Kroeg.Server.Models;
using Kroeg.Server.Services.EntityStore;

namespace Kroeg.Server.Tools
{
    public static class Static
    {
        public static Task<APEntity> GetEntity(this IEntityStore store, List<ASTerm> term, bool doRemote)
        {
            if (term.Count != 1) throw new ArgumentException("Need to get exactly one term", nameof(term));

            var termToGet = (string) term[0].Primitive;
            if (term[0].SubObject != null) termToGet = (string) term[0].SubObject["id"].First().Primitive;

            return store.GetEntity(termToGet, doRemote);
        }
    }
}
