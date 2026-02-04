using System;
using System.Diagnostics;
using System.IO;
using System.Xml;

namespace WSAppBak
{
	internal class WSAppBak
	{
		private const string AppName = "Windows Store App Backup";
		private const string AppCreator = "Kiran Murmu";
		private const string WSAppXmlFile = "AppxManifest.xml";
		
		private readonly string AppCurrentDirectory = Directory.GetCurrentDirectory();
		private readonly string ToolsDirectory;
		
		private bool IsRunning = true;
		private string WSAppName;
		private string WSAppPath;
		private string WSAppVersion;
		private string WSAppFileName;
		private string WSAppOutputPath;
		private string WSAppProcessorArchitecture;
		private string WSAppPublisher;
		
		public WSAppBak()
		{
			ToolsDirectory = Path.Combine(AppCurrentDirectory, "WSAppBak");
		}

		public void Run()
		{
			try
			{
				DisplayHeader();
				ReadInput();
			}
			catch (Exception ex)
			{
				DisplayError($"An unexpected error occurred: {ex.Message}");
				WaitForUserInput();
			}
		}

		private void DisplayHeader()
		{
			Console.Clear();
			Console.WriteLine($"\t\t'{AppName}' by {AppCreator}");
			Console.WriteLine("================================================================================");
		}

		private void ReadInput()
		{
			while (IsRunning)
			{
				DisplayHeader();
				WSAppPath = GetValidPath("Enter the App path: ", ValidateAppPath);
				WSAppOutputPath = GetValidPath("\nEnter the Output path: ", ValidateOutputPath);
				
				WSAppFileName = Path.GetFileName(WSAppPath);
				ReadAppManifest();
				
				ProcessAppPackage();
			}
		}

		private string GetValidPath(string prompt, Func<string, bool> validator)
		{
			while (IsRunning)
			{
				Console.Write(prompt);
				string path = Console.ReadLine().Trim();
				
				// Handle quoted paths
				if (path.StartsWith("\"") && path.EndsWith("\""))
				{
					path = path.Substring(1, path.Length - 2);
				}
				
				if (validator(path))
				{
					return path;
				}
				
				Console.WriteLine("\nInvalid path. Please try again.");
				WaitForUserInput();
			}
			return string.Empty;
		}

		private bool ValidateAppPath(string path)
		{
			return File.Exists(Path.Combine(path, WSAppXmlFile));
		}

		private bool ValidateOutputPath(string path)
		{
			return Directory.Exists(path);
		}

		private void ReadAppManifest()
		{
			try
			{
				string manifestPath = Path.Combine(WSAppPath, WSAppXmlFile);
				using (XmlReader xmlReader = XmlReader.Create(manifestPath))
				{
					while (xmlReader.Read())
					{
						if (xmlReader.IsStartElement() && xmlReader.Name == "Identity")
						{
							WSAppName = xmlReader["Name"];
							WSAppPublisher = xmlReader["Publisher"];
							WSAppVersion = xmlReader["Version"];
							WSAppProcessorArchitecture = xmlReader["ProcessorArchitecture"];
							break;
						}
					}
				}
			}
			catch (Exception ex)
			{
				DisplayError($"Failed to read app manifest: {ex.Message}");
				IsRunning = false;
			}
		}

		private void ProcessAppPackage()
		{
			try
			{
				if (!VerifyToolsExist())
				{
					IsRunning = false;
					return;
				}
				
				if (CreateAppxPackage())
				{
					if (CreateCertificate())
					{
						if (ConvertCertificate())
						{
							SignAppPackage();
						}
					}
				}
			}
			catch (Exception ex)
			{
				DisplayError($"Error processing app package: {ex.Message}");
				WaitForUserInput();
				IsRunning = false;
			}
		}

		private bool VerifyToolsExist()
		{
			string[] requiredTools = { "MakeAppx.exe", "MakeCert.exe", "Pvk2Pfx.exe", "SignTool.exe" };
			
			foreach (string tool in requiredTools)
			{
				string toolPath = Path.Combine(ToolsDirectory, tool);
				if (!File.Exists(toolPath))
				{
					DisplayError($"Required tool not found: {tool}");
					return false;
				}
			}
			
			return true;
		}

		private bool CreateAppxPackage()
		{
			string toolPath = Path.Combine(ToolsDirectory, "MakeAppx.exe");
			string appxOutputPath = Path.Combine(WSAppOutputPath, $"{WSAppFileName}.appx");
			string args = $"pack -d \"{WSAppPath}\" -p \"{appxOutputPath}\" -l";
			
			// Clean up existing file
			if (File.Exists(appxOutputPath))
			{
				File.Delete(appxOutputPath);
			}
			
			Console.WriteLine("\nPlease wait.. Creating '.appx' package file.\n");
			string result = RunProcess(toolPath, args);
			
			if (result.ToLower().Contains("succeeded"))
			{
				DisplayHeader();
				Console.WriteLine($"Package '{Path.GetFileName(appxOutputPath)}' creation succeeded.");
				return true;
			}
			else
			{
				DisplayError($"Package '{Path.GetFileName(appxOutputPath)}' creation failed.");
				WaitForUserInput();
				IsRunning = false;
				return false;
			}
		}

		private bool CreateCertificate()
		{
			string toolPath = Path.Combine(ToolsDirectory, "MakeCert.exe");
			string pvkPath = Path.Combine(WSAppOutputPath, $"{WSAppFileName}.pvk");
			string cerPath = Path.Combine(WSAppOutputPath, $"{WSAppFileName}.cer");
			string args = $"-n \"{WSAppPublisher}\" -r -a sha256 -len 2048 -cy end -h 0 -eku 1.3.6.1.5.5.7.3.3 -b 01/01/2000 -sv \"{pvkPath}\" \"{cerPath}\"";
			
			// Clean up existing files
			CleanupFile(pvkPath);
			CleanupFile(cerPath);
			
			Console.WriteLine("\nPlease wait.. Creating certificate for the package.\n");
			Console.Write("Certificate creation: ");
			string result = RunProcess(toolPath, args);
			
			if (result.ToLower().Contains("succeeded"))
			{
				Console.WriteLine(" succeeded");
				return true;
			}
			else
			{
				DisplayError("Failed to create certificate for the package.");
				WaitForUserInput();
				IsRunning = false;
				return false;
			}
		}

		private bool ConvertCertificate()
		{
			string toolPath = Path.Combine(ToolsDirectory, "Pvk2Pfx.exe");
			string pvkPath = Path.Combine(WSAppOutputPath, $"{WSAppFileName}.pvk");
			string cerPath = Path.Combine(WSAppOutputPath, $"{WSAppFileName}.cer");
			string pfxPath = Path.Combine(WSAppOutputPath, $"{WSAppFileName}.pfx");
			string args = $"-pvk \"{pvkPath}\" -spc \"{cerPath}\" -pfx \"{pfxPath}\"";
			
			// Clean up existing file
			CleanupFile(pfxPath);
			
			Console.WriteLine("\nPlease wait.. Converting certificate to sign the package.\n");
			Console.Write("Certificate conversion: ");
			string result = RunProcess(toolPath, args);
			
			if (string.IsNullOrEmpty(result))
			{
				Console.WriteLine(" succeeded");
				return true;
			}
			else
			{
				DisplayError("Failed to convert certificate to sign the package.");
				WaitForUserInput();
				IsRunning = false;
				return false;
			}
		}

		private void SignAppPackage()
		{
			string toolPath = Path.Combine(ToolsDirectory, "SignTool.exe");
			string appxPath = Path.Combine(WSAppOutputPath, $"{WSAppFileName}.appx");
			string pfxPath = Path.Combine(WSAppOutputPath, $"{WSAppFileName}.pfx");
			string args = $"sign -fd SHA256 -a -f \"{pfxPath}\" \"{appxPath}\"";
			
			Console.WriteLine("\n\nPlease wait.. Signing the package, this may take some minutes.\n");
			string result = RunProcess(toolPath, args);
			
			if (result.ToLower().Contains("successfully signed"))
			{
				DisplaySuccess();
			}
			else
			{
				DisplayError("Failed to sign the package.");
			}
			
			IsRunning = false;
			WaitForUserInput();
		}

		private string RunProcess(string fileName, string args)
		{
			string result = string.Empty;
			
			try
			{
				ProcessStartInfo startInfo = new ProcessStartInfo
				{
					FileName = fileName,
					Arguments = args,
					UseShellExecute = false,
					RedirectStandardOutput = true,
					RedirectStandardError = true,
					CreateNoWindow = true
				};
				
				using (Process process = Process.Start(startInfo))
				{
					while (!process.StandardOutput.EndOfStream)
					{
						string line = process.StandardOutput.ReadLine();
						Console.WriteLine(line);
						if (!string.IsNullOrEmpty(line))
						{
							result = line;
						}
					}
					
					// Also read error output
					while (!process.StandardError.EndOfStream)
					{
						string errorLine = process.StandardError.ReadLine();
						if (!string.IsNullOrEmpty(errorLine))
						{
							Console.WriteLine($"Error: {errorLine}");
						}
					}
					
					process.WaitForExit();
				}
			}
			catch (Exception ex)
			{
				DisplayError($"Error running process: {ex.Message}");
			}
			
			return result;
		}

		private void DisplaySuccess()
		{
			DisplayHeader();
			Console.WriteLine("Package signing succeeded!");
			Console.WriteLine();
			Console.WriteLine("================================================================================");
			Console.WriteLine("Important: Please install the '.cer' file to [Local Computer\\Trusted Root Certification Authorities]");
			Console.WriteLine("before installing the App Package or use 'WSAppPkgIns.exe' file to install the App Package!");
			Console.WriteLine("================================================================================");
		}

		private void DisplayError(string message)
		{
			Console.WriteLine();
			Console.WriteLine($"Error: {message}");
		}

		private void WaitForUserInput()
		{
			Console.Write("\nPress any key to continue...");
			Console.ReadKey(true);
		}

		private void CleanupFile(string filePath)
		{
			if (File.Exists(filePath))
			{
				try
				{
					File.Delete(filePath);
				}
				catch (Exception ex)
				{
					DisplayError($"Failed to delete file {Path.GetFileName(filePath)}: {ex.Message}");
				}
			}
		}
	}
}