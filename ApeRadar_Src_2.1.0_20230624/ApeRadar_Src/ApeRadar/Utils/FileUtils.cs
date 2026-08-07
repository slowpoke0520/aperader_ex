using Newtonsoft.Json.Linq;
using System;
using System.IO;
using System.Linq;

namespace ApeRadar.Utils
{
    static internal class FileUtils
    {
        private static DateTimeOffset latestFileWriteTime = DateTimeOffset.MinValue;

        public static string GetLatestTempArenaInfoFile(bool requireFileToBeNewer)
        {
            if (Properties.Settings.Default.GamePath == "")
            {
                return "";
            }

            string[] tempArenaInfoPath = Array.Empty<string>();
            if (Directory.Exists($@"{Properties.Settings.Default.GamePath}\replays\"))
            {
                tempArenaInfoPath = Directory.GetFiles($@"{Properties.Settings.Default.GamePath}\replays\", "tempArenaInfo.json", SearchOption.AllDirectories);
            }

            if (tempArenaInfoPath.Length == 0)
            {
                LogUtils.WriteDebug("tempArenaInfo file not found. ");
                latestFileWriteTime = DateTimeOffset.MinValue;
                return "";
            }
            else
            {
                foreach (string path in tempArenaInfoPath)
                {
                    LogUtils.WriteDebug($"tempArenaInfo file path={path}");
                }
            }

            DateTimeOffset dt = DateTimeOffset.MinValue;
            string latestFileName = "";
            foreach (string filename in tempArenaInfoPath)
            {
                FileInfo fi = new(filename);

                if (fi.LastWriteTime > dt)
                {
                    dt = fi.LastWriteTime;
                    latestFileName = filename;
                }
            }

            bool result = (dt > latestFileWriteTime);
            latestFileWriteTime = dt;

            if (result || !requireFileToBeNewer)
            {
                return latestFileName;
            }
            else
            {
                return "";
            }
        }

        public static JObject ReadTempArenaInfoFile(string filename)
        {
            string tempArenaInfo;
            using FileStream fs = new(filename, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete);
            using StreamReader sr = new(fs);
            byte[] buffer = new byte[4];

            int firstByte = sr.Peek();

            if (firstByte == 0x7B)
            {
                tempArenaInfo = sr.ReadToEnd();
            }
            else if (firstByte == 0x12)
            {
                fs.Seek(0, SeekOrigin.Begin);
                fs.Read(buffer, 0, 4);
                if (Enumerable.SequenceEqual(buffer, new byte[] { 0x12, 0x32, 0x34, 0x11 }))
                {
                    fs.Seek(8, SeekOrigin.Begin);
                    fs.Read(buffer, 0, 4);
                    sr.DiscardBufferedData();
                    tempArenaInfo = sr.ReadToEnd()[..BitConverter.ToInt32(buffer, 0)];
                }
                else
                {
                    throw new FileFormatException("FileFormatIncorrect");
                }
            }
            else
            {
                throw new FileFormatException("FileFormatIncorrect");
            }

            LogUtils.WriteInfo($"tempArenaInfo:{tempArenaInfo}");
            try
            {
                return JsonUtils.Parse(tempArenaInfo);
            }
            catch (Exception ex)
            {
                throw new FileFormatException("FileFormatIncorrect", ex);
            }
        }
    }
}
