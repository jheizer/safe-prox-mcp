using System.Text.Json;
using System.Text.Json.Serialization;

namespace SafeProxMcp.Models;

/// <summary>
/// Proxmox encodes booleans as 0/1 integers in its JSON API. System.Text.Json
/// refuses to read a number into a bool, so every boolean-ish field needs this.
/// </summary>
public sealed class PveBooleanConverter : JsonConverter<bool?>
{
    public override bool? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.True:
                return true;
            case JsonTokenType.False:
                return false;
            case JsonTokenType.Number:
                return reader.TryGetInt64(out var l) ? l != 0 : reader.TryGetDouble(out var d) && d != 0;
            case JsonTokenType.String:
                var text = reader.GetString();
                if (string.IsNullOrEmpty(text))
                    return null;
                return text is "1" or "true" or "yes" or "on";
            case JsonTokenType.Null:
                return null;
            default:
                throw new JsonException($"Unexpected token {reader.TokenType} when reading a Proxmox boolean.");
        }
    }

    public override void Write(Utf8JsonWriter writer, bool? value, JsonSerializerOptions options)
    {
        if (value is null)
            writer.WriteNullValue();
        else
            writer.WriteBooleanValue(value.Value);
    }
}

/// <summary>
/// Proxmox mixes numeric and numeric-string encodings for the same field
/// (qemu configs store "memory" as a string, lxc configs as a number) and uses
/// plain labels where a number is expected ("N/A" for wearout). Non-numeric
/// text becomes null instead of blowing up the whole payload.
/// </summary>
public sealed class PveInt64Converter : JsonConverter<long?>
{
    public override long? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                return reader.TryGetInt64(out var l) ? l : (long)reader.GetDouble();
            case JsonTokenType.String:
                var text = reader.GetString();
                if (string.IsNullOrWhiteSpace(text))
                    return null;
                if (long.TryParse(text, out var parsed))
                    return parsed;
                return double.TryParse(text, System.Globalization.CultureInfo.InvariantCulture, out var real)
                    ? (long)real
                    : null;
            case JsonTokenType.Null:
                return null;
            default:
                throw new JsonException($"Unexpected token {reader.TokenType} when reading a Proxmox integer.");
        }
    }

    public override void Write(Utf8JsonWriter writer, long? value, JsonSerializerOptions options)
    {
        if (value is null)
            writer.WriteNullValue();
        else
            writer.WriteNumberValue(value.Value);
    }
}

/// <summary>
/// Same tolerance as PveInt64Converter for the smaller Proxmox counters
/// (rpm, wearout, rotational).
/// </summary>
public sealed class PveInt32Converter : JsonConverter<int?>
{
    public override int? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
    {
        switch (reader.TokenType)
        {
            case JsonTokenType.Number:
                if (reader.TryGetInt32(out var i))
                    return i;
                var widened = reader.TryGetInt64(out var l) ? l : (long)reader.GetDouble();
                return widened is >= int.MinValue and <= int.MaxValue ? (int)widened : null;
            case JsonTokenType.String:
                var text = reader.GetString();
                if (string.IsNullOrWhiteSpace(text))
                    return null;
                return int.TryParse(text, out var parsed) ? parsed : null;
            case JsonTokenType.Null:
                return null;
            default:
                throw new JsonException($"Unexpected token {reader.TokenType} when reading a Proxmox integer.");
        }
    }

    public override void Write(Utf8JsonWriter writer, int? value, JsonSerializerOptions options)
    {
        if (value is null)
            writer.WriteNullValue();
        else
            writer.WriteNumberValue(value.Value);
    }
}
