using System;
using System.Collections.Generic;
using System.Linq;
using System.Text;
using Kroeg.ActivityStreams;
using Microsoft.Extensions.Configuration;
using Newtonsoft.Json.Linq;

namespace Kroeg.Server.Tools
{
    public class EntityData
    {
        public string BaseUri { get; set; }
        public string BaseDomain => (new Uri(BaseUri)).Host;
        public bool RewriteRequestScheme { get; set; }
        public IConfiguration EntityNames { get; set; }

        private static readonly HashSet<string> Activities = new HashSet<string>
        {
            "Create", "Update", "Delete", "Follow", "Add", "Remove", "Like", "Block", "Undo", "Announce"
        };

        private static readonly HashSet<string> Actors = new HashSet<string>
        {
            "Actor", "Application", "Group", "Organization", "Person", "Service"
        };

        public bool IsActivity(string type)
        {
            return  Activities.Contains(type);
        }

        public bool IsActor(ASObject @object)
        {
            return @object["type"].Any(a => Actors.Contains((string)a.Primitive));
        }

        private string _getFormat(IEnumerable<string> type, string category)
        {
            var firstformatType = type.FirstOrDefault(a => EntityNames[a.ToLower()] != null);
            if (firstformatType != null) return EntityNames[firstformatType.ToLower()];
            if (EntityNames["!" + category] != null) return EntityNames["!" + category];
            return EntityNames["!fallback"];
        }

        private JToken _parse(JObject data, JToken curr, string thing)
        {
            if (thing.StartsWith("$"))
                return curr ?? data.SelectToken(thing);
            if (thing == "guid")
                return curr ?? Guid.NewGuid().ToString();
            if (thing == "lower")
                return curr?.ToObject<string>()?.ToLower();
            if (thing.StartsWith("'"))
                return curr ?? thing.Substring(1);

            if (thing.All(char.IsNumber) && curr?.Type == JTokenType.Array) return ((JArray) curr)[int.Parse(thing)];

            return curr;
        }

        private string _runCommand(JObject data, IEnumerable<string> args)
        {
            JToken val = null;
            foreach (var item in args)
            {
                val = _parse(data, val, item);
            }

            return (val ?? "unknown").ToObject<string>();
        }

        private string _parseUriFormat(JObject data, string format)
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
                    result.Append(_runCommand(data, contents));

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

        public string UriFor(ASObject @object)
        {
            var types = @object["type"].Select(a => (string)a.Primitive).ToList();
            var category = "object";

            if (@object["actor"].Any())
                category = "activity";
            else if (types.Any(a => Actors.Contains(a)))
            {
                category = "actor";
            }

            var format = _getFormat(types, category);
            var result = _parseUriFormat(@object.Serialize(), format);

            return BaseUri + result.ToLower(); 
        }
    }
}
