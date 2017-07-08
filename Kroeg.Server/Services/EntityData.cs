using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using System.Text.RegularExpressions;
using Kroeg.ActivityStreams;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;
using System.Threading.Tasks;
using Kroeg.Server.Services.EntityStore;

namespace Kroeg.Server.Tools
{
    public class EntityData
    {
        public EntityData(IConfigurationSection kroegSection)
        {
            _kroegSection = kroegSection;
        }

        private readonly IConfigurationSection _kroegSection;

        public string BaseUri => _kroegSection.GetValue<string>("BaseUri");
        public string BaseDomain => (new Uri(BaseUri)).Host;
        public string BasePath => (new Uri(BaseUri)).AbsolutePath;

        public bool RewriteRequestScheme => _kroegSection.GetValue<bool>("RewriteRequestScheme");
        public bool UnflattenRemotely => _kroegSection.GetValue<bool>("UnflattenRemotely");
        public IConfiguration EntityNames { get; set; }

        private static readonly HashSet<string> Activities = new HashSet<string>
        {
            "Create", "Update", "Delete", "Follow", "Add", "Remove", "Like", "Block", "Undo", "Announce"
        };

        private static readonly HashSet<string> Actors = new HashSet<string>
        {
            "Actor", "Application", "Group", "Organization", "Person", "Service"
        };

        [Obsolete("hardcoded single type")]
        public bool IsActivity(string type)
        {
            return  Activities.Contains(type);
        }

        public bool IsActivity(ASObject @object)
        {
            return @object["actor"].Count > 0;
        }

        public bool IsActor(ASObject @object)
        {
            return @object["type"].Any(a => Actors.Contains((string)a.Primitive));
        }

        private string _getFormat(IEnumerable<string> type, string category, bool isRelative)
        {
            var firstformatType = type.FirstOrDefault(a => EntityNames[a.ToLower()] != null);
            if (firstformatType != null) return EntityNames[firstformatType.ToLower()];
            if (isRelative && EntityNames["+" + category] != null) return EntityNames["+" + category];
            if (EntityNames["!" + category] != null) return EntityNames["!" + category];
            return EntityNames["!fallback"];
        }

        private static string _generateSlug(string val)
        {
            if (val == null) return null;
            val = val.ToLower().Substring(0, Math.Min(val.Length, 40));

            val = Regex.Replace(val, @"[^a-z0-9\s-]", "");
            val = Regex.Replace(val, @"\s+", " ");
            return Regex.Replace(val, @"\s", "-");
        }

        private static string _shortGuid() => Guid.NewGuid().ToString().Substring(0, 8);

        private async Task<JToken> _parse(IEntityStore store, JObject data, JToken curr, string thing)
        {
            if (thing.StartsWith("$"))
                return curr ?? data.SelectToken(thing);
            if (thing.StartsWith("%"))
                return curr?.SelectToken(thing.Replace('%', '$'));
            if (thing == "resolve")
                return curr == null ? null : (await store?.GetEntity(curr.ToObject<string>(), false))?.Data?.Serialize();
            if (thing == "guid")
                return curr ?? Guid.NewGuid().ToString();
            if (thing == "shortguid")
                return curr ?? _shortGuid();
            if (thing == "lower")
                return curr?.ToObject<string>()?.ToLower();
            if (thing == "slug")
                return _generateSlug(curr?.ToObject<string>());
            if (thing.StartsWith("'"))
                return curr ?? thing.Substring(1);

            if (thing.All(char.IsNumber) && curr?.Type == JTokenType.Array) return ((JArray) curr)[int.Parse(thing)];

            return curr;
        }

        private async Task<string> _runCommand(IEntityStore store, JObject data, IEnumerable<string> args)
        {
            JToken val = null;
            foreach (var item in args)
            {
                val = await _parse(store, data, val, item);
            }

            return (val ?? "unknown").ToObject<string>();
        }

        private async Task<string> _parseUriFormat(IEntityStore store, JObject data, string format)
        {
            var result = new StringBuilder();
            var index = 0;
            while (index < format.Length)
            {
                var nextEscape = format.IndexOf("\\", index);
                if (nextEscape == -1) nextEscape = int.MaxValue;
                var nextStart = format.IndexOf("${", index);
                if (nextStart == -1) nextStart = int.MaxValue;
                if (nextEscape < nextStart && nextEscape < format.Length)
                {
                    result.Append(format.Substring(index, nextEscape - index));
                    result.Append(format[nextEscape + 1]);
                    index = nextEscape + 2;
                }
                else if (nextStart < nextEscape && nextStart < format.Length)
                {
                    result.Append(format.Substring(index, nextStart - index));

                    var end = format.IndexOf("}", nextStart);
                    if (end == -1) throw new Exception("invalid format for URI");

                    var contents = format.Substring(nextStart + 2, end - nextStart - 2).Split('|');
                    result.Append(await _runCommand(store, data, contents));

                    index = end + 1;
                }
                else if (nextStart == int.MaxValue && nextEscape == int.MaxValue)
                {
                    result.Append(format.Substring(index));
                    break;
                }
            }

            return result.ToString();
        }

        public async Task<string> UriFor(IEntityStore store, ASObject @object, string category = null, string parentId = null)
        {
            var types = @object["type"].Select(a => (string)a.Primitive).ToList();

            if (category == null)
                if (@object["actor"].Any())
                    category = "activity";
                else if (types.Any(a => Actors.Contains(a)))
                    category = "actor";
                else
                    category = "object";

            var format = _getFormat(types, category, parentId != null);
            var result = await _parseUriFormat(store, @object.Serialize(), format);
            if (parentId != null && result.StartsWith("+"))
                return parentId + "/" + result.Substring(1).ToLower();

            result = result.ToLower();
            if (Uri.IsWellFormedUriString(result, UriKind.Absolute))
                return result;

            return BaseUri + result.ToLower(); 
        }

        public async Task<string> FindUnusedID(IEntityStore entityStore, ASObject @object, string category = null, string parentId = null)
        {
            var types = @object["type"].Select(a => (string)a.Primitive).ToList();
            var format = _getFormat(types, category, parentId != null);

            string uri = await UriFor(entityStore, @object, category, parentId);
            if (format.Contains("guid")) // is GUID-based, can just regenerate
            {
                while (await entityStore.GetEntity(uri, false) != null) uri = await UriFor(entityStore, @object, category, parentId);                
            }
            else if (await entityStore.GetEntity(uri, false) != null)
            {
                string shortGuid = _shortGuid();
                while (await entityStore.GetEntity($"{uri}-{shortGuid}", false) != null) shortGuid = _shortGuid();
                return $"{uri}-{shortGuid}";
            }

            return uri;
        }
    }
}
