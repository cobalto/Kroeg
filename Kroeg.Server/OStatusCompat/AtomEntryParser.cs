using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using System.Linq;
using System.Threading.Tasks;
using System.Xml.Linq;
using Kroeg.ActivityStreams;
using Kroeg.Server.Models;
using Kroeg.Server.Services.EntityStore;
using Kroeg.Server.Tools;

namespace Kroeg.Server.OStatusCompat
{
    public class AtomEntryParser
    {
        private static string _objectTypeToType(string objectType)
        {
            if (objectType == null) return null;

            // length: 35
            if (objectType.StartsWith("http://activitystrea.ms/schema/1.0/"))
                objectType = objectType.Substring(35);
            else if (objectType.StartsWith("http://ostatus.org/schema/1.0/"))
                objectType = objectType.Substring(30);
            if (!objectType.StartsWith("http"))
                objectType = objectType.Substring(0, 1).ToUpperInvariant() + objectType.Substring(1);

            if (objectType == "Comment") objectType = "Note";

            return objectType;
        }

        private static readonly XNamespace Atom = "http://www.w3.org/2005/Atom";
        private static readonly XNamespace AtomMedia = "http://purl.org/syndication/atommedia";
        private static readonly XNamespace AtomThreading = "http://purl.org/syndication/thread/1.0";
        private static readonly XNamespace ActivityStreams = "http://activitystrea.ms/spec/1.0/";
        private static readonly XNamespace PortableContacts = "http://portablecontacts.net/spec/1.0";
        private static readonly XNamespace OStatus = "http://ostatus.org/schema/1.0";
        private static readonly XNamespace NoNamespace = "";

        private readonly IEntityStore _entityStore;
        private readonly EntityData _entityConfiguration;
        private readonly APContext _context;

        private ASObject _parseAuthor(XElement element)
        {
            var ao = new ASObject();

            ao.Replace("type", new ASTerm("Person"));

            // set preferredUsername and name
            {
                var atomName = element.Element(Atom + "name")?.Value;
                var pocoDisplayName = element.Element(PortableContacts + "displayName")?.Value;
                var pocoPreferredUsername = element.Element(PortableContacts + "preferredUsername")?.Value;

                ao.Replace("preferredUsername", new ASTerm(pocoPreferredUsername ?? atomName));
                ao.Replace("name", new ASTerm(pocoDisplayName ?? pocoPreferredUsername ?? atomName));
            }

            // set summary
            {
                var atomSummary = element.Element(Atom + "summary")?.Value;
                var pocoNote = element.Element(PortableContacts + "note")?.Value;

                ao.Replace("summary", new ASTerm(pocoNote ?? atomSummary));
            }

            string retrievalUrl = null;

            {
                foreach (var link in element.Elements(Atom + "link"))
                {
                    var rel = link.Attribute(NoNamespace + "rel")?.Value;
                    var type = link.Attribute(NoNamespace + "type")?.Value;
                    var href = link.Attribute(NoNamespace + "href")?.Value;

                    switch (rel)
                    {
                        case "avatar":
                            var avatarObject = new ASObject();
                            avatarObject.Replace("id", new ASTerm((string) null)); // transient object!
                            avatarObject.Replace("type", new ASTerm("Image"));
                            avatarObject.Replace("mediaType", new ASTerm(type));
                            var width = link.Attribute(AtomMedia + "width")?.Value;
                            var height = link.Attribute(AtomMedia + "height")?.Value;

                            if (width != null && height != null)
                            {
                                avatarObject.Replace("width",
                                    new ASTerm(int.Parse(width)));
                                avatarObject.Replace("height",
                                    new ASTerm(int.Parse(height)));
                            }

                            avatarObject.Replace("url", new ASTerm(href));

                            ao["icon"].Add(new ASTerm(avatarObject));
                            break;
                        case "alternate":
                            if (type == "text/html")
                            {
                                if (retrievalUrl == null)
                                    retrievalUrl = href;

                                ao["_:atomAlternate"].Add(new ASTerm(href));
                            }
                            break;
                        case "self":
                            if (type == "application/atom+xml")
                                retrievalUrl = href;
                            break;
                    }
                }
            }

            // should be Mastodon *and* GNU social compatible: Mastodon uses uri as id

            if (element.Element(Atom + "id") != null)
                ao.Replace("id", new ASTerm(element.Element(Atom + "id")?.Value));
            else
                ao.Replace("id", new ASTerm(element.Element(Atom + "uri")?.Value));

            if (element.Element(Atom + "uri") != null)
                ao["url"].Add(new ASTerm(element.Element(Atom + "uri")?.Value));

            if (element.Element(Atom + "email") != null)
                ao["email"].Add(new ASTerm(element.Element(Atom + "email")?.Value));

            foreach (var url in element.Elements(PortableContacts + "urls"))
                ao["url"].Add(new ASTerm(url.Element(PortableContacts + "value")?.Value));

            if (retrievalUrl != null)
                ao.Replace("_:atomRetrieveUrl", new ASTerm(retrievalUrl));
            
            return ao;
        }

        [SuppressMessage("ReSharper", "PossibleNullReferenceException")]
        private async Task<ASTerm> _parseActivityObject(XElement element, string authorId, bool isActivity = false)
        {
            if (!isActivity && element.Element(ActivityStreams + "verb") != null) return await _parseActivity(element, authorId);
            if (await _isSelf(element.Element(Atom + "id")?.Value)) return new ASTerm(element.Element(Atom + "id")?.Value);

            var ao = new ASObject();
            ao.Replace("id", new ASTerm("atom:object:" + element.Element(Atom + "id")?.Value));

            var objectType = _objectTypeToType(element.Element(ActivityStreams + "object-type")?.Value);
            if (objectType == "Person")
                return new ASTerm(_parseAuthor(element));

            ao.Replace("type", new ASTerm(objectType));


            if (element.Element(Atom + "summary") != null)
                ao.Replace("summary", new ASTerm(element.Element(Atom + "summary")?.Value));
            if (element.Element(Atom + "published") != null)
                ao.Replace("published", new ASTerm(element.Element(Atom + "published")?.Value));
            if (element.Element(Atom + "updated") != null)
                ao.Replace("updated", new ASTerm(element.Element(Atom + "updated")?.Value));

            ao.Replace("content", new ASTerm(element.Element(Atom + "content")?.Value));
            var mediaType = element.Element(Atom + "content")?.Attribute(NoNamespace + "type")?.Value;

            if (mediaType != null)
            {
                if (mediaType == "text") mediaType = "text/plain";
                if (mediaType.Contains("/")) ao.Replace("mediaType", new ASTerm(mediaType));
            }

            if (element.Element(OStatus + "conversation") != null)
                ao.Replace("_:conversation", new ASTerm(element.Element(OStatus + "conversation").Attribute(NoNamespace + "ref").Value));

            if (element.Element(AtomThreading + "in-reply-to") != null)
                ao.Replace("inReplyTo", new ASTerm("atom:object:" + element.Element(AtomThreading + "in-reply-to").Attribute(NoNamespace + "ref").Value));

            foreach (var tag in element.Elements(Atom + "category"))
            {
                var val = tag.Attribute(NoNamespace + "term").Value;

                var tagao = new ASObject();
                tagao["id"].Add(new ASTerm($"{_entityConfiguration.BaseUri}/tag/{val}"));
                tagao["name"].Add(new ASTerm("#" + val));
                tagao["type"].Add(new ASTerm("Tag"));

                ao["tag"].Add(new ASTerm(tagao));
            }

            string retrievalUrl = null;

            foreach (var link in element.Elements(Atom + "link"))
            {
                var rel = link.Attribute(NoNamespace + "rel").Value;
                var type = link.Attribute(NoNamespace + "type")?.Value;
                var href = link.Attribute(NoNamespace + "href").Value;

                if (rel == "self" && type == "application/atom+xml")
                    retrievalUrl = href;
                else if (rel == "alternate" && type == "text/html")
                {
                    ao["url"].Add(new ASTerm(href));

                    if (retrievalUrl == null) retrievalUrl = href;
                }
                else if (rel == "mentioned")
                {
                    if (href == "http://activityschema.org/collection/public")
                        href = "https://www.w3.org/ns/activitystreams#Public";

                    ao["to"].Add(new ASTerm(href));
                }
                else if (rel == "enclosure")
                {
                    // image
                    var subAo = new ASObject();
                    subAo["id"].Add(new ASTerm((string)null));
                    subAo["url"].Add(new ASTerm(href));
                    subAo["mediaType"].Add(new ASTerm(type));

                    switch (type.Split('/')[0])
                    {
                        case "image":
                            subAo.Replace("type", new ASTerm("Image"));
                            break;
                        case "video":
                            subAo.Replace("type", new ASTerm("Video"));
                            break;
                        default:
                            continue;
                    }

                    if (link.Attribute(NoNamespace + "length") != null)
                        subAo["fileSize"].Add(new ASTerm(int.Parse(link.Attribute(NoNamespace + "length").Value)));

                    ao["attachment"].Add(new ASTerm(subAo));
                }
            }
            
            return new ASTerm(ao);
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

        private async Task<ASTerm> _findRelevantObject(string authorId, string objectType, string objectId)
        {
            return new ASTerm((await _context.Entities.FromSql("SELECT * FROM \"Entities\" WHERE \"SerializedData\" @> {0}::jsonb", JsonConvert.SerializeObject(
                new RelevantObjectJson { Type = objectType, Object = objectId, Actor = authorId })).FirstOrDefaultAsync())?.Id);
            // how?
        }

        [SuppressMessage("ReSharper", "PossibleNullReferenceException")]
        private async Task<ASTerm> _parseActivity(XElement element, string authorId)
        {
            if (await _isSelf(element.Element(Atom + "id").Value)) return new ASTerm(await _fixActivityToObjectId(element.Element(Atom + "id").Value));

            var ao = new ASObject();
            ao.Replace("id", new ASTerm("atom:activity:" + element.Element(Atom + "id").Value));

            var verb = _objectTypeToType(element.Element(ActivityStreams + "verb")?.Value) ?? "Post";
            var originalVerb = verb;

            if (verb == "Unfavorite") verb = "Undo";
            if (verb == "Unfollow") verb = "Undo";
            if (verb == "Request-friend") return null;

            if (verb == "Post") verb = "Create";
            else if (verb == "Share") verb = "Announce";
            else if (verb == "Favorite") verb = "Like";

            if (!_entityConfiguration.IsActivity(verb)) return null;

            ao.Replace("type", new ASTerm(verb));

            if (element.Element(Atom + "title") != null)
                ao.Replace("summary", new ASTerm(element.Element(Atom + "title").Value));
            if (element.Element(Atom + "published") != null)
                ao.Replace("published", new ASTerm(element.Element(Atom + "published").Value));
            if (element.Element(Atom + "updated") != null)
                ao.Replace("updated", new ASTerm(element.Element(Atom + "updated").Value));

            if (authorId != null)
                ao.Replace("actor", new ASTerm(authorId));

            if (element.Element(Atom + "author") != null)
            {
                var newAuthor = _parseAuthor(element.Element(Atom + "author"));
                ao.Replace("actor", new ASTerm(newAuthor));
                authorId = (string)newAuthor["id"].First().Primitive;
            }

            string retrievalUrl = null;

            foreach (var link in element.Elements(Atom + "link"))
            {
                var rel = link.Attribute(NoNamespace + "rel").Value;
                var type = link.Attribute(NoNamespace + "type")?.Value;
                var href = link.Attribute(NoNamespace + "href").Value;

                if (rel == "self" && type == "application/atom+xml")
                    retrievalUrl = href;
                else if (rel == "alternate" && type == "text/html")
                {
                    ao["url"].Add(new ASTerm(href));

                    if (retrievalUrl == null) retrievalUrl = href;
                }
                else if (rel == "mentioned")
                {
                    if (href == "http://activityschema.org/collection/public")
                        href = "https://www.w3.org/ns/activitystreams#Public";

                    ao["to"].Add(new ASTerm(href));
                }
            }

            if (retrievalUrl != null)
                ao.Replace("_:atomRetrieveUrl", new ASTerm(retrievalUrl));

            if (element.Element(ActivityStreams + "object") != null)
            {
                var parsedActivityObject = await _parseActivityObject(element.Element(ActivityStreams + "object"), authorId);

                if (verb == "Undo" && originalVerb == "Unfavorite")
                {
                    parsedActivityObject = await _findRelevantObject(authorId, "Like", _getId(parsedActivityObject));
                }
                else if (verb == "Undo" && originalVerb == "Unfollow")
                    parsedActivityObject = await _findRelevantObject(authorId, "Follow", _getId(parsedActivityObject));

                ao.Replace("object", parsedActivityObject);
            }
            else
            {
                ao.Replace("object", await _parseActivityObject(element, authorId, true));
            }

            return new ASTerm(ao);
        }

        private string _getId(ASTerm term)
        {
            if (term.Primitive != null) return (string) term.Primitive;
            return (string) term.SubObject["id"].Single().Primitive;
        }

        private async Task<bool> _isSelf(string id)
        {
            var getId = await _entityStore.GetEntity(id, false);
            return getId?.IsOwner == true;
        }

        private async Task<string> _fixActivityToObjectId(string id)
        {
            if (!await _isSelf(id)) return id;
            return (string) (await _entityStore.GetEntity(id, false)).Data["object"].First().Primitive;
        }

        [SuppressMessage("ReSharper", "PossibleNullReferenceException")]
        private async Task<ASObject> _parseFeed(XElement element)
        {
            var ao = new ASObject();
            ao.Replace("type", new ASTerm("OrderedCollectionPage"));
            ao.Replace("_:origin", new ASTerm("atom"));
            ao.Replace("id", new ASTerm(element.Element(Atom + "id").Value));

            if (element.Element(Atom + "title") != null)
                ao.Replace("summary", new ASTerm(element.Element(Atom + "title").Value));
            if (element.Element(Atom + "updated") != null)
                ao.Replace("updated", new ASTerm(element.Element(Atom + "updated").Value));

            var author = _parseAuthor(element.Element(Atom + "author"));
            ao.Replace("attributedTo", new ASTerm(author));

            var authorId = (string) author["id"].First().Primitive;

            foreach (var entry in element.Elements(Atom + "entry"))
                ao["orderedItems"].Add(await _parseActivity(entry, authorId));


            foreach (var link in element.Elements(Atom + "link"))
            {
                var rel = link.Attribute(NoNamespace + "rel").Value;
                var type = link.Attribute(NoNamespace + "type")?.Value;
                var href = link.Attribute(NoNamespace + "href").Value;

                if (rel == "alternate" && type == "text/html")
                {
                    ao["url"].Add(new ASTerm(href));
                }
                else switch (rel)
                {
                    case "salmon":
                        ao["_:salmonUrl"].Add(new ASTerm(href));
                        break;
                    case "hub":
                        ao["_:hubUrl"].Add(new ASTerm(href));
                        break;
                    case "prev":
                        ao["prev"].Add(new ASTerm(href));
                        break;
                    case "next":
                        ao["next"].Add(new ASTerm(href));
                        break;
                }
            }

            return ao;
        }

        public AtomEntryParser(IEntityStore entityStore, EntityData entityConfiguration, APContext context)
        {
            _entityStore = entityStore;
            _entityConfiguration = entityConfiguration;
            _context = context;
        }

        public async Task<ASObject> Parse(XDocument doc)
        {
            if (doc.Root?.Name == Atom + "entry")
                return (await _parseActivity(doc.Root, null)).SubObject;
            return await _parseFeed(doc.Root);
        }
    }
}
