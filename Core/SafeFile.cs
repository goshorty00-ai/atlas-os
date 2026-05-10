using System;
using System.IO;
using System.Text;

namespace AtlasAI.Core
{
    public static class SafeFile
    {
        public static void WriteAllTextAtomic(string path, string content)
        {
            var dir = Path.GetDirectoryName(path);
            if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                Directory.CreateDirectory(dir);

            try
            {
                if (File.Exists(path))
                {
                    try { File.SetAttributes(path, FileAttributes.Normal); } catch { }
                }
            }
            catch
            {
            }

            var tmp = path + ".tmp_" + Guid.NewGuid().ToString("N");
            try
            {
                File.WriteAllText(tmp, content, new UTF8Encoding(encoderShouldEmitUTF8Identifier: false));
                if (File.Exists(path))
                {
                    try
                    {
                        File.Replace(tmp, path, destinationBackupFileName: null);
                        return;
                    }
                    catch
                    {
                    }
                }

                File.Move(tmp, path, overwrite: true);
            }
            finally
            {
                try { if (File.Exists(tmp)) File.Delete(tmp); } catch { }
            }
        }
    }
}
