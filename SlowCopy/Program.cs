using System.Diagnostics;

namespace SlowCopy
{
    internal class Program
    {
        static long totalBytesCopied = 0;
        static long totalFilesCopied = 0;
        static long totalFilesToCopy = 0;
        static long totalBytesToCopy = 0;
        static bool isCancelled = false;
        static Stopwatch stopwatch = new Stopwatch();
        static object lockObject = new object();

        static void Main(string[] args)
        {
            Console.WriteLine("=== File Tree Copier with Speed Limit ===\n");

            // Parse command line arguments or prompt user
            if (args.Length >= 2)
            {
                string sourcePath = args[0];
                string destinationPath = args[1];
                int speedLimit = args.Length >= 3 ? int.Parse(args[2]) : 0;

                CopyDirectoryWithLimit(sourcePath, destinationPath, speedLimit);
            }
            else
            {
                RunInteractiveMode();
            }
        }

        static void RunInteractiveMode()
        {
            Console.Write("Enter source directory path: ");
            string sourcePath = Console.ReadLine();

            Console.Write("Enter destination directory path: ");
            string destinationPath = Console.ReadLine();

            Console.Write("Enter speed limit in KB/s (0 for unlimited): ");
            if (!int.TryParse(Console.ReadLine(), out int speedLimit))
            {
                speedLimit = 0;
            }

            Console.WriteLine("\nStarting copy operation...\n");
            CopyDirectoryWithLimit(sourcePath, destinationPath, speedLimit);
        }

        static void CopyDirectoryWithLimit(string sourceDir, string destDir, int speedLimitKBps)
        {
            try
            {
                // Validate paths
                if (!Directory.Exists(sourceDir))
                {
                    Console.WriteLine($"Error: Source directory '{sourceDir}' does not exist.");
                    return;
                }

                // Calculate totals first
                Console.WriteLine("Scanning files...");
                CalculateTotals(sourceDir);
                Console.WriteLine($"Found {totalFilesToCopy} files ({FormatBytes(totalBytesToCopy)})\n");

                // Setup cancellation
                Console.CancelKeyPress += (sender, e) =>
                {
                    e.Cancel = true;
                    isCancelled = true;
                    Console.WriteLine("\nCancellation requested. Finishing current file...");
                };

                // Start timer
                stopwatch.Start();

                // Start progress display thread
                Thread progressThread = new Thread(() => DisplayProgress(speedLimitKBps));
                progressThread.IsBackground = true;
                progressThread.Start();

                // Create destination directory
                Directory.CreateDirectory(destDir);

                // Start copying
                CopyDirectoryRecursive(sourceDir, destDir, speedLimitKBps);

                // Stop timer and wait for progress thread
                stopwatch.Stop();
                Thread.Sleep(1100); // Let progress thread display final stats

                if (!isCancelled)
                {
                    Console.WriteLine("\n\nCopy completed successfully!");
                    Console.WriteLine($"Copied {totalFilesCopied} files ({FormatBytes(totalBytesCopied)})");
                    Console.WriteLine($"Total time: {stopwatch.Elapsed:mm\\:ss}");

                    if (stopwatch.Elapsed.TotalSeconds > 0)
                    {
                        double avgSpeed = totalBytesCopied / stopwatch.Elapsed.TotalSeconds / 1024;
                        Console.WriteLine($"Average speed: {avgSpeed:F2} KB/s");
                    }
                }
                else
                {
                    Console.WriteLine("\n\nCopy operation cancelled.");
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"\nError: {ex.Message}");
            }
        }

        static void CalculateTotals(string directory)
        {
            try
            {
                // Count files in current directory
                string[] files = Directory.GetFiles(directory);
                foreach (string file in files)
                {
                    FileInfo fi = new FileInfo(file);
                    totalFilesToCopy++;
                    totalBytesToCopy += fi.Length;
                }

                // Recursively count subdirectories
                string[] subdirs = Directory.GetDirectories(directory);
                foreach (string subdir in subdirs)
                {
                    CalculateTotals(subdir);
                }
            }
            catch (UnauthorizedAccessException)
            {
                Console.WriteLine($"Warning: No access to '{directory}'");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: {ex.Message}");
            }
        }

        static void CopyDirectoryRecursive(string sourceDir, string destDir, int speedLimitKBps)
        {
            if (isCancelled) return;

            try
            {
                // Copy files in current directory
                string[] files = Directory.GetFiles(sourceDir);
                foreach (string file in files)
                {
                    if (isCancelled) return;

                    string destFile = Path.Combine(destDir, Path.GetFileName(file));
                    CopyFileWithLimit(file, destFile, speedLimitKBps);
                }

                // Copy subdirectories
                string[] subdirs = Directory.GetDirectories(sourceDir);
                foreach (string subdir in subdirs)
                {
                    if (isCancelled) return;

                    string destSubDir = Path.Combine(destDir, Path.GetFileName(subdir));
                    Directory.CreateDirectory(destSubDir);
                    CopyDirectoryRecursive(subdir, destSubDir, speedLimitKBps);
                }
            }
            catch (UnauthorizedAccessException)
            {
                Console.WriteLine($"Warning: No access to '{sourceDir}'");
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: {ex.Message}");
            }
        }

        static void CopyFileWithLimit(string sourceFile, string destFile, int speedLimitKBps)
        {
            if (isCancelled) return;

            try
            {
                FileInfo fileInfo = new FileInfo(sourceFile);
                long fileSize = fileInfo.Length;
                int bufferSize = 64 * 1024; // 64KB buffer
                byte[] buffer = new byte[bufferSize];

                using (FileStream sourceStream = new FileStream(sourceFile, FileMode.Open, FileAccess.Read))
                using (FileStream destStream = new FileStream(destFile, FileMode.Create, FileAccess.Write))
                {
                    int bytesRead;
                    long totalBytesRead = 0;

                    while ((bytesRead = sourceStream.Read(buffer, 0, bufferSize)) > 0)
                    {
                        if (isCancelled) return;

                        destStream.Write(buffer, 0, bytesRead);
                        totalBytesRead += bytesRead;

                        lock (lockObject)
                        {
                            totalBytesCopied += bytesRead;
                            totalFilesCopied = totalBytesRead == fileSize ? totalFilesCopied + 1 : totalFilesCopied;
                        }

                        // Apply speed limit if specified
                        if (speedLimitKBps > 0)
                        {
                            double targetTimePerChunk = (bytesRead / 1024.0) / speedLimitKBps;
                            int sleepTime = (int)(targetTimePerChunk * 1000);

                            if (sleepTime > 0)
                            {
                                Thread.Sleep(sleepTime);
                            }
                        }
                    }
                }
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error copying '{sourceFile}': {ex.Message}");
            }
        }

        static void DisplayProgress(int speedLimitKBps)
        {
            Console.CursorVisible = false;
            int lastLineCount = 0;

            
            while (!isCancelled && totalBytesCopied < totalBytesToCopy)
            {
                Console.Clear();
                // Clear previous progress lines
                for (int i = 0; i < lastLineCount; i++)
                {
                    Console.SetCursorPosition(0, 0);
                    Console.Write(new string(' ', Console.WindowWidth));
                    Console.SetCursorPosition(0, Console.CursorTop);
                }

                if (Console.CursorTop >= 1)
                {
                    Console.SetCursorPosition(0, Console.CursorTop);
                }

                // Calculate progress
                double percentage = totalBytesToCopy > 0 ? (double)totalBytesCopied / totalBytesToCopy * 100 : 0;
                string progressBar = GetProgressBar(percentage, 50);

                // Calculate speed
                double elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
                double currentSpeed = elapsedSeconds > 0 ? totalBytesCopied / elapsedSeconds / 1024 : 0;
                double estimatedTime = currentSpeed > 0 ? (totalBytesToCopy - totalBytesCopied) / 1024 / currentSpeed : 0;

                // Display progress
                Console.WriteLine($"Progress: {percentage:F1}%");
                Console.WriteLine($"[{progressBar}]");
                Console.WriteLine($"Files: {totalFilesCopied}/{totalFilesToCopy}");
                Console.WriteLine($"Data: {FormatBytes(totalBytesCopied)} / {FormatBytes(totalBytesToCopy)}");
                Console.WriteLine($"Speed: {currentSpeed:F2} KB/s (Limit: {(speedLimitKBps > 0 ? speedLimitKBps.ToString() + " KB/s" : "Unlimited")})");
                Console.WriteLine($"Elapsed: {stopwatch.Elapsed:mm\\:ss} | Remaining: {(estimatedTime > 0 ? TimeSpan.FromSeconds(estimatedTime).ToString(@"mm\:ss") : "--:--")}");

                lastLineCount = 6;

                Thread.Sleep(1000);
            }

            Console.CursorVisible = true;
        }

        static string GetProgressBar(double percentage, int length)
        {
            int filledLength = (int)(percentage / 100 * length);
            string bar = new string('█', filledLength) + new string('░', length - filledLength);
            return bar;
        }

        static string FormatBytes(long bytes)
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