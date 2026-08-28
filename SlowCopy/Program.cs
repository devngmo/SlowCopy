using System.Diagnostics;
using System.Text;

namespace SlowCopy
{
    internal class Program
    {
        

        static void Main(string[] args)
        {
            Console.WriteLine("=== File Tree Copier with Speed Limit ===\n");

            // Parse command line arguments or prompt user
            if (args.Length >= 2)
            {
                string sourcePath = args[0];
                string destinationPath = args[1];
                int speedLimit = args.Length >= 3 ? int.Parse(args[2]) : 0;

                //CopyDirectoryWithLimit(sourcePath, destinationPath, speedLimit);
            }
            else
            {
                RunInteractiveMode();
            }
        }

        static void RunInteractiveMode()
        {
            string mode = "";
            while (mode == "")
            {
                Console.Write("Select Mode (C:Copy, M:Move, D:Delete): ");
                mode = Console.ReadLine()?.ToUpper();
                if (mode == "C" || mode == "M" || mode == "D")
                {
                    break;
                }
                else
                {
                    Console.WriteLine("Invalid mode. Please enter C, M, or D.");
                    mode = "";
                }
            }

            var runner = new CopyModeRunner();
            if (mode == "C")
            {
                runner.StartCLI();
            }
            else if (mode == "M")
            {
                runner.StartMoveCLI();
            }
            else if (mode == "D")
            {
                
                runner.StartDeleteCLI();
            }
        }
    }
}