using System;
using System.Collections.Generic;
using System.Text.Json;
using System.Text.Json.Serialization;

namespace ArtaleAI.Models
{
    /// <summary>
    /// 浮點數陣列 JSON 轉換器
    /// 強制在 JSON 中顯示小數點（即使值為整數）
    /// 確保座標精度在序列化時不會丟失視覺化資訊
    /// </summary>
    public class FloatArrayConverter : JsonConverter<float[]>
    {
        /// <summary>
        /// 讀取 JSON 並轉換為 float[]
        /// </summary>
        public override float[]? Read(ref Utf8JsonReader reader, Type typeToConvert, JsonSerializerOptions options)
        {
            if (reader.TokenType != JsonTokenType.StartArray)
                throw new JsonException("Expected StartArray token");

            var list = new List<float>();
            while (reader.Read())
            {
                if (reader.TokenType == JsonTokenType.EndArray)
                    return list.ToArray();

                if (reader.TokenType == JsonTokenType.Number)
                    list.Add(reader.GetSingle());
                else if (reader.TokenType == JsonTokenType.String)
                    list.Add(float.Parse(reader.GetString() ?? "0"));
            }

            throw new JsonException("Unexpected end of array");
        }

        /// <summary>
        /// 將 float[] 寫入 JSON，強制顯示小數點（至少 2 位小數）
        /// 使用 RawValue 方式確保小數點始終顯示
        /// </summary>
        public override void Write(Utf8JsonWriter writer, float[] value, JsonSerializerOptions options)
        {
            writer.WriteStartArray();
            foreach (var f in value)
            {
                var rounded = (float)Math.Round(f, 2);
                var formatted = rounded.ToString("F2", System.Globalization.CultureInfo.InvariantCulture);
                writer.WriteRawValue(formatted);
            }
            writer.WriteEndArray();
        }
    }

    /// <summary>
    /// 提供資料模型的 JSON 序列化輔助方法
    /// 統一管理 JSON 序列化選項
    /// </summary>
    public static class DataModelHelper
    {
        /// <summary>
        /// 將物件序列化為 JSON 字串
        /// 使用縮排格式，方便閱讀
        /// </summary>
        public static string ToJson<T>(T obj)
        {
            return JsonSerializer.Serialize(obj, new JsonSerializerOptions
            {
                WriteIndented = true
            });
        }

        /// <summary>
        /// 將 JSON 字串反序列化為物件
        /// </summary>
        public static T? FromJson<T>(string json)
        {
            return JsonSerializer.Deserialize<T>(json);
        }
    }
}
