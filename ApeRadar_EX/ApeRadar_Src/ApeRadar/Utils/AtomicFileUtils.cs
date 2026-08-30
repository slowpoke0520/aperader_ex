using System;
using System.IO;
using System.Text;

namespace ApeRadar.Utils
{
    static internal class AtomicFileUtils
    {
        public static void WriteAllText(string filename, string contents)
        {
            string fullPath = Path.GetFullPath(filename);
            string? directory = Path.GetDirectoryName(fullPath);
            if (directory == null)
            {
                throw new ArgumentException("File path must include a directory", nameof(filename));
            }

            Directory.CreateDirectory(directory);
            string temporaryPath = Path.Combine(directory, $".{Path.GetFileName(fullPath)}.{Guid.NewGuid():N}.tmp");
            try
            {
                File.WriteAllText(temporaryPath, contents, new UTF8Encoding(false));
                File.Move(temporaryPath, fullPath, true);
            }
            finally
            {
                if (File.Exists(temporaryPath))
                {
                    File.Delete(temporaryPath);
                }
            }
        }
    }
}
