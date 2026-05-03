using System;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace PathToTarkov.Models;

/// <summary>
/// Handles Position as either [x,y,z] array or {x,y,z} object (both formats used in PTT configs).
/// </summary>
public class SpawnPositionConverter : JsonConverter<SpawnPointPosition>
{
    public override SpawnPointPosition Read(ref Utf8JsonReader reader, Type t, JsonSerializerOptions opts)
    {
        if (reader.TokenType == JsonTokenType.StartArray)
        {
            // [x, y, z] array format
            reader.Read(); float x = reader.GetSingle();
            reader.Read(); float y = reader.GetSingle();
            reader.Read(); float z = reader.GetSingle();
            reader.Read(); // EndArray
            return new SpawnPointPosition { X = x, Y = y, Z = z };
        }

        if (reader.TokenType == JsonTokenType.StartObject)
        {
            float x = 0, y = 0, z = 0;
            while (reader.Read() && reader.TokenType != JsonTokenType.EndObject)
            {
                if (reader.TokenType != JsonTokenType.PropertyName) continue;
                var name = reader.GetString()?.ToLowerInvariant();
                reader.Read();
                var val = reader.GetSingle();
                if      (name == "x") x = val;
                else if (name == "y") y = val;
                else if (name == "z") z = val;
            }
            return new SpawnPointPosition { X = x, Y = y, Z = z };
        }

        throw new JsonException($"Unexpected token {reader.TokenType} for SpawnPointPosition");
    }

    public override void Write(Utf8JsonWriter writer, SpawnPointPosition value, JsonSerializerOptions opts)
    {
        writer.WriteStartObject();
        writer.WriteNumber("x", value.X);
        writer.WriteNumber("y", value.Y);
        writer.WriteNumber("z", value.Z);
        writer.WriteEndObject();
    }
}
