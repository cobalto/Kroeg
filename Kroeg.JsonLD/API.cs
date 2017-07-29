using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Newtonsoft.Json.Linq;
using Newtonsoft.Json;

namespace Kroeg.JsonLD
{
    public class API
    {
        public class JsonLDException : Exception
        {
            public JsonLDException(string msg) : base(msg) { }
        }

        private static HashSet<string> _containerValues = new HashSet<string> { "@list", "@set", "@index", "@language" };
        private static HashSet<string> _limitedResultList = new HashSet<string> { "@value", "@language", "@type", "@index" };

        public delegate Task<JObject> ResolveContext(string uri);

        private ResolveContext _resolve;

        /// <summary>
        /// 6.2
        /// </summary>
        /// <param name="activeContext"></param>
        /// <param name="localContext"></param>
        /// <param name="term"></param>
        /// <param name="defined"></param>
        /// <returns></returns>
        private async Task _createTermDefinition(Context activeContext, JObject localContext, string term,
            Dictionary<string, bool> defined)
        {
            if (defined.ContainsKey(term))
                if (defined[term])
                    return;
                else
                {
                    throw new JsonLDException("cyclic IRI mapping");
                }

            defined[term] = false;
            if (term.StartsWith("@")) throw new JsonLDException("keyword redefinition");

            activeContext.Remove(term);
            var value = localContext[term];
            if (value == null || value.Type == JTokenType.Null ||
                value.Type == JTokenType.Object && ((JObject)value)["@id"]?.Type == JTokenType.Null)
            {
                defined[term] = true;
                activeContext.Add(term, null);
                return;
            }

            if (value.Type == JTokenType.String)
            {
                var data = value.ToObject<string>();
                value = new JObject { ["@id"] = data };
            }
            else if (value.Type != JTokenType.Object)
                throw new JsonLDException("invalid term definition");

            var definition = new TermDefinition();
            if (value["@type"] != null)
            {
                var type = value["@type"];
                if (type.Type != JTokenType.String) throw new JsonLDException("invalid type mapping");

                var typeString = await _expandIri(activeContext, type.ToObject<string>(), false, true,
                    localContext, defined);

                if (typeString != "@id" && typeString != "@vocab" && !Uri.IsWellFormedUriString(typeString, UriKind.Absolute))
                    throw new JsonLDException("invalid type mapping");

                definition.TypeMapping = typeString;
            }

            if (value["@reverse"] != null)
            {
                if (value["@id"] != null) throw new JsonLDException("invalid reverse property");
                if (value["@reverse"].Type != JTokenType.String) throw new JsonLDException("invalid IRI mapping");

                definition.IriMapping = await _expandIri(activeContext, value["@reverse"].ToObject<string>(), false,
                    true, localContext, defined);
                if (!definition.IriMapping.Contains(":")) throw new JsonLDException("invalid IRI mapping");
                definition.ReverseProperty = true;

                activeContext.Add(term, definition);
                defined[term] = true;
                return;
            }

            definition.ReverseProperty = false; // not needed, but to be sure(TM)

            if (value["@id"] != null && value["@id"].ToObject<string>() != term)
            {
                if (value["@id"].Type != JTokenType.String) throw new JsonLDException("invalid IRI mapping");
                definition.IriMapping = await _expandIri(activeContext, value["@id"].ToObject<string>(), false, true,
                    localContext, defined);

                // todo: 13.2
            }
            else if (term.Contains(":"))
            {
                var spl = term.Split(new[] { ':' }, 2);
                var prefix = spl[0];
                var suffix = spl[1];

                if (!suffix.StartsWith("//") && localContext[prefix] != null)
                    await _createTermDefinition(activeContext, localContext, prefix, defined);

                if (activeContext.Has(prefix))
                    definition.IriMapping = activeContext[prefix].IriMapping + suffix;
                else
                    definition.IriMapping = term;
            }
            else if (activeContext.VocabularyMapping != null)
                definition.IriMapping = activeContext.VocabularyMapping + term;
            else
                throw new JsonLDException("invalid IRI mapping");

            if (value["@container"] != null)
            {
                var container = value["@container"].ToObject<string>();
                if (!_containerValues.Contains(container)) throw new JsonLDException("invalid container mapping");

                definition.ContainerMapping = container;
            }

            if (value["@language"] != null && value["@type"] == null)
            {
                var language = value["@language"].ToObject<string>();
                if (value["@language"].Type != JTokenType.Null && value["@language"].Type != JTokenType.String)
                    throw new JsonLDException("invalid language mapping");

                definition.LanguageMapping = language.ToLower();
                definition.HasLanguageMapping = true;
            }

            activeContext.Add(term, definition);
            defined[term] = true;
        }

        /// <summary>
        /// 6.3
        /// </summary>
        /// <param name="activeContext"></param>
        /// <param name="value"></param>
        /// <param name="documentRelative"></param>
        /// <param name="vocab"></param>
        /// <param name="localContext"></param>
        /// <param name="defined"></param>
        /// <returns></returns>
        private async Task<string> _expandIri(Context activeContext, string value, bool documentRelative = false,
            bool vocab = false, JObject localContext = null, Dictionary<string, bool> defined = null)
        {
            if (value == null || value.StartsWith("@"))
                return value;

            if (localContext != null && defined != null && localContext[value] != null && (!defined.ContainsKey(value) || !defined[value]))
            {
                // create term definition!
                await _createTermDefinition(activeContext, localContext, value, defined);
            }

            if (vocab && activeContext.Has(value)) return activeContext[value].IriMapping;

            if (value.Contains(":"))
            {
                var spl = value.Split(new char[] { ':' }, 2);
                var prefix = spl[0];
                var suffix = spl[1];
                if (prefix == "_" || suffix.StartsWith("//")) return value;

                if (localContext != null && defined != null && localContext[prefix] != null &&
                    (!defined.ContainsKey(prefix) || !defined[prefix]))
                {
                    await _createTermDefinition(activeContext, localContext, prefix, defined);
                }

                if (activeContext.Has(prefix)) return activeContext[prefix].IriMapping + suffix;

                return value;
            }

            if (vocab && activeContext.VocabularyMapping != null) return activeContext.VocabularyMapping + value;
            if (documentRelative) return new Uri(new Uri(activeContext.BaseIri), value).ToString();

            return value;
        }

        /// <summary>
        /// 7.2
        /// </summary>
        /// <param name="activeContext"></param>
        /// <param name="activeProperty"></param>
        /// <param name="value"></param>
        /// <returns></returns>
        private async Task<JToken> _expandValue(Context activeContext, string activeProperty, JToken value)
        {
            if (activeContext[activeProperty]?.TypeMapping == "@id")
                return new JObject { ["@id"] = await _expandIri(activeContext, value.ToObject<string>(), true) };
            if (activeContext[activeProperty]?.TypeMapping == "@vocab")
                return new JObject { ["@id"] = await _expandIri(activeContext, value.ToObject<string>(), true, true) };

            var result = new JObject { ["@value"] = value };

            if (activeContext[activeProperty]?.TypeMapping != null)
                result["@type"] = activeContext[activeProperty].TypeMapping;
            else if (value.Type == JTokenType.String)
            {
                if (activeContext[activeProperty]?.LanguageMapping != null)
                    result["@language"] = activeContext[activeProperty].LanguageMapping;
                else if (activeContext.DefaultLanguage != null)
                    result["@language"] = activeContext.DefaultLanguage;
            }

            return result;
        }

        /// <summary>
        /// 6.1
        /// </summary>
        /// <param name="activeContext"></param>
        /// <param name="localContext"></param>
        /// <param name="remoteContext"></param>
        /// <returns></returns>
        private async Task<Context> _processContext(Context activeContext, JToken localContext,
            List<string> remoteContext = null)
        {
            remoteContext = remoteContext ?? new List<string>();
            var result = activeContext.Clone();

            if (localContext.Type != JTokenType.Array) localContext = new JArray(localContext);

            foreach (var context in (JArray)localContext)
            {
                if (context.Type == JTokenType.Null)
                    result = new Context();
                else if (context.Type == JTokenType.String)
                {
                    var contextUri = context.ToObject<string>();
                    if (activeContext.BaseIri != null) contextUri = new Uri(new Uri(activeContext.BaseIri), contextUri).ToString();

                    if (remoteContext.Contains(contextUri))
                        throw new JsonLDException("recursive context inclusion");

                    remoteContext.Add(contextUri);
                    // dereference!

                    JObject obj = null;

                    try
                    {
                        obj = await _resolve(contextUri);
                    }
                    catch (Exception e)
                    {
                        throw new Exception("loading remote context failed", e);
                    }

                    if (obj.Type != JTokenType.Object || obj["@context"] == null)
                        throw new JsonLDException("invalid remote context");

                    var newContext = obj["@context"];
                    result = await _processContext(result, newContext, remoteContext);
                    continue;
                }
                else if (context.Type != JTokenType.Object)
                {
                    throw new JsonLDException("invalid local context");
                }

                var contextObject = (JObject)context;
                if (contextObject["@base"] != null)
                {
                    var value = contextObject["@base"];
                    if (value.Type == JTokenType.Null)
                        result.BaseIri = null;
                    else if (value.Type != JTokenType.String)
                        throw new JsonLDException("invalid base IRI");
                    else
                    {
                        var valStr = value.ToObject<string>();
                        if (Uri.IsWellFormedUriString(valStr, UriKind.Absolute)) result.BaseIri = valStr;
                        else if (Uri.IsWellFormedUriString(valStr, UriKind.Relative))
                            result.BaseIri = new Uri(new Uri(result.BaseIri), valStr).ToString();
                        else
                            throw new JsonLDException("invalid base IRI");
                    }
                }

                if (contextObject["@vocab"] != null)
                {
                    var value = contextObject["@vocab"];
                    if (value.Type == JTokenType.Null)
                        result.VocabularyMapping = null;
                    else if (value.Type != JTokenType.String)
                        throw new JsonLDException("invalid vocab mapping");
                    else
                    {
                        var valStr = value.ToObject<string>();
                        if (valStr != "_:" && !Uri.IsWellFormedUriString(valStr, UriKind.Absolute))
                            throw new JsonLDException("invalid vocab mapping");

                        result.VocabularyMapping = valStr;
                    }
                }

                if (contextObject["@language"] != null)
                {
                    var value = contextObject["@language"];
                    if (value.Type == JTokenType.Null)
                        result.DefaultLanguage = null;
                    else if (value.Type != JTokenType.String)
                        throw new JsonLDException("invalid default language");

                    result.DefaultLanguage = value.ToObject<string>().ToLower();
                }

                var defined = new Dictionary<string, bool>();
                foreach (var kv in contextObject)
                {
                    if (kv.Key != "@base" && kv.Key != "@vocab" && kv.Key != "@language")
                        await _createTermDefinition(result, contextObject, kv.Key, defined);
                }
            }

            return result;
        }

        private static void _add(JObject obj, string key, JToken value)
        {
            if (obj[key] == null) obj[key] = new JArray();
            if (value.Type != JTokenType.Array)
                ((JArray)obj[key]).Add(value);
            else
                foreach (var item in (JArray)value)
                    ((JArray)obj[key]).Add(item);
        }

        /// <summary>
        /// 7.1
        /// </summary>
        /// <param name="activeContext"></param>
        /// <param name="activeProperty"></param>
        /// <param name="element"></param>
        /// <returns></returns>
        private async Task<JToken> _expand(Context activeContext, string activeProperty, JToken element)
        {
            if (element.Type == JTokenType.Null) return element; // return null

            if (element.Type == JTokenType.Array)
            {
                var result = new JArray();

                foreach (var item in (JArray)element)
                {
                    var expandedItem = await _expand(activeContext, activeProperty, item);
                    if (activeProperty == "@list" || activeContext[activeProperty]?.ContainerMapping == "@list")
                    {
                        if (expandedItem.Type == JTokenType.Array || expandedItem.Type == JTokenType.Object && (JObject)expandedItem["@list"] != null)
                            throw new JsonLDException("list of lists");
                    }

                    if (expandedItem.Type == JTokenType.Array)
                        foreach (var value in (JArray)expandedItem)
                            result.Add(value);
                    else
                        result.Add(expandedItem);
                }

                return result;
            }

            if (element.Type != JTokenType.Object)
            {
                if (activeProperty == null || activeProperty == "@graph") return JValue.CreateNull();

                return await _expandValue(activeContext, activeProperty, element);
            }

            var objectElement = (JObject)element;
            if (objectElement["@context"] != null)
                activeContext = await _processContext(activeContext, objectElement["@context"]);

            var resultObject = new JObject();

            foreach (var i in ((IEnumerable<KeyValuePair<string, JToken>>)objectElement).OrderBy(a => a.Key))
            {
                var key = i.Key;
                var value = i.Value;

                if (key == "@context") continue;
                var expandedProperty = await _expandIri(activeContext, key, false, true);

                if (expandedProperty == null || !expandedProperty.Contains(":") &&
                    !expandedProperty.StartsWith("@")) continue;

                JToken expandedValue = null;

                if (expandedProperty.StartsWith("@"))
                {
                    if (activeProperty == "@reverse") throw new JsonLDException("invalid reverse property map");
                    if (resultObject[expandedProperty] != null) throw new JsonLDException("colliding keywords");

                    if (expandedProperty == "@id")
                        if (value.Type != JTokenType.String)
                            throw new JsonLDException("invalid @id value");
                        else
                        {
                            expandedValue = await _expandIri(activeContext, value.ToObject<string>(), true);
                        }
                    else if (expandedProperty == "@type")
                    {
                        if (value.Type == JTokenType.String)
                        {
                            expandedValue = await _expandIri(activeContext, value.ToObject<string>(), true, true);
                        }
                        else if (value.Type == JTokenType.Array &&
                                 ((JArray)value).All(a => a.Type == JTokenType.String))
                        {
                            var arrs = new JArray();
                            foreach (var t in (JArray)value)
                                arrs.Add(await _expandIri(activeContext, t.ToObject<string>(), true, true));

                            expandedValue = arrs;
                        }
                        else
                            throw new JsonLDException("invalid type value");
                    }
                    else if (expandedProperty == "@graph")
                        expandedValue = await _expand(activeContext, "@graph", value);
                    else if (expandedProperty == "@value")
                        if (value.Type == JTokenType.Array || value.Type == JTokenType.Object)
                            throw new JsonLDException("invalid value object value");
                        else
                            expandedValue = value;
                    else if (expandedProperty == "@language")
                        if (value.Type != JTokenType.String)
                            throw new JsonLDException("invalid language-tagged string");
                        else
                            expandedValue = value.ToObject<string>().ToLower();
                    else if (expandedProperty == "@index")
                        if (value.Type != JTokenType.String)
                            throw new JsonLDException("invalid @index value");
                        else
                            expandedValue = value;
                    else if (expandedProperty == "@list")
                    {
                        if (activeProperty == null || activeProperty == "@graph") continue;
                        expandedValue = await _expand(activeContext, activeProperty, value);
                        if (expandedValue.Type == JTokenType.Object && ((JObject)expandedValue)["@list"] != null)
                            throw new JsonLDException("list of lists");
                    }
                    else if (expandedProperty == "@set")
                        expandedValue = await _expand(activeContext, activeProperty, value);
                    else if (expandedProperty == "@reverse")
                        if (value.Type != JTokenType.Object)
                            throw new JsonLDException("invalid @reverse value");
                        else
                        {
                            expandedValue = await _expand(activeContext, "@reverse", value);
                            if (expandedValue["@reverse"] != null)
                            {
                                foreach (var kv in (JObject)expandedValue)
                                    _add(resultObject, kv.Key, kv.Value);
                            }

                            if (((JObject)expandedValue).Properties().Any(a => a.Name != "@reverse"))
                            {
                                if (resultObject["@value"] == null) resultObject["@value"] = new JObject();

                                var reverseMap = (JObject)resultObject["@value"];

                                foreach (var kv in (JObject)expandedValue)
                                {
                                    if (kv.Key == "@reverse") continue;

                                    if (kv.Value.Type == JTokenType.Object &&
                                        (kv.Value["@value"] != null || kv.Value["@list"] != null))
                                        throw new JsonLDException("invalid reverse property");

                                    _add(reverseMap, kv.Key, kv.Value);
                                }
                            }
                        }

                    if (expandedValue != null)
                        resultObject[expandedProperty] = expandedValue;

                    continue;
                }
                else if (activeContext[key]?.ContainerMapping == "@language" && value.Type == JTokenType.Object)
                {
                    expandedValue = new JArray();
                    foreach (var kv in (JObject)value)
                    {
                        JArray ar;
                        if (kv.Value.Type != JTokenType.Array)
                            ar = new JArray(kv.Value);
                        else
                            ar = (JArray)kv.Value;
                        foreach (var item in ar)
                            if (item.Type != JTokenType.String) throw new JsonLDException("invalid language map value");
                            else
                                ((JArray)expandedValue).Add(new JObject
                                {
                                    ["@value"] = item,
                                    ["@language"] = kv.Key.ToLower()
                                });
                    }
                }
                else if (activeContext[key]?.ContainerMapping == "@index" && value.Type == JTokenType.Object)
                {
                    expandedValue = new JArray();
                    foreach (var kv in (JObject)value)
                    {
                        JArray ar;
                        if (kv.Value.Type != JTokenType.Array)
                            ar = new JArray(kv.Value);
                        else
                            ar = (JArray)kv.Value;

                        ar = (JArray)await _expand(activeContext, key, ar);

                        foreach (var item in ar)
                        {
                            if (item["@index"] == null) item["@index"] = kv.Key;
                            ((JArray)expandedValue).Add(item);
                        }
                    }
                }
                else
                    expandedValue = await _expand(activeContext, key, value);

                if (expandedValue == null) continue;

                if (activeContext[key]?.ContainerMapping == "@list" &&
                    (expandedValue.Type != JTokenType.Object || expandedValue["@list"] == null))
                {
                    if (expandedValue.Type != JTokenType.Array)
                        expandedValue = new JArray(expandedValue);

                    expandedValue = new JObject { ["@list"] = expandedValue };
                }

                if (activeContext[key]?.ReverseProperty == true)
                {
                    if (resultObject["@reverse"] == null) resultObject["@reverse"] = new JObject();
                    var reverseMap = (JObject)resultObject["@reverse"];

                    if (expandedValue.Type != JTokenType.Array) expandedValue = new JArray(expandedValue);
                    foreach (var item in (JArray)expandedValue)
                        if (item.Type == JTokenType.Object && (item["@value"] != null || item["@list"] != null))
                            throw new JsonLDException("invalid reverse property value");
                        else
                            _add(reverseMap, expandedProperty, item);
                }
                else
                {
                    _add(resultObject, expandedProperty, expandedValue);
                }
            }

            var resultVal = (JToken)resultObject;

            if (resultObject["@value"] != null)
            {
                var names = resultObject.Properties().Select(a => a.Name);
                if (!names.All(_limitedResultList.Contains) || resultObject["@language"] == resultObject["@type"] && resultObject["@language"] != null) throw new JsonLDException("invalid value object");

                if (resultObject["@value"].Type == JTokenType.Null) resultVal = JValue.CreateNull();
                else if (resultObject["@value"].Type != JTokenType.String && resultObject["@language"] != null) throw new JsonLDException("invalid language-tagged value");
                else if (resultObject["@type"] != null && !Uri.IsWellFormedUriString(resultObject["@type"].ToObject<string>(), UriKind.Absolute)) throw new JsonLDException("invalid typed value");
            }
            else if (resultObject["@type"] != null && resultObject["@type"].Type != JTokenType.Array)
                resultObject["@type"] = new JArray(resultObject["@type"]);
            else if (resultObject["@set"] != null || resultObject["@list"] != null)
            {
                if (resultObject.Count > 3 || (resultObject.Count == 3 && resultObject["@index"] == null)) throw new JsonLDException("invalid set or list error");
                if (resultObject["@set"] != null)
                    resultVal = resultObject["@set"];
            }

            if (resultObject.Count == 1 && resultObject["@language"] != null) resultVal = null;

            if (activeProperty == null || activeProperty == "@graph")
            {
                // todo: drop free-floating values
            }

            return resultVal;
        }

        public async Task<JToken> Expand(JToken element)
        {
            return await _expand(new Context(), null, element);
        }

        private InverseContext _createInverseContext(Context activeContext)
        {
            var result = new InverseContext();

            var defaultLanguage = "@none";
            if (activeContext.DefaultLanguage != null) defaultLanguage = activeContext.DefaultLanguage;

            foreach (var kv in activeContext.TermDefinitions.OrderBy(a => a.Key).OrderBy(a => a.Key.Length))
            {
                var term = kv.Key;
                var termDefinition = kv.Value;

                if (termDefinition == null) continue;

                var container = termDefinition.ContainerMapping ?? "@none";
                var iri = termDefinition.IriMapping;

                if (!result.Data.ContainsKey(iri)) result.Data[iri] = new Dictionary<string, InverseContextObject>();
                var containerMap = result.Data[iri];

                if (!containerMap.ContainsKey(container))
                    containerMap[container] = new InverseContextObject();

                var typeLanguageMap = containerMap[container];
                if (termDefinition.ReverseProperty)
                {
                    if (!typeLanguageMap.Type.ContainsKey("@reverse")) typeLanguageMap.Type["@reverse"] = term;
                }
                else if (termDefinition.TypeMapping != null)
                {
                    if (!typeLanguageMap.Type.ContainsKey(termDefinition.TypeMapping)) typeLanguageMap.Type[termDefinition.TypeMapping] = term;
                }
                else if (termDefinition.HasLanguageMapping)
                {
                    if (!typeLanguageMap.Language.ContainsKey(termDefinition.LanguageMapping)) typeLanguageMap.Language[termDefinition.LanguageMapping] = term;
                }
                else
                {
                    if (!typeLanguageMap.Language.ContainsKey(defaultLanguage)) typeLanguageMap.Language[defaultLanguage] = term;
                    if (!typeLanguageMap.Language.ContainsKey("@none")) typeLanguageMap.Language["@none"] = term;

                    if (!typeLanguageMap.Type.ContainsKey("@none")) typeLanguageMap.Type["@none"] = term;
                }
            }

            return result;
        }

        private string _compactIri(Context activeContext, InverseContext inverseContext, string iri, JToken value = null, bool vocab = false, bool reverse = false)
        {
            if (iri == null) return null;
            if (vocab && inverseContext.Data.ContainsKey(iri))
            {
                var defaultLanguage = activeContext.DefaultLanguage ?? "@none";
                var containers = new List<string>();
                var typeLanguage = "@language";
                var typeLanguageValue = "@null";
                if (value != null && value.Type == JTokenType.Object) containers.Add("@index");
                if (reverse)
                {
                    typeLanguage = "@type";
                    typeLanguageValue = "@reverse";
                    containers.Add("@set");
                }
                if (value?.Type == JTokenType.Object && value["@list"] != null)
                {
                    if (value["@index"] == null) containers.Add("@list");
                    var list = (JArray)value["@list"];
                    string commonType = null;
                    string commonLanguage = null;
                    if (list.Count == 0) commonLanguage = defaultLanguage;
                    foreach (var item in list)
                    {
                        var itemLanguage = "@none";
                        var itemType = "@none";
                        if (item["@value"] != null)
                        {
                            if (item["@language"] != null) itemLanguage = item["@language"].ToObject<string>();
                            else if (item["@type"] != null) itemType = item["@type"].ToObject<string>();
                            else itemLanguage = "@null";
                        }
                        else itemType = "@id";

                        if (commonLanguage == null) commonLanguage = itemLanguage;
                        else if (commonLanguage != itemLanguage && item["@value"] != null) commonLanguage = "@none";

                        if (commonType == null) commonType = itemType;
                        else if (itemType != commonType) commonType = "@none";

                        if (commonLanguage == "@none" && commonType == "@none") break;
                    }

                    if (commonLanguage == null) commonLanguage = "@none";
                    if (commonType == null) commonType = "@none";
                    if (commonType != "@none") typeLanguage = "@type"; typeLanguageValue = commonType;
                }
                else
                {
                    if (value?.Type == JTokenType.Object && value["@value"] != null)
                    {
                        if (value["@language"] != null && value["@index"] == null)
                        {
                            typeLanguageValue = value["@language"].ToObject<string>();
                            containers.Add("@language");
                        }
                        else if (value["@type"] != null)
                        {
                            typeLanguageValue = value["@type"].ToObject<string>();
                            typeLanguage = "@type";
                        }
                    }
                    else
                    {
                        typeLanguage = "@type";
                        typeLanguageValue = "@id";
                    }

                    containers.Add("@set");
                }

                containers.Add("@none");
                if (typeLanguageValue == null) typeLanguageValue = "@null";

                var preferredValues = new List<string>();
                if (typeLanguageValue == "@reverse") preferredValues.Add("@reverse");
                if ((typeLanguageValue == "@id" || typeLanguageValue == "@reverse") && (value?.Type == JTokenType.Object && value["@id"] != null))
                {
                    var term = _compactIri(activeContext, inverseContext, value["@id"].ToObject<string>(), null, true, true);
                    if (activeContext.Has(term))
                        preferredValues.AddRange(new[] { "@vocab", "@id", "@none" });
                    else
                        preferredValues.AddRange(new[] { "@id", "@vocab", "@none" });
                }
                else
                    preferredValues.AddRange(new[] { typeLanguageValue, "@none" });

                var resultTerm = _selectTerm(inverseContext, iri, containers, typeLanguage, preferredValues);
                if (resultTerm != null) return resultTerm;

            }

            if (vocab && activeContext.VocabularyMapping != null)
            {
                if (iri.StartsWith(activeContext.VocabularyMapping) && iri.Length > activeContext.VocabularyMapping.Length)
                {
                    var suffix = iri.Substring(activeContext.VocabularyMapping.Length);
                    if (!activeContext.Has(suffix)) return suffix;
                }
            }

            string compactIri = null;
            foreach (var kv in activeContext.TermDefinitions)
            {
                var term = kv.Key;
                var termDefinition = kv.Value;
                if (term.Contains(":")) continue;
                if (termDefinition == null || termDefinition.IriMapping == iri || !iri.StartsWith(termDefinition.IriMapping)) continue;
                var candidate = term + ":" + iri.Substring(termDefinition.IriMapping.Length);
                if (compactIri == null || candidate.Length < compactIri.Length || candidate.CompareTo(compactIri) < 0) compactIri = candidate;
            }

            if (compactIri != null) return compactIri;
            if (!vocab && activeContext.BaseIri != null) iri = (new Uri(iri)).MakeRelativeUri(new Uri(activeContext.BaseIri)).ToString();
            return iri;
        }

        private string _selectTerm(InverseContext inverseContext, string iri, List<string> containers, string typeLanguage, List<string> preferredValues)
        {
            var containerMap = inverseContext.Data[iri];
            foreach (var container in containers)
            {
                if (!containerMap.ContainsKey(container)) continue;

                var typeLanguageMap = containerMap[container];

                var valueMap = typeLanguage == "@language" ? typeLanguageMap.Language : typeLanguageMap.Type;
                foreach (var item in preferredValues)
                    if (valueMap.ContainsKey(item)) return valueMap[item];
            }

            return null;
        }

        private JToken _compactValue(Context activeContext, InverseContext inverseContext, string activeProperty, JObject value)
        {
            var numberMembers = value.Count;
            if (value["@index"] != null && activeContext[activeProperty]?.ContainerMapping == "@index") numberMembers--;
            if (numberMembers > 2) return value;
            if (value["@id"] != null)
            {
                if (numberMembers == 1 && activeContext[activeProperty]?.TypeMapping == "@id") return _compactIri(activeContext, inverseContext, value["@id"].ToObject<string>());
                else if (numberMembers == 1 && activeContext[activeProperty]?.TypeMapping == "@vocab") return _compactIri(activeContext, inverseContext, value["@id"].ToObject<string>(), null, true);
                else return value;
            }

            if (value["@type"] != null && value["@type"].ToObject<string>() == activeContext[activeProperty]?.TypeMapping) return value["@value"];
            if (value["@language"] != null && value["@language"].ToObject<string>() == activeContext[activeProperty]?.LanguageMapping) return value["@value"];
            if (numberMembers == 1 && (value["@value"].Type != JTokenType.String || activeContext.DefaultLanguage == null || activeContext[activeProperty]?.LanguageMapping == null)) return value["@value"];

            return value;
        }

        private JToken _compact(Context activeContext, InverseContext inverseContext, string activeProperty, JToken element, bool compactArrays = true)
        {
            if (element.Type != JTokenType.Array && element.Type != JTokenType.Object) return element;
            if (element.Type == JTokenType.Array)
            {
                var resultAr = new JArray();
                foreach (var item in element)
                {
                    var compactedItem = _compact(activeContext, inverseContext, activeProperty, item);
                    if (compactedItem != null) resultAr.Add(compactedItem);
                }

                if (resultAr.Count == 1 && activeContext[activeProperty]?.ContainerMapping == null && compactArrays) return resultAr[0];
                return resultAr;
            }

            if (element["@value"] != null || element["@id"] != null)
            {
                var compactResult = _compactValue(activeContext, inverseContext, activeProperty, (JObject) element);
                if (compactResult.Type != JTokenType.Object && compactResult.Type != JTokenType.Array) return compactResult;
            }

            var insideReverse = activeProperty == "@reverse";
            var result = new JObject();
            foreach (var kv in ((JObject) element))
            {
                var expandedProperty = kv.Key;
                var expandedValue = kv.Value;
                JToken compactedValue;

                if (expandedProperty == "@id" || expandedProperty == "@type")
                {

                    if (expandedValue.Type == JTokenType.String)
                        compactedValue = _compactIri(activeContext, inverseContext, expandedValue.ToObject<string>(), null, expandedProperty == "@type");
                    else
                    {
                        var arr = new JArray();
                        foreach (var kv2 in (JArray)expandedValue)
                            arr.Add(_compactIri(activeContext, inverseContext, kv2.ToObject<string>(), null, true));
                        if (arr.Count == 1)
                            compactedValue = arr[0];
                        else
                            compactedValue = arr;
                    }

                    var alias = _compactIri(activeContext, inverseContext, expandedProperty, null, true);
                    result[alias] = compactedValue;
                    continue;
                }

                if (expandedProperty == "@reverse")
                {
                    compactedValue = _compact(activeContext, inverseContext, "@reverse", expandedValue);
                    var skip = new List<string>();
                    foreach (var kv2 in (JObject)compactedValue)
                    {
                        if (skip.Contains(kv.Key)) continue;
                        var property = kv2.Key;
                        var value = kv2.Value;

                        if (activeContext[property]?.ReverseProperty == true)
                        {
                            if ((activeContext[property].ContainerMapping == "@set" || !compactArrays) && value.Type != JTokenType.Array) value = new JArray(value);

                            if (result[property] == null) result[property] = value;
                            else if (result[property].Type != JTokenType.Array)
                            {
                                var r = new JArray();
                                r.Add(result[property]);
                                if (value.Type == JTokenType.Array)
                                    foreach (var item in value) r.Add(item);
                                else
                                    r.Add(value);

                                result[property] = r;
                            }

                            skip.Add(property);
                        }
                    }

                    foreach (var val in skip)
                        compactedValue[val].Remove();

                    if (compactedValue.HasValues)
                    {
                        var alias = _compactIri(activeContext, inverseContext, "@reverse", null, true);
                        result[alias] = compactedValue;
                    }
                    continue;
                }

                if (expandedProperty == "@index" && activeContext[activeProperty]?.ContainerMapping == "@index") continue;
                if (expandedProperty == "@index" || expandedProperty == "@value" || expandedProperty == "@language")
                {
                    var alias = _compactIri(activeContext, inverseContext, expandedProperty, null, true);
                    result[alias] = expandedValue;
                    continue;
                }

                if (((JArray)expandedValue).Count == 0)
                {
                    var itemActiveProperty = _compactIri(activeContext, inverseContext, expandedProperty, expandedValue, true, insideReverse);
                    if (result[itemActiveProperty] != null) result[itemActiveProperty] = new JArray();
                    else if (result[itemActiveProperty].Type != JTokenType.Array) result[itemActiveProperty] = new JArray(result[itemActiveProperty]);
                }

                {
                    var ar = (JArray)expandedValue;
                    foreach (var expandedItem in ar)
                    {
                        var itemActiveProperty = _compactIri(activeContext, inverseContext, expandedProperty, expandedItem, true, insideReverse);
                        var container = activeContext[itemActiveProperty]?.ContainerMapping;
                        var compactedItem = _compact(activeContext, inverseContext, itemActiveProperty, expandedItem["@list"] != null ? expandedItem["@list"] : expandedItem);
                        if (expandedItem["@list"] != null)
                        {
                            if (compactedItem.Type != JTokenType.Array) compactedItem = new JArray(compactedItem);
                            if (container != "@list")
                            {
                                var resObj = new JObject();
                                resObj[_compactIri(activeContext, inverseContext, "@list", compactedItem)] = compactedItem;
                                compactedItem = resObj;

                                if (expandedItem["@index"] != null)
                                    compactedItem[_compactIri(activeContext, inverseContext, "@index")] = expandedItem["@index"];
                            }
                            else
                            {
                                throw new Exception("compaction to list of lists");
                            }
                        }
                        if (container == "@language" || container == "@index")
                        {
                            if (result[itemActiveProperty] == null) result[itemActiveProperty] = new JObject();
                            var mapObject = (JObject)result[itemActiveProperty];
                            if (container == "@language" && compactedItem["@value"] != null) compactedItem = compactedItem["@value"];
                            var mapKey = expandedItem[container].ToObject<string>();
                            if (mapObject[mapKey] == null) mapObject[mapKey] = compactedItem;
                            else
                            {
                                if (mapObject[mapKey].Type != JTokenType.Array)
                                    mapObject[mapKey] = new JArray(mapObject[mapKey]);
                                ((JArray)mapObject[mapKey]).Add(compactedItem);
                            }
                        }
                        else
                        {
                            if (!compactArrays && (container == "@set" || container == "@list" || expandedProperty == "@list" || expandedProperty == "@graph") && compactedItem.Type != JTokenType.Array) compactedItem = new JArray(compactedItem);

                            if (result[itemActiveProperty] == null) result[itemActiveProperty] = compactedItem;
                            else
                            {
                                if (result[itemActiveProperty].Type != JTokenType.Array) result[itemActiveProperty] = new JArray(result[itemActiveProperty]);
                                if (compactedItem.Type == JTokenType.Array)
                                    foreach (var item in compactedItem) ((JArray)result[itemActiveProperty]).Add(item);
                                else
                                    ((JArray)result[itemActiveProperty]).Add(compactedItem);
                            }
                        }
                    }
                }
            }


            return result;
        }

        public JToken CompactExpanded(Context context, JToken toCompact)
        {
            var inverseContext = _createInverseContext(context);
            return _compact(context, inverseContext, null, toCompact);
        }

        public async Task<Context> BuildContext(JToken data)
        {
            return await _processContext(new Context(), data);
        }

        public API(ResolveContext resolve)
        {
            _resolve = resolve;
        }
    }

    internal class InverseContextObject
    {
        public Dictionary<string, string> Language { get; }  = new Dictionary<string, string>();
        public Dictionary<string, string> Type { get; } = new Dictionary<string, string>();
    }


    internal class InverseContext
    {
        public Dictionary<string, Dictionary<string, InverseContextObject>> Data = new Dictionary<string, Dictionary<string, InverseContextObject>>();
    }

    public class Context
    {
        public string BaseIri { get; set; }
        public string VocabularyMapping { get; set; }
        public string DefaultLanguage { get; set; }

        internal Dictionary<string, TermDefinition> TermDefinitions { get; } = new Dictionary<string, TermDefinition>();

        internal bool Has(string term) => TermDefinitions.ContainsKey(term);
        internal void Add(string term, TermDefinition definition) => TermDefinitions[term] = definition;
        internal void Remove(string term) => TermDefinitions.Remove(term);
        internal TermDefinition this[string term] => Has(term) ? TermDefinitions[term] : null;

        public Context Clone()
        {
            var c = new Context() {BaseIri = BaseIri, VocabularyMapping = VocabularyMapping, DefaultLanguage = DefaultLanguage};
            foreach (var kv in TermDefinitions)
                c.TermDefinitions.Add(kv.Key, kv.Value);

            return c;
        }
    }

    internal class TermDefinition
    {
        public string IriMapping { get; set; }
        public bool ReverseProperty { get; set; }
        public string TypeMapping { get; set; }
        public string LanguageMapping { get; set; }
        public bool HasLanguageMapping { get; set; }
        public string ContainerMapping { get; set; }
    }
}
