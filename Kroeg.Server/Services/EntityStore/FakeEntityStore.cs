
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
    public class FakeEntityStore : IEntityStore
    {
        private FakeEntityService _fakeEntityService;
        public FakeEntityStore(FakeEntityService fakeEntityService, IEntityStore next)
        {
            _fakeEntityService = fakeEntityService;
            Bypass = next;
        }

        public async Task<APEntity> GetEntity(string id, bool doRemote)
        {
            string fragment = null;
            string parsedId = null;

            // no fragment, so who cares
            if (!id.Contains("#"))
                return await Bypass.GetEntity(id, doRemote);

            var split = id.Split(new char[] { '#' }, 2);
            fragment = split[1];
            parsedId = split[0];

            // try local get
            var entity = await Bypass.GetEntity(id, false);
            if (entity != null) return entity;

            // doesn't exist, so try non-fragment
            entity = await Bypass.GetEntity(parsedId, false);
            if (entity != null && entity.IsOwner)
            {
                // exists, and I own it!
                var result = await _fakeEntityService.BuildFakeObject(entity, fragment);
                if (result == null) return null;
                return APEntity.From(result, true);
            }

            if (!doRemote) return null;

            return await Bypass.GetEntity(id, true);
        }

        public Task<APEntity> StoreEntity(APEntity entity)
        {
            return Bypass.StoreEntity(entity);
        }

        public async Task CommitChanges()
        {
            await Bypass.CommitChanges();
        }

        public IEntityStore Bypass { get; }
    }
}