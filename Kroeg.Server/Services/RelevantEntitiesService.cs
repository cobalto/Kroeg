using Kroeg.ActivityStreams;
using Kroeg.Server.Models;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Kroeg.Server.Services
{
    public class RelevantEntitiesService
    {
        private readonly APContext _context;

        public RelevantEntitiesService(APContext context)
        {
            _context = context;
        }

        private class RelevantObjectJson
        {
            [JsonProperty("type")]
            public string Type { get; set; }
            [JsonProperty("object")]
            public string Object { get; set; }
            [JsonProperty("actor")]
            public string Actor { get; set; }
        }

        private class FollowerJson
        {
            [JsonProperty("followers")]
            public string Followers { get; set; }
        }

        private async Task<List<APEntity>> _search(object obj)
        {
            return await _context.Entities.FromSql("SELECT * FROM \"Entities\" WHERE \"SerializedData\" @> {0}::jsonb", JsonConvert.SerializeObject(
                obj, new JsonSerializerSettings { NullValueHandling = NullValueHandling.Ignore })).ToListAsync();
        }

        public async Task<List<APEntity>> FindRelevantObject(string authorId, string objectType, string objectId)
        {
            return await _search(new RelevantObjectJson { Type = objectType, Object = objectId, Actor = authorId });
        }

        public async Task<List<APEntity>> FindEntitiesWithFollowerId(string followerId)
        {
            return await _search(new FollowerJson { Followers = followerId });
        }
    }
}
