using System.Collections;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace Kroeg.ActivityStreams
{
    public class ASObject : IEnumerable<KeyValuePair<string, List<ASTerm>>>
    {
        private Dictionary<string, List<ASTerm>> _terms = new Dictionary<string, List<ASTerm>>();
        private static Dictionary<string, string> _languageMapMap = new Dictionary<string, string> { ["content"] = "contentMap", ["name"] = "nameMap", ["summary"] = "summaryMap" };
        private static HashSet<string> _alwaysArray = new HashSet<string> { "items", "orderedItems" };

        public List<ASTerm> this[string value] => _terms.ContainsKey(value) ? _terms[value] : (_terms[value] = new List<ASTerm>());

        public void Replace(string key, ASTerm value)
        {
            this[key].Clear();
            this[key].Add(value);
        }

        private void _deserialize(string arg, JToken val)
        {
            if (val.Type == JTokenType.Array)
            {
                foreach (var v in val.Value<JArray>())
                {
                    _deserialize(arg, v);
                }
            }
            else
            {
                var arr = this[arg];
                if (val.Type == JTokenType.Object)
                {
                    arr.Add(new ASTerm { SubObject = ASObject.Parse(val.Value<JObject>()) });
                }
                else if (val.Type == JTokenType.Float)
                    arr.Add(new ASTerm { Primitive = val.Value<decimal>() });
                else if (val.Type == JTokenType.Integer)
                    arr.Add(new ASTerm { Primitive = val.Value<int>() });
                else if (val.Type == JTokenType.Boolean)
                    arr.Add(new ASTerm { Primitive = val.Value<bool>() });
                else
                    arr.Add(new ASTerm { Primitive = val.Value<string>() });
            }
        }

        public static ASObject Parse(string obj)
        {
            var ser = new JsonTextReader(new StringReader(obj));
            ser.DateParseHandling = DateParseHandling.None;
            return Parse(JObject.Load(ser));
        }

        public ASObject Clone()
        {
            var o = new ASObject();
            foreach (var kv in _terms)
            {
                o._terms[kv.Key] = new List<ASTerm>(kv.Value.Select(a => a.Clone()));
            }

            return o;
        }

        public static ASObject Parse(JObject obj)
        {
            var ao = new ASObject();

            foreach (var kv in obj)
            {
                if (kv.Key == "@context") continue;

                if (_languageMapMap.ContainsValue(kv.Key))
                {
                    var termval = ao[kv.Key.Substring(0, kv.Key.Length - 3)];
                    var val = kv.Value.Value<JObject>();
                    foreach (var v in val)
                    {
                        termval.Add(new ASTerm { Language = v.Key, Primitive = v.Value.Value<string>() });
                    }
                }
                else
                {
                    ao._deserialize(kv.Key, kv.Value);
                }
            }

            return ao;
        }

        public JObject Serialize(bool includeContext = true)
        {
            var result = new JObject();
            if (includeContext)
                result["@context"] = "https://www.w3.org/ns/activitystreams";

            foreach (var kv in _terms)
            {
                if (kv.Value.Count == 0) continue;
                if (kv.Value.Count > 1 || kv.Value[0].Language != null && _languageMapMap.ContainsKey(kv.Key) || _alwaysArray.Contains(kv.Key))
                {
                    var unlanguage = kv.Value.Where(a => a.Language == null).Select(a => a.Primitive ?? a.SubObject.Serialize(false)).ToArray();
                    var language = kv.Value.Where(a => a.Language != null).ToDictionary(a => a.Language);

                    if (unlanguage.Length > 0)
                    {
                        result[kv.Key] = new JArray(unlanguage);
                    }

                    if (language.Count > 0)
                    {
                        var map = new JObject();
                        foreach (var val in language)
                        {
                            map[val.Key] = JToken.FromObject(val.Value.Primitive);
                        }

                        result[_languageMapMap[kv.Key]] = map;
                    }
                }
                else
                {
                    if (kv.Value[0].Primitive != null)
                        result[kv.Key] = JToken.FromObject(kv.Value[0].Primitive);
                    else if (kv.Value[0].SubObject != null)
                        result[kv.Key] = kv.Value[0].SubObject.Serialize(false);
                    else
                        result[kv.Key] = JValue.CreateNull();
                }
            }

            return result;
        }

        public IEnumerator<KeyValuePair<string, List<ASTerm>>> GetEnumerator()
        {
            return _terms.GetEnumerator();
        }

        IEnumerator IEnumerable.GetEnumerator()
        {
            return _terms.GetEnumerator();
        }
    }
}
