﻿using Kroeg.Server.Models;
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

        public string PageTemplate { get; private set; }

        public class Registers {
            internal List<string> Load { get; set; }
            internal int Accumulator { get; set; }

            public Dictionary<string, APEntity> UsedEntities { get; set; } = new Dictionary<string, APEntity>();
        }

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
            if (path == "templates/page.html") PageTemplate = data;
            Templates[path.Substring(_base.Length, path.Length - _base.Length - 5).Replace('\\', '/')] = TemplateParser.Parse(data);
        }

        private async Task<bool> _parseCondition(APEntity entity, IEntityStore entityStore, string text, Registers regs)
        {
            return await _parseCommand(entity, entityStore, text, regs) != null;
        }

        private async Task<string> _parseCommand(APEntity entity, IEntityStore entityStore, string command, Registers regs)
        {
            JToken _inbetween = null;
            bool isHtml = false;
            var splitCommand = command.Split(' ');
            for (var i = 0; i < splitCommand.Length; i++)
            {
                var asf = splitCommand[i];
                var isAnd = false;
                var previousResult = _inbetween;
                if (asf.StartsWith("&"))
                {
                    isAnd = true;
                    asf = asf.Substring(1);
                }

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
                    regs.UsedEntities[id] = toCheck;
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
                    if (_inbetween == null) return await ParseTemplate(template, entityStore, entity, regs);
                    string id;
                    if (_inbetween.Type == JTokenType.Array)
                        _inbetween = _inbetween[0];

                    if (_inbetween.Type == JTokenType.Object)
                        id = _inbetween["id"].ToObject<string>();
                    else
                        id = _inbetween.ToObject<string>();
                    var newEntity = await entityStore.GetEntity(id, true);
                    regs.UsedEntities[id] = newEntity;
                    return await ParseTemplate(template, entityStore, newEntity, regs);
                }
                else if (asf == "load")
                {
                    regs.Load = new List<string>();
                    if (_inbetween != null)
                    foreach (var item in (JArray) _inbetween)
                    {
                        regs.Load.Add(item.Value<string>());
                    }
                    regs.Accumulator = -1;
                    _inbetween = null;
                }
                else if (asf == "item")
                {
                    if (_inbetween == null && regs.Accumulator >= 0 && regs.Accumulator < regs.Load.Count) _inbetween = regs.Load[regs.Accumulator];
                }
                else if (asf == "next")
                {
                    regs.Accumulator++;
                    _inbetween = (regs.Accumulator < regs.Load.Count) ? regs.Load[regs.Accumulator] : null;
                }
                else if (asf == "server")
                {
                    _inbetween = "server";
                }
                else if (asf == "is")
                {
                    var test = splitCommand[++i];
                    if (test == "Activity") _inbetween = _entityData.IsActivity(entity.Data) ? "Activity" : _inbetween;
                    else if (test == "Collection") _inbetween = entity.Data["type"].Any((a) => (string)a.Primitive == "Collection" || (string)a.Primitive == "OrderedCollection") ? "Collection" : _inbetween;
                    else if (test == "CollectionPage") _inbetween = entity.Data["type"].Any((a) => (string)a.Primitive == "CollectionPage" || (string)a.Primitive == "OrderedCollectionPage") ? "CollectionPage" : _inbetween;
                    else _inbetween = entity.Data["type"].Any((a) => (string)a.Primitive == test) ? test : _inbetween;
                }

                if (isAnd)
                    _inbetween = previousResult == null ? null : _inbetween;
            }

            if (_inbetween == null) return null;

            string text;
            if (_inbetween.Type == JTokenType.Array) text = _inbetween[0].ToObject<string>();
            else text = _inbetween.ToObject<string>();

            if (isHtml) return text;
            return WebUtility.HtmlEncode(text);
        }

        public async Task<string> ParseTemplate(string template, IEntityStore entityStore, APEntity entity, Registers regs = null)
        {
            if (regs == null) regs = new Registers();
            if (!Templates.ContainsKey(template)) throw new InvalidOperationException($"Template {template} does not exist!");
            var templ = Templates[template];
            var builder = new StringBuilder();
            var end = "";

            for (int i = 0; i < templ.Count; i++)
            {
                var item = templ[i];
                switch (item.Type)
                {
                    case "wrap":
                        builder.Append($"<{item.Data} data-id=\"{entity.Id}\" data-template=\"{template}\">");
                        end = $"</{item.Data.Split(' ')[0]}>";
                        break;
                    case "text":
                        builder.Append(item.Data);
                        break;
                    case "if":
                    case "while":
                        if (!await _parseCondition(entity, entityStore, item.Data.Split(new[] { ' ' }, 2)[1], regs))
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
                        if (item.Data.Contains("%")) {
                            builder.Append("<span>");
                        }
                        builder.Append(await _parseCommand(entity, entityStore, item.Data, regs) ?? "");
                        if (item.Data.Contains("%")) {
                            builder.Append("</span>");
                        }
                        break;
                }
            }

            builder.Append(end);

            return builder.ToString();
        }
    }
}
