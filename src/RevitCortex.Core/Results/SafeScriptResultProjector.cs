using System;
using System.Collections;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Linq;
using System.Reflection;
using System.Runtime.CompilerServices;
using System.Text;
using Newtonsoft.Json;
using Newtonsoft.Json.Linq;

namespace RevitCortex.Core.Results;

public sealed class ScriptResultException : Exception
{
    public string Reason { get; }
    public string ResultPath { get; }
    public string ValueType { get; }

    public ScriptResultException(string reason, string path, Type? type = null)
        : base($"Script result rejected: {reason} at {Clip(path)} ({Clip(type?.FullName ?? "value")}).")
    {
        Reason = reason;
        ResultPath = Clip(path);
        ValueType = Clip(type?.FullName ?? "value");
    }

    private static string Clip(string text) => text.Length <= 256 ? text : text.Substring(0, 256);

    public CortexResult<object> ToFailure(string transactionState) => CortexResult<object>.Fail(
        CortexErrorCode.ResultSerializationFailed,
        Message + (transactionState == "rolled_back"
            ? " Changes in the Cortex-managed transaction were rolled back."
            : " Changes may already have been saved; rollback is not confirmed."),
        suggestion: "Return plain data instead of Revit objects, or request fewer results. Verify model state and external effects before retrying; do not retry automatically.",
        context: new Dictionary<string, object>
        {
            ["reason"] = Reason, ["resultPath"] = ResultPath, ["valueType"] = ValueType,
            ["transactionState"] = transactionState, ["externalEffectsMayRemain"] = true
        });
}

/// <summary>
/// Projects script data without general-purpose CLR property serialization.
/// IEnumerable compatibility intentionally executes script iterator code; it is not a sandbox.
/// </summary>
public static class SafeScriptResultProjector
{
    public const int MaxDepth = 16;
    public const int MaxNodes = 100000;
    public const int MaxStringBytes = 256 * 1024;
    public const int MaxResponseBytes = 1024 * 1024;
    private const int TransportReserveBytes = 4096;

    public static JObject Project(object? value, string? scriptPath = null, string? scriptLifetime = null,
        Func<object, long?>? elementIdValue = null, int diagnosticReserveBytes = 0)
    {
        if (diagnosticReserveBytes < 0 || diagnosticReserveBytes >= MaxResponseBytes - TransportReserveBytes)
            throw new ArgumentOutOfRangeException(nameof(diagnosticReserveBytes));
        using var output = new BoundedWriter(MaxResponseBytes - TransportReserveBytes - diagnosticReserveBytes);
        using (var writer = new JsonTextWriter(output) { CloseOutput = false, AutoCompleteOnClose = false, StringEscapeHandling = StringEscapeHandling.EscapeNonAscii })
        {
            var projection = new Projection(writer, elementIdValue);
            writer.WriteStartObject();
            writer.WritePropertyName("result");
            projection.Write(value, 1, "result");
            if (scriptPath != null)
            {
                writer.WritePropertyName("scriptSavedTo");
                projection.Write(scriptPath, 1, "scriptSavedTo");
                writer.WritePropertyName("scriptLifetime");
                projection.Write(scriptLifetime, 1, "scriptLifetime");
            }
            writer.WriteEndObject();
            writer.Flush();
        }
        // Only our bounded, scalar-only JSON reaches the parser. Never deserialize CLR types.
        using var reader = new JsonTextReader(new StringReader(output.ToString()))
        { MaxDepth = MaxDepth + 2, DateParseHandling = DateParseHandling.None };
        return JObject.Load(reader);
    }

    private sealed class Projection
    {
        private readonly JsonTextWriter _writer;
        private readonly HashSet<object> _ancestors = new(new IdentityComparer());
        private int _nodes;
        private readonly Func<object, long?>? _elementIdValue;
        public Projection(JsonTextWriter writer, Func<object, long?>? elementIdValue)
        { _writer = writer; _elementIdValue = elementIdValue; }

        public void Write(object? value, int depth, string path)
        {
            if (depth > MaxDepth) throw new ScriptResultException("DepthLimit", path);
            if (++_nodes > MaxNodes) throw new ScriptResultException("NodeLimit", path);
            if (value == null) { _writer.WriteNull(); return; }
            var type = value.GetType();
            // Scalars never invoke application property getters or converters.
            if (value is string text) { CheckString(text, path); _writer.WriteValue(text); return; }
            if (value is char character) { _writer.WriteValue(character.ToString()); return; }
            if (type.IsEnum) { _writer.WriteValue(Enum.Format(type, value, "G")); return; }
            if (value is double d && (double.IsNaN(d) || double.IsInfinity(d)) ||
                value is float f && (float.IsNaN(f) || float.IsInfinity(f)))
                throw new ScriptResultException("NonFiniteNumber", path, type);
            if (value is bool || value is byte || value is sbyte || value is short || value is ushort ||
                value is int || value is uint || value is long || value is ulong || value is float ||
                value is double || value is decimal)
            { _writer.WriteValue(value); return; }
            if (value is Guid guid) { _writer.WriteValue(guid.ToString("D")); return; }
            if (value is DateTime date) { _writer.WriteValue(date.ToString("O", CultureInfo.InvariantCulture)); return; }
            if (value is DateTimeOffset offset) { _writer.WriteValue(offset.ToString("O", CultureInfo.InvariantCulture)); return; }

            // Must precede reflection, dictionary handling and GetEnumerator.
            if (IsRevitType(type))
            {
                // The Tools layer supplies a narrowly typed ElementId.Value adapter.
                var id = _elementIdValue?.Invoke(value);
                if (id.HasValue) { _writer.WriteValue(id.Value); return; }
                throw new ScriptResultException("RevitApiObject", path, type);
            }
            if (!_ancestors.Add(value)) throw new ScriptResultException("ReferenceCycle", path, type);
            try
            {
                if (value is JRaw) throw new ScriptResultException("RawJsonNotAllowed", path, type);
                if (value is JValue scalar)
                {
                    if (scalar.Type == JTokenType.Undefined || scalar.Type == JTokenType.Comment)
                        throw new ScriptResultException("UnsupportedJsonToken", path, type);
                    Write(scalar.Value, depth, path);
                }
                else if (value is JObject jsonObject)
                {
                    _writer.WriteStartObject();
                    foreach (var property in jsonObject.Properties())
                    { Property(property.Name, path); Write(property.Value, depth + 1, Child(path, property.Name)); }
                    _writer.WriteEndObject();
                }
                else if (value is JArray array) { Sequence(array, depth, path); }
                else if (value is JToken) throw new ScriptResultException("UnsupportedJsonToken", path, type);
                else if (value is IDictionary dictionary)
                {
                    _writer.WriteStartObject();
                    var iterator = dictionary.GetEnumerator();
                    try
                    {
                        while (Next(iterator, path))
                        {
                            if (!(iterator.Key is string key)) throw new ScriptResultException("DictionaryKeyMustBeString", path);
                            Property(key, path);
                            Write(iterator.Value, depth + 1, Child(path, key));
                        }
                    }
                    finally { (iterator as IDisposable)?.Dispose(); }
                    _writer.WriteEndObject();
                }
                else if (value is IEnumerable sequence) { Sequence(sequence, depth, path); }
                else if (IsAnonymous(type))
                {
                    // Read backing fields, never property getters (even for anonymous data).
                    var fields = type.GetFields(BindingFlags.Instance | BindingFlags.NonPublic);
                    if (fields.Any(field => !field.IsInitOnly || !field.Name.StartsWith("<", StringComparison.Ordinal) || !field.Name.EndsWith(">i__Field", StringComparison.Ordinal)))
                        throw new ScriptResultException("UnsupportedObject", path, type);
                    _writer.WriteStartObject();
                    foreach (var field in fields)
                    {
                        var end = field.Name.IndexOf('>');
                        var name = field.Name.Substring(1, end - 1);
                        Property(name, path);
                        Write(field.GetValue(value), depth + 1, Child(path, name));
                    }
                    _writer.WriteEndObject();
                }
                else throw new ScriptResultException("UnsupportedObject", path, type);
            }
            catch (ScriptResultException) { throw; }
            catch (Exception) { throw new ScriptResultException("ResultEvaluationFailed", path, type); }
            finally { _ancestors.Remove(value); }
        }

        private void Sequence(IEnumerable sequence, int depth, string path)
        {
            _writer.WriteStartArray();
            var iterator = sequence.GetEnumerator();
            try
            {
                var index = 0;
                while (Next(iterator, path)) Write(iterator.Current, depth + 1, Child(path, (index++).ToString(CultureInfo.InvariantCulture)));
            }
            finally { (iterator as IDisposable)?.Dispose(); }
            _writer.WriteEndArray();
        }

        private bool Next(IEnumerator iterator, string path)
        {
            if (_nodes >= MaxNodes) throw new ScriptResultException("NodeLimit", path);
            return iterator.MoveNext();
        }
        private void Property(string name, string path) { CheckString(name, path); _writer.WritePropertyName(name); }
        private static void CheckString(string value, string path)
        {
            if (Encoding.UTF8.GetByteCount(value) > MaxStringBytes) throw new ScriptResultException("StringLimit", path);
        }
        private static string Child(string path, string name) =>
            (path.Length > 180 ? path.Substring(0, 180) : path) + "." + (name.Length > 64 ? name.Substring(0, 64) : name);
        private static bool IsAnonymous(Type type) => type.IsSealed && type.IsDefined(typeof(CompilerGeneratedAttribute), false)
            && type.Name.StartsWith("<>f__AnonymousType", StringComparison.Ordinal);
        private static bool IsRevitType(Type type)
        {
            for (var current = type; current != null; current = current.BaseType)
            {
                var assembly = current.Assembly.GetName().Name ?? "";
                if (assembly.StartsWith("RevitAPI", StringComparison.OrdinalIgnoreCase) ||
                    (current.Namespace ?? "").StartsWith("Autodesk.Revit.", StringComparison.Ordinal)) return true;
            }
            return false;
        }
    }

    private sealed class IdentityComparer : IEqualityComparer<object>
    {
        public new bool Equals(object? x, object? y) => ReferenceEquals(x, y);
        public int GetHashCode(object obj) => RuntimeHelpers.GetHashCode(obj);
    }

    private sealed class BoundedWriter : TextWriter
    {
        private readonly StringBuilder _text = new();
        private readonly int _limit;
        public BoundedWriter(int limit) => _limit = limit;
        public override Encoding Encoding => Encoding.UTF8;
        // EscapeNonAscii makes JSON output ASCII, so characters equal UTF-8 bytes.
        private void Reserve(int count)
        {
            if (count > _limit - _text.Length) throw new ScriptResultException("ResponseSizeLimit", "result");
        }
        public override void Write(char value) { Reserve(1); _text.Append(value); }
        public override void Write(string? value) { if (value != null) { Reserve(value.Length); _text.Append(value); } }
        public override void Write(char[] buffer, int index, int count) { Reserve(count); _text.Append(buffer, index, count); }
        public override string ToString() => _text.ToString();
    }
}
