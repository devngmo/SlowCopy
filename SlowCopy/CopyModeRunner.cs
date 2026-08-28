using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;

namespace SlowCopy
{
    internal class CopyModeRunner
    {
        long totalBytesCopied = 0;
        long totalFilesCopied = 0;
        long totalFilesToCopy = 0;
        long totalBytesToCopy = 0;
        bool isCancelled = false;
        Stopwatch stopwatch = new Stopwatch();
        object lockObject = new object();
        string sourcePath;
        string destinationPath;

        public void StartCLI()
        {
            Console.Write("Enter source directory path: ");
            sourcePath = Console.ReadLine();

            Console.Write("Enter destination directory path: ");
            destinationPath = Console.ReadLine();

            Console.Write("Enter speed limit in KB/s (0 for unlimited): ");
            if (!int.TryParse(Console.ReadLine(), out int speedLimit))
            {
                speedLimit = 0;
            }

            Console.WriteLine("\nStarting copy operation...\n");
            CopyDirectoryWithLimit(speedLimit);
        }

        void CopyDirectoryWithLimit(int speedLimitKBps, bool isMoveOperation = false)
        {
            try
            {
                // Validate paths
                if (!Directory.Exists(sourcePath))
                {
                    Console.WriteLine($"Error: Source directory '{sourcePath}' does not exist.");
                    return;
                }

                // Calculate totals first
                Console.WriteLine("Scanning files...");
                CalculateTotals(sourcePath);
                Console.WriteLine($"Found {totalFilesToCopy} files ({Utils.FormatBytes(totalBytesToCopy)})\n");

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
                Directory.CreateDirectory(destinationPath);

                // Start copying
                CopyDirectoryRecursive(sourcePath, destinationPath, speedLimitKBps, isMoveOperation);

                // Stop timer and wait for progress thread
                stopwatch.Stop();
                Thread.Sleep(1100); // Let progress thread display final stats

                if (!isCancelled)
                {
                    Console.WriteLine("\n\nCopy completed successfully!");
                    Console.WriteLine($"Copied {totalFilesCopied} files ({Utils.FormatBytes(totalBytesCopied)})");
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

        void CalculateTotals(string directory)
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

        /// <summary>
        /// Copies a directory recursively with an optional speed limit and move operation.
        /// </summary>
        /// <param name="sourceDir"></param>
        /// <param name="destDir"></param>
        /// <param name="speedLimitKBps"></param>
        /// <param name="isMoveOperation"></param>
        /// <returns>True if the operation was successful, false otherwise.</returns>
        bool CopyDirectoryRecursive(string sourceDir, string destDir, int speedLimitKBps, bool isMoveOperation = false)
        {
            if (isCancelled) return false;

            try
            {
                // Copy files in current directory
                string[] files = Directory.GetFiles(sourceDir);
                foreach (string file in files)
                {
                    if (isCancelled) return false;

                    string destFile = Path.Combine(destDir, Path.GetFileName(file));
                    bool done = CopyFileWithLimit(file, destFile, speedLimitKBps, isMoveOperation);
                    if (!done) return false;
                }

                // Copy subdirectories
                string[] subdirs = Directory.GetDirectories(sourceDir);
                foreach (string subdir in subdirs)
                {
                    if (isCancelled) return false;

                    string destSubDir = Path.Combine(destDir, Path.GetFileName(subdir));
                    Directory.CreateDirectory(destSubDir);
                    bool done = CopyDirectoryRecursive(subdir, destSubDir, speedLimitKBps, isMoveOperation);
                    if (!done)
                    {
                        return false;
                    } else
                    {
                        if (isMoveOperation)
                        {
                            try
                            {
                                Directory.Delete(subdir, true);
                            }
                            catch (Exception ex)
                            {
                                Console.WriteLine($"Warning: Could not delete '{subdir}': {ex.Message}");
                            }
                        }
                    }
                }
                return true;
            }
            catch (UnauthorizedAccessException)
            {
                Console.WriteLine($"Warning: No access to '{sourceDir}'");
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Warning: {ex.Message}");
                return false;
            }
        }

        bool CopyFileWithLimit(string sourceFile, string destFile, int speedLimitKBps, bool isMoveOperation = false)
        {
            if (isCancelled) return false;

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
                        if (isCancelled) return false;

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
                if (!isCancelled && File.Exists(destFile))
                {
                    if (isMoveOperation)
                    {
                        File.Delete(sourceFile);
                    }
                    return true;
                }
                return false;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error copying '{sourceFile}': {ex.Message}");
                return false;
            }
        }

        void DisplayProgress(int speedLimitKBps)
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
                string progressBar = Utils.GetProgressBar(percentage, 50);

                // Calculate speed
                double elapsedSeconds = stopwatch.Elapsed.TotalSeconds;
                double currentSpeed = elapsedSeconds > 0 ? totalBytesCopied / elapsedSeconds / 1024 : 0;
                double estimatedTime = currentSpeed > 0 ? (totalBytesToCopy - totalBytesCopied) / 1024 / currentSpeed : 0;

                // Display progress
                Console.WriteLine($"Progress: {percentage:F1}%");
                Console.WriteLine($"[{progressBar}]");
                Console.WriteLine($"Files: {totalFilesCopied}/{totalFilesToCopy}");
                Console.WriteLine($"Data: {Utils.FormatBytes(totalBytesCopied)} / {Utils.FormatBytes(totalBytesToCopy)}");
                Console.WriteLine($"Speed: {currentSpeed:F2} KB/s (Limit: {(speedLimitKBps > 0 ? speedLimitKBps.ToString() + " KB/s" : "Unlimited")})");
                Console.WriteLine($"Elapsed: {stopwatch.Elapsed:mm\\:ss} | Remaining: {(estimatedTime > 0 ? TimeSpan.FromSeconds(estimatedTime).ToString(@"mm\:ss") : "--:--")}");

                lastLineCount = 6;

                Thread.Sleep(1000);
            }

            Console.CursorVisible = true;
        }

        internal void StartMoveCLI()
        {
            Console.Write("Enter source directory path: ");
            sourcePath = Console.ReadLine();

            Console.Write("Enter destination directory path: ");
            destinationPath = Console.ReadLine();

            Console.Write("Enter speed limit in KB/s (0 for unlimited): ");
            if (!int.TryParse(Console.ReadLine(), out int speedLimit))
            {
                speedLimit = 0;
            }

            Console.WriteLine("\nStarting move operation...\n");
            CopyDirectoryWithLimit(speedLimit, true);
        }

        internal void StartDeleteCLI()
        {
            Console.Write("Enter directory path to delete: ");
            sourcePath = Console.ReadLine();

            Console.WriteLine("\nStarting delete operation...\n");
            Directory.Delete(sourcePath, true);
            Console.WriteLine("\nDelete operation completed.\n");
        }
    }
}
