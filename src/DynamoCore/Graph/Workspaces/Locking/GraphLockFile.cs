using System;
using System.IO;
using System.Security;
using System.Text;
using Newtonsoft.Json;

namespace Dynamo.Graph.Workspaces.Locking
{
    internal static class GraphLockFile
    {
        private static readonly JsonSerializer Serializer = JsonSerializer.Create(new JsonSerializerSettings
        {
            Formatting = Formatting.Indented,
            TypeNameHandling = TypeNameHandling.None
        });

        internal static string PathFor(string graphPath)
        {
            var fullPath = Path.GetFullPath(graphPath);
            var directory = Path.GetDirectoryName(fullPath);
            var fileName = Path.GetFileName(fullPath);

            return Path.Combine(directory, "." + fileName + ".dynlock");
        }

        internal static bool TryCreateExclusive(string sidecarPath, GraphLockInfo info)
        {
            try
            {
                using (var stream = new FileStream(sidecarPath, FileMode.CreateNew, FileAccess.Write, FileShare.Read))
                using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
                {
                    Serializer.Serialize(writer, info);
                }

                return true;
            }
            catch (IOException) when (File.Exists(sidecarPath))
            {
                return false;
            }
            catch (UnauthorizedAccessException)
            {
                throw;
            }
            catch (SecurityException)
            {
                throw;
            }
            catch (IOException)
            {
                throw;
            }
        }

        internal static bool TryRead(string sidecarPath, out GraphLockInfo info)
        {
            info = null;

            try
            {
                using (var stream = File.Open(sidecarPath, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete))
                using (var reader = new StreamReader(stream, Encoding.UTF8))
                using (var jsonReader = new JsonTextReader(reader))
                {
                    info = Serializer.Deserialize<GraphLockInfo>(jsonReader);
                }

                return info != null;
            }
            catch (FileNotFoundException)
            {
                return false;
            }
            catch (DirectoryNotFoundException)
            {
                return false;
            }
            catch (JsonException)
            {
                return false;
            }
        }

        internal static void WriteHeartbeat(string sidecarPath, GraphLockInfo info)
        {
            var tempPath = sidecarPath + ".tmp";

            using (var stream = new FileStream(tempPath, FileMode.Create, FileAccess.Write, FileShare.None))
            using (var writer = new StreamWriter(stream, new UTF8Encoding(false)))
            {
                Serializer.Serialize(writer, info);
            }

            try
            {
                File.Replace(tempPath, sidecarPath, null, true);
            }
            catch (IOException)
            {
                File.Move(tempPath, sidecarPath, true);
            }
            catch (PlatformNotSupportedException)
            {
                File.Move(tempPath, sidecarPath, true);
            }
        }

        internal static void TryDelete(string sidecarPath)
        {
            try
            {
                File.Delete(sidecarPath);
            }
            catch (IOException)
            {
                return;
            }
            catch (UnauthorizedAccessException)
            {
                return;
            }
            catch (SecurityException)
            {
                return;
            }
        }
    }
}
