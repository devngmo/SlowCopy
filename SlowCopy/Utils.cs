using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace SlowCopy
{
    public class Utils
    {
        public static string GetProgressBar(double percentage, int length)
        {
            int filledLength = (int)(percentage / 100 * length);
            string bar = new string('█', filledLength) + new string('░', length - filledLength);
            return bar;
        }

        public static string FormatBytes(long bytes)
        {
            string[] sizes = { "B", "KB", "MB", "GB", "TB" };
            int order = 0;
            double len = bytes;

            while (len >= 1024 && order < sizes.Length - 1)
            {
                order++;
                len /= 1024;
            }

            return $"{len:F2} {sizes[order]}";
        }
    }
}
