using System;
using System.Collections.Generic;
using System.Linq;
using Kroeg.ActivityStreams;

namespace Kroeg.Server.Tools
{
    public class EntityData
    {
        public string BaseUri { get; set; }
        public string BaseDomain => (new Uri(BaseUri)).Host;
        public bool RewriteRequestScheme { get; set; }

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

        public string UriFor(ASObject @object)
        {
            var types = @object["type"].Select(a => (string)a.Primitive).ToList();

            if (types.Any(a => Activities.Contains(a)))
            {
                return BaseUri + "activity/" + Guid.NewGuid();
            }

            if (types.Any(a => Actors.Contains(a)))
            {
                return BaseUri + "actor/" + types.First(Actors.Contains).ToLower() + "/" + Guid.NewGuid();
            }

            return BaseUri + "entity/" + string.Join("", types.First().ToLower().Where(char.IsLetter)) + "/" + Guid.NewGuid();
        }
    }
}
