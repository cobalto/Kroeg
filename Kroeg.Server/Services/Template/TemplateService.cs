using Kroeg.Server.Models;
using Kroeg.Server.Services.EntityStore;
using Kroeg.Server.Tools;
using Newtonsoft.Json.Linq;
using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net;
using System.Text;
using System.Threading.Tasks;

namespace Kroeg.Server.Services.Template
{
    public class TemplateService
    {
        public Dictionary<string, List<TemplateItem>> Templates { get; } = new Dictionary<string, List<TemplateItem>>();

        private string _base = "templates/";
        private EntityData _entityData;

        public TemplateService(EntityData entityData)
        {
            _entityData = entityData;
            _parse(_base);
        }

        private void _parse(string dir)
        {
            foreach (var file in Directory.EnumerateFiles(dir))
                _parseFile(file);

            foreach (var subdir in Directory.EnumerateDirectories(dir))
                _parse(subdir);
        }

        private void _parseFile(string path)
        {
            var data = File.ReadAllText(path);
            Templates[path.Substring(_base.Length, path.Length - _base.Length - 5).Replace('\\', '/')] = TemplateParser.Parse(data);
        }

        private async Task<bool> _parseCondition(APEntity entity, IEntityStore entityStore, string text)
        {
            var data = text.Split(' ');
            if (data.Length == 2)
            {
                if (text == "is Activity") return _entityData.IsActivity(entity.Data);
                if (text == "is Collection") return entity.Data["type"].Any((a) => (string)a.Primitive == "Collection" || (string)a.Primitive == "OrderedCollection");
                if (text == "is CollectionPage") return entity.Data["type"].Any((a) => (string)a.Primitive == "CollectionPage" || (string)a.Primitive == "OrderedCollectionPage");
                else if (data[0] == "is") return entity.Data["type"].Any((a) => (string)a.Primitive == data[1]);
                else if (data[0] == "has") return entity.Data[data[1]].Any();
                return false;
            }

            var value = data[0];
            var arr = entity.Data[data[2]];
            switch (data[1])
            {
                case "in":
                    return arr.Any((a) => (string)a.Primitive == value);
            }

            return false;
        }

        private async Task<string> _parseCommand(APEntity entity, IEntityStore entityStore, string command)
        {
            JToken _inbetween = null;
            bool isHtml = false;
            foreach (var asf in command.Split(' '))
            {
                if (asf.StartsWith("$"))
                {
                    var name = asf.Substring(1);
                    if (_inbetween != null) continue;
                    var obj = new List<JToken>();
                    foreach (var item in entity.Data[name])
                        if (item.SubObject == null)
                            obj.Add(JToken.FromObject(item.Primitive));
                        else
                            obj.Add(item.SubObject.Serialize(false));
                    if (obj.Count == 0) _inbetween = null;
                    else _inbetween = new JArray(obj.ToArray());
                }
                else if (asf.StartsWith("%"))
                {
                    if (_inbetween == null) continue;
                    string id;
                    if (_inbetween.Type == JTokenType.Array)
                        id = _inbetween[0].ToObject<string>();
                    else
                        id = _inbetween.ToObject<string>();
                    var toCheck = await entityStore.GetEntity(id, true);
                    var obj = new List<JToken>();
                    foreach (var item in toCheck.Data[asf.Substring(1)])
                        if (item.SubObject == null)
                            obj.Add(JToken.FromObject(item.Primitive));
                        else
                            obj.Add(item.SubObject.Serialize(false));
                    if (obj.Count == 0) _inbetween = null;
                    else _inbetween = new JArray(obj.ToArray());
                }
                else if (asf.StartsWith("'"))
                    _inbetween = _inbetween ?? asf.Substring(1);
                else if (asf == "ishtml")
                    isHtml = true;
                else if (asf.StartsWith("render:"))
                {
                    var template = asf.Substring("render:".Length);
                    if (_inbetween == null) return await ParseTemplate(template, entityStore, entity);
                    string id;
                    if (_inbetween.Type == JTokenType.Array)
                        id = _inbetween[0].ToObject<string>();
                    else
                        id = _inbetween.ToObject<string>();
                    var newEntity = await entityStore.GetEntity(id, true);
                    return await ParseTemplate(template, entityStore, newEntity);
                }
            }

            string text;
            if (_inbetween.Type == JTokenType.Array) text = _inbetween[0].ToObject<string>();
            else text = _inbetween.ToObject<string>();

            if (isHtml) return text;
            return WebUtility.HtmlEncode(text);
        }

        public async Task<string> ParseTemplate(string template, IEntityStore entityStore, APEntity entity)
        {
            var templ = Templates[template];
            var builder = new StringBuilder();
            for (int i = 0; i < templ.Count; i++)
            {
                var item = templ[i];
                switch (item.Type)
                {
                    case "text":
                        builder.Append(item.Data);
                        break;
                    case "if":
                    case "while":
                        if (!await _parseCondition(entity, entityStore, item.Data.Split(new[] { ' ' }, 2)[1]))
                            i = item.Offset - 1;
                        break;
                    case "jump":
                        i = item.Offset - 1;
                        break;
                    case "end":
                        var begin = templ[item.Offset];
                        if (begin.Type == "while")
                            i = item.Offset - 1;
                        break;
                    case "command":
                        builder.Append(await _parseCommand(entity, entityStore, item.Data));
                        break;
                }
            }

            return builder.ToString();
        }
    }
}
