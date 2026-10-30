using System;
using System.IO;
using System.Linq;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RevitCortex.Core.Addins;

/// <summary>Deliberately small JSON Schema profile. Unsupported keywords fail registration.</summary>
public static class AddinJson
{
    public static JToken Parse(string json, int maxBytes = 1048576)
    {
        if (json == null || Encoding.UTF8.GetByteCount(json) > maxBytes)
            throw new AddinProtocolException("PayloadTooLarge", "JSON exceeds the declared response/input limit.");
        using var reader = new JsonTextReader(new StringReader(json)) { MaxDepth = 32, DateParseHandling = DateParseHandling.None };
        var token = JToken.ReadFrom(reader, new JsonLoadSettings { DuplicatePropertyNameHandling = DuplicatePropertyNameHandling.Error });
        if (reader.Read()) throw new AddinProtocolException("InvalidJson", "Trailing JSON content.");
        return token;
    }

    public static void CheckSchema(JToken schema, int depth = 0)
    {
        if (depth > 16 || !(schema is JObject obj)) throw new AddinProtocolException("InvalidSchema", "Schema must be an object with bounded nesting.");
        var allowed = new[] { "type", "properties", "required", "additionalProperties", "items", "enum", "minimum", "maximum", "minLength", "maxLength", "minItems", "maxItems", "description", "title" };
        if (obj.Properties().Any(p => !allowed.Contains(p.Name))) throw new AddinProtocolException("UnsupportedSchema", "Unsupported JSON Schema keyword. Use the Cortex schema profile v1.");
        var type = (string?)obj["type"];
        if (!new[] { "object", "array", "string", "integer", "number", "boolean", "null" }.Contains(type)) throw new AddinProtocolException("InvalidSchema", "An explicit supported type is required.");
        foreach (var keyword in new[] { "minLength", "maxLength", "minItems", "maxItems" })
            if (obj[keyword] != null && (obj[keyword]!.Type != JTokenType.Integer || obj.Value<double>(keyword) < 0 || obj.Value<double>(keyword) > int.MaxValue))
                throw new AddinProtocolException("InvalidSchema", keyword + " must be a nonnegative Int32.");
        foreach (var keyword in new[] { "minimum", "maximum" })
            if (obj[keyword] != null && (obj[keyword]!.Type is not (JTokenType.Integer or JTokenType.Float) || double.IsNaN(obj.Value<double>(keyword)) || double.IsInfinity(obj.Value<double>(keyword))))
                throw new AddinProtocolException("InvalidSchema", keyword + " must be finite numeric data.");
        foreach (var pair in new[] { new[] { "minimum", "maximum" }, new[] { "minLength", "maxLength" }, new[] { "minItems", "maxItems" } })
            if (obj[pair[0]] != null && obj[pair[1]] != null && obj.Value<double>(pair[0]) > obj.Value<double>(pair[1]))
                throw new AddinProtocolException("InvalidSchema", "Minimum must not exceed maximum.");
        if ((type != "object" && new[] { "properties", "required", "additionalProperties" }.Any(k => obj[k] != null))
            || (type != "array" && new[] { "items", "minItems", "maxItems" }.Any(k => obj[k] != null))
            || (type != "string" && new[] { "minLength", "maxLength" }.Any(k => obj[k] != null))
            || (type != "number" && type != "integer" && new[] { "minimum", "maximum" }.Any(k => obj[k] != null)))
            throw new AddinProtocolException("InvalidSchema", "Schema keyword does not apply to its declared type.");
        if (obj["properties"] is JObject props) foreach (var p in props.Properties()) CheckSchema(p.Value, depth + 1);
        else if (obj["properties"] != null) throw new AddinProtocolException("InvalidSchema", "properties must be an object.");
        if (obj["items"] != null) CheckSchema(obj["items"]!, depth + 1);
        if (type == "array" && obj["items"] == null) throw new AddinProtocolException("InvalidSchema", "Array items schema is required.");
        if (type == "object" && (obj["additionalProperties"]?.Type != JTokenType.Boolean || obj.Value<bool?>("additionalProperties") != false)) throw new AddinProtocolException("InvalidSchema", "Object schemas must set additionalProperties=false.");
        if (obj["required"] != null && (!(obj["required"] is JArray req) || req.Any(v => v.Type != JTokenType.String || obj["properties"]?[(string)v!] == null)))
            throw new AddinProtocolException("InvalidSchema", "required must name declared properties.");
        if (obj["enum"] != null && (!(obj["enum"] is JArray choices) || choices.Count == 0)) throw new AddinProtocolException("InvalidSchema", "enum must be a nonempty array.");
    }

    public static void Validate(JToken value, JToken schema, string path = "$")
    {
        var type = (string?)schema["type"];
        bool valid = type switch
        {
            "object" => value.Type == JTokenType.Object, "array" => value.Type == JTokenType.Array,
            "string" => value.Type == JTokenType.String, "integer" => value.Type == JTokenType.Integer,
            "number" => value.Type is JTokenType.Integer or JTokenType.Float,
            "boolean" => value.Type == JTokenType.Boolean, "null" => value.Type == JTokenType.Null, _ => false
        };
        if (!valid) throw new AddinProtocolException("SchemaMismatch", path + ": expected " + type);
        if (schema["enum"] is JArray choices && !choices.Any(v => JToken.DeepEquals(v, value))) throw new AddinProtocolException("SchemaMismatch", path + ": value is not in enum.");
        if (value is JObject obj)
        {
            foreach (var name in (schema["required"] as JArray ?? new JArray()).Values<string>()) if (obj.Property(name!) == null) throw new AddinProtocolException("SchemaMismatch", path + ": missing " + name);
            foreach (var p in obj.Properties())
            {
                var sub = schema["properties"]?[p.Name];
                if (sub == null) throw new AddinProtocolException("SchemaMismatch", path + ": unknown property " + p.Name);
                Validate(p.Value, sub, path + "." + p.Name);
            }
        }
        if (value is JArray array)
        {
            Limit(array.Count, schema, "minItems", "maxItems", path);
            for (int i = 0; i < array.Count; i++) Validate(array[i], schema["items"]!, path + "[" + i + "]");
        }
        if (type == "string") Limit(value.Value<string>()!.Length, schema, "minLength", "maxLength", path);
        if (type is "integer" or "number")
        {
            var number = value.Value<double>();
            if (double.IsNaN(number) || double.IsInfinity(number)) throw new AddinProtocolException("SchemaMismatch", path + ": number must be finite.");
            Limit(number, schema, "minimum", "maximum", path);
        }
    }
    private static void Limit(double value, JToken schema, string min, string max, string path)
    {
        if ((schema[min] != null && value < schema.Value<double>(min)) || (schema[max] != null && value > schema.Value<double>(max)))
            throw new AddinProtocolException("SchemaMismatch", path + ": outside schema limits.");
    }
    public static JToken Canonical(JToken token) => token is JObject obj
        ? new JObject(obj.Properties().OrderBy(p => p.Name, StringComparer.Ordinal).Select(p => new JProperty(p.Name, Canonical(p.Value))))
        : token is JArray a ? new JArray(a.Select(Canonical)) : token.DeepClone();
}
