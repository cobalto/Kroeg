using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kroeg.Server.Models;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;

namespace Kroeg.Server.Services
{
    public class ActivityService
    {
        private readonly APContext _context;

        private class OriginatingCreateJson
        {
            [JsonProperty("type")]
            public string Type { get; set; }

            [JsonProperty("object")]
            public string Object { get; set; }
        }

        public ActivityService(APContext context)
        {
            _context = context;
        }

        public async Task<APEntity>
            DetermineOriginatingCreate(string id)
        {
            var serializedJson = JsonConvert.SerializeObject(new OriginatingCreateJson { Type = "Create", Object = id });

            return await _context.Entities.FromSql("SELECT * from \"Entities\" WHERE \"SerializedData\"@> {0}::jsonb", serializedJson).FirstOrDefaultAsync();
        }
    }
}
