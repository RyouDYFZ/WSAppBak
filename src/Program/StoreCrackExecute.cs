using System;
using System.Collections.Generic;

namespace StoreCrack.Program
{
	internal class StoreCrackExecute
	{
		private static void Main(string[] args)
		{
			Console.Title = "StoreCrack";
			
			// Parse command line arguments
			var parsedArgs = ParseArguments(args);
			
			// Check for help request
			if (parsedArgs.ContainsKey("help"))
			{
				ShowHelp();
				return;
			}
			
			// Create StoreCrackService instance
			StoreCrack.Services.StoreCrackService storeCrackService = new StoreCrack.Services.StoreCrackService();
			
			// Check if running in silent mode
			if (parsedArgs.ContainsKey("silent") && parsedArgs.ContainsKey("appPath") && parsedArgs.ContainsKey("outputPath"))
			{
				// Run in silent mode with provided arguments
				int exitCode = storeCrackService.RunSilent(parsedArgs["appPath"], parsedArgs["outputPath"]);
				Environment.Exit(exitCode);
			}
			else
			{
				// Run in interactive mode
				storeCrackService.Run();
			}
		}
		
		private static Dictionary<string, string> ParseArguments(string[] args)
		{
			Dictionary<string, string> result = new Dictionary<string, string>();
			
			for (int i = 0; i < args.Length; i++)
			{
				string arg = args[i].ToLower();
				
				if (arg.StartsWith("-"))
				{
					string key = arg.Substring(1);
					
					// Check if this is a flag without a value
					if (key == "silent" || key == "help")
					{
						result[key] = "true";
					}
					// Check if this is a key-value pair
					else if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
					{
						result[key] = args[i + 1];
						i++;
					}
				}
			}
			
			return result;
		}
		
		private static void ShowHelp()
		{
			Console.WriteLine("StoreCrack");
			Console.WriteLine("========================================");
			Console.WriteLine();
			Console.WriteLine("Usage:");
			Console.WriteLine("  StoreCrack [options]");
			Console.WriteLine();
			Console.WriteLine("Options:");
			Console.WriteLine("  -appPath <path>   Specify the path to the app directory");
			Console.WriteLine("  -outputPath <path> Specify the output directory for the package");
			Console.WriteLine("  -silent           Run in silent mode without user interaction");
			Console.WriteLine("  -help             Show this help message");
			Console.WriteLine();
			Console.WriteLine("Examples:");
			Console.WriteLine("  Interactive mode:");
			Console.WriteLine("    StoreCrack");
			Console.WriteLine();
			Console.WriteLine("  Silent mode:");
			Console.WriteLine("    StoreCrack -silent -appPath \"C:\\Path\\To\\App\" -outputPath \"D:\\Output\\Directory\"");
		}
	}
}
