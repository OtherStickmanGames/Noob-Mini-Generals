using System;
using System.IO;
using Newtonsoft.Json;
using UnityEngine;


    public static class Json
    {
        private static JsonSerializerSettings settings = new JsonSerializerSettings
        {
            TypeNameHandling = TypeNameHandling.Auto,
            ObjectCreationHandling = ObjectCreationHandling.Replace,
            NullValueHandling = NullValueHandling.Ignore,
            MissingMemberHandling = MissingMemberHandling.Ignore,
            Error = (sender, eventArgs) =>
            {
                Debug.LogError(
                    $"{eventArgs.ErrorContext.Error.Message} {eventArgs.ErrorContext.OriginalObject} {eventArgs.ErrorContext.Member}");
            }
        };

        public static T Deserialize<T>(string json)
        {
            return JsonConvert.DeserializeObject<T>(json, settings);
        }

        public static object Deserialize(string json)
        {
            return JsonConvert.DeserializeObject(json, settings);
        }

        public static string Serialize(object obj, Type type, bool indented = false)
        {
            return JsonConvert.SerializeObject(obj, type, indented ? Formatting.Indented : Formatting.None, settings);
        }

        public static string Serialize<T>(T obj, bool indented = false)
        {
            return JsonConvert.SerializeObject(obj, indented ? Formatting.Indented : Formatting.None, settings);
        }

        public static T DeserializeFromFile<T>(string filePath)
        {
            using StreamReader file = File.OpenText(filePath);
            var serializer = new JsonSerializer();
            var obj = (T)serializer.Deserialize(file, typeof(T));

            return obj;
        }
    }

    [JsonObject]
    public struct SerializedVector3
    {
        public float x;
        public float y;
        public float z;

        public static SerializedVector3 zero
        {
            get
            {
                return new SerializedVector3() { x = 0.0f, y = 0.0f, z = 0.0f };
            }
        }

        public static SerializedVector3 one
        {
            get
            {
                return new SerializedVector3() { x = 1.0f, y = 1.0f, z = 1.0f };
            }
        }

        public static SerializedVector3 FromVector3(Vector3 vector)
        {
            return new SerializedVector3() { x = vector.x, y = vector.y, z = vector.z };
        }

        public Vector3 ToVector3()
        {
            return new Vector3(x, y, z);
        }

        public override string ToString()
        {
            return ToVector3().ToString();
        }

        public static implicit operator Vector3(SerializedVector3 v)
        {
            return v.ToVector3();
        }
    }

    [JsonObject]
    public struct SerializedVector2
    {
        public float x;
        public float y;

        public static SerializedVector2 zero
        {
            get
            {
                return new SerializedVector2() { x = 0.0f, y = 0.0f };
            }
        }

        public static SerializedVector2 one
        {
            get
            {
                return new SerializedVector2() { x = 1.0f, y = 1.0f };
            }
        }

        public static SerializedVector2 FromVector2(Vector2 vector)
        {
            return new SerializedVector2() { x = vector.x, y = vector.y };
        }

        public static SerializedVector2 FromVector3(Vector3 vector)
        {
            return new SerializedVector2() { x = vector.x, y = vector.y };
        }

        public Vector2 ToVector2()
        {
            return new Vector2(x, y);
        }

        public static implicit operator Vector2(SerializedVector2 v)
        {
            return v.ToVector2();
        }
        
        public static implicit operator SerializedVector2(Vector3 v)
        {
            return FromVector3(v);
        }
        public static implicit operator SerializedVector2(Vector2 v)
        {
            return FromVector2(v);
        }
    }

[JsonObject]
public struct SerializedRect
{
    public float x;
    public float y;

    public float width;
    public float height;

    public static SerializedRect zero
    {
        get
        {
            return new SerializedRect() { x = 0.0f, y = 0.0f, width = 0.0f, height = 0.0f };
        }
    }

    public static SerializedRect FromRect(Rect rect)
    {
        return new SerializedRect() { x = rect.x, y = rect.y, width = rect.width, height = rect.height };
    }

    public SerializedRect(float x, float y, float width, float height)
    {
        this.x = x;
        this.y = y;
        this.width = width;
        this.height = height;
    }

    public Rect ToRect()
    {
        return new Rect(x, y, width, height);
    }

    public static implicit operator Rect(SerializedRect r)
    {
        return r.ToRect();
    }
}
