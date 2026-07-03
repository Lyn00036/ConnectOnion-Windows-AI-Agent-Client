using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;
using System.Text.Encodings.Web;
using System.Text.Json;

namespace ConnectOnion.Protocol;

/// <summary>
/// Produces the exact canonical JSON the ConnectOnion SDK signs: keys sorted,
/// no whitespace, JS-compatible escaping. Port of <c>canonicalJSON</c> in
/// <c>address.ts</c> (which does <c>Object.keys().sort()</c> then
/// <c>JSON.stringify</c>).
///
/// The signature only verifies if this string is BYTE-IDENTICAL to what the JS
/// side produced, so this is validated by a JS↔C# conformance test.
/// </summary>
public static class CanonicalJson
{
    // UnsafeRelaxedJsonEscaping matches JS JSON.stringify, which does not escape
    // '+', '<', '>', '&' etc. System.Text.Json's default encoder would, breaking
    // byte-for-byte equality for payloads containing those characters.
    private static readonly JsonWriterOptions Options = new()
    {
        Encoder = JavaScriptEncoder.UnsafeRelaxedJsonEscaping,
        Indented = false,
        SkipValidation = true,
    };

    /// <summary>
    /// Serializes an object with keys sorted by UTF-16 code unit (matching JS
    /// <c>Array.prototype.sort</c> on ASCII keys). Values may be string, bool,
    /// or an integral/floating number.
    /// </summary>
    public static string Serialize(IEnumerable<KeyValuePair<string, object?>> pairs)
    {
        using var buffer = new MemoryStream();
        using (var writer = new Utf8JsonWriter(buffer, Options))
        {
            writer.WriteStartObject();
            foreach (var pair in pairs.OrderBy(p => p.Key, StringComparer.Ordinal))
            {
                writer.WritePropertyName(pair.Key);
                WriteValue(writer, pair.Value);
            }
            writer.WriteEndObject();
        }
        return Encoding.UTF8.GetString(buffer.ToArray());
    }

    private static void WriteValue(Utf8JsonWriter writer, object? value)
    {
        switch (value)
        {
            case null:
                writer.WriteNullValue();
                break;
            case string s:
                writer.WriteStringValue(s);
                break;
            case bool b:
                writer.WriteBooleanValue(b);
                break;
            case int i:
                writer.WriteNumberValue(i);
                break;
            case long l:
                writer.WriteNumberValue(l);
                break;
            case double d:
                writer.WriteNumberValue(d);
                break;
            default:
                throw new NotSupportedException($"Unsupported canonical JSON value type: {value.GetType()}");
        }
    }
}
