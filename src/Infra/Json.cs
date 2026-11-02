using System.IO;
using System.Runtime.Serialization.Json;

namespace Toolkit
{
    internal static class Json
    {
        public static void Write<T>(string path, T value)
        {
            string dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir)) Directory.CreateDirectory(dir);

            using (FileStream fs = File.Create(path))
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T));
                serializer.WriteObject(fs, value);
            }
        }

        public static T Read<T>(string path)
        {
            using (FileStream fs = File.OpenRead(path))
            {
                DataContractJsonSerializer serializer = new DataContractJsonSerializer(typeof(T));
                return (T)serializer.ReadObject(fs);
            }
        }
    }

    internal static class Bytes
    {
        public static bool Equal(byte[] left, byte[] right)
        {
            if (ReferenceEquals(left, right)) return true;
            if (left == null || right == null) return false;
            if (left.Length != right.Length) return false;

            for (int i = 0; i < left.Length; i++)
            {
                if (left[i] != right[i]) return false;
            }

            return true;
        }
    }
}
