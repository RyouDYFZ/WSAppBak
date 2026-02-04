using System;
using System.IO;
using System.Text.Json;
using System.Threading.Tasks;
using StoreCrack.StoreAgent.Services;

namespace StoreCrack.StoreAgent.Program
{
    internal class Program
    {
        private static async Task Main(string[] args)
        {
            Console.WriteLine("StoreAgent - Windows Store App Downloader and Installer");
            Console.WriteLine("============================================");
            
            try
            {
                // Parse command line arguments
                var parsedArgs = ParseArguments(args);
                
                // Check for help request
                if (parsedArgs.ContainsKey("help"))
                {
                    ShowHelp();
                    Environment.Exit(0);
                }
                
                // Create StoreService instance
                var storeService = new StoreService();
                
                // Check for uninstall operation
                if (parsedArgs.ContainsKey("uninstall"))
                {
                    if (!parsedArgs.ContainsKey("pfn"))
                    {
                        Console.WriteLine("Error: -pfn is required for uninstall operation.");
                        ShowHelp();
                        Environment.Exit(1);
                    }
                    
                    string packageFamilyName = parsedArgs["pfn"];
                    bool success = await storeService.UninstallAsync(packageFamilyName);
                    
                    if (success)
                    {
                        Console.WriteLine("Uninstallation completed successfully.");
                        Environment.Exit(0);
                    }
                    else
                    {
                        Console.WriteLine("Uninstallation failed.");
                        Environment.Exit(1);
                    }
                }
                
                // Check for install certificate operation
                if (parsedArgs.ContainsKey("installcert"))
                {
                    if (!parsedArgs.ContainsKey("certPath"))
                    {
                        Console.WriteLine("Error: -certPath is required for installcert operation.");
                        ShowHelp();
                        Environment.Exit(1);
                    }
                    
                    string certPath = parsedArgs["certPath"];
                    bool success = storeService.InstallCertificate(certPath);
                    
                    if (success)
                    {
                        Console.WriteLine("Certificate installed successfully.");
                        Environment.Exit(0);
                    }
                    else
                    {
                        Console.WriteLine("Certificate installation failed.");
                        Environment.Exit(1);
                    }
                }
                
                // Check for install local package operation
                if (parsedArgs.ContainsKey("installpkg"))
                {
                    if (!parsedArgs.ContainsKey("pkgPath"))
                    {
                        Console.WriteLine("Error: -pkgPath is required for installpkg operation.");
                        ShowHelp();
                        Environment.Exit(1);
                    }
                    
                    string pkgPath = parsedArgs["pkgPath"];
                    var storeService = new StoreService();
                    
                    // Ensure certificate is installed
                    if (!storeService.EnsureCertificateInstalled(pkgPath))
                    {
                        Console.WriteLine("Warning: Failed to ensure certificate for package.");
                    }
                    
                    // Install the package
                    try
                    {
                        var pm = new Windows.Management.Deployment.PackageManager();
                        var result = await pm.AddPackageAsync(new Uri(pkgPath), null, Windows.Management.Deployment.DeploymentOptions.ForceApplicationShutdown);
                        await WaitForDeploymentCompletion(result);
                        Console.WriteLine("Package installed successfully.");
                        Environment.Exit(0);
                    }
                    catch (Exception ex)
                    {
                        Console.WriteLine($"Error installing package: {ex.Message}");
                        Environment.Exit(1);
                    }
                }
                
                // Validate required arguments for install operation
                if (!parsedArgs.ContainsKey("productId") && !parsedArgs.ContainsKey("pfn"))
                {
                    Console.WriteLine("Error: Either -productId or -pfn is required.");
                    ShowHelp();
                    Environment.Exit(1);
                }
                
                // Determine input type
                string input = parsedArgs.ContainsKey("productId") ? parsedArgs["productId"] : parsedArgs["pfn"];
                bool isProductId = parsedArgs.ContainsKey("productId");
                
                // Run the install process
                var result = await storeService.ProcessApp(input, isProductId);
                
                // Write result to JSON file
                string outputPath = parsedArgs.TryGetValue("output", out string? customOutput) 
                    ? customOutput 
                    : Path.Combine(Environment.CurrentDirectory, "install_result.json");
                
                await File.WriteAllTextAsync(outputPath, JsonSerializer.Serialize(result, new JsonSerializerOptions { WriteIndented = true }));
                
                Console.WriteLine($"Success: Result written to {outputPath}");
                Environment.Exit(0);
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error: {ex.Message}");
                Environment.Exit(2);
            }
        }
        
        private static System.Collections.Generic.Dictionary<string, string> ParseArguments(string[] args)
        {
            var result = new System.Collections.Generic.Dictionary<string, string>();
            
            for (int i = 0; i < args.Length; i++)
            {
                string arg = args[i].ToLower();
                
                if (arg.StartsWith("-"))
                {
                    string key = arg.Substring(1);
                    
                    if (i + 1 < args.Length && !args[i + 1].StartsWith("-"))
                    {
                        result[key] = args[i + 1];
                        i++;
                    }
                    else
                    {
                        result[key] = "true";
                    }
                }
            }
            
            return result;
        }
        
        private static void ShowHelp()
        {
            Console.WriteLine("Usage: StoreAgent [options]");
            Console.WriteLine("");
            Console.WriteLine("Options:");
            Console.WriteLine("  -productId <id>    Specify the ProductId from Store (for install)");
            Console.WriteLine("  -pfn <name>        Specify the PackageFamilyName (for install or uninstall)");
            Console.WriteLine("  -uninstall         Uninstall the specified package");
            Console.WriteLine("  -installcert       Install a certificate");
            Console.WriteLine("  -certPath <path>   Specify the path to the .cer file");
            Console.WriteLine("  -installpkg        Install a local package");
            Console.WriteLine("  -pkgPath <path>    Specify the path to the .msix file");
            Console.WriteLine("  -output <path>     Specify output path for result JSON");
            Console.WriteLine("  -help              Show this help message");
            Console.WriteLine("");
            Console.WriteLine("Examples:");
            Console.WriteLine("  Install using ProductId:");
            Console.WriteLine("    StoreAgent -productId 9WZDNCRFHVQM");
            Console.WriteLine("  Install using PackageFamilyName:");
            Console.WriteLine("    StoreAgent -pfn Microsoft.WindowsCalculator_8wekyb3d8bbwe");
            Console.WriteLine("  Uninstall using PackageFamilyName:");
            Console.WriteLine("    StoreAgent -uninstall -pfn Microsoft.WindowsCalculator_8wekyb3d8bbwe");
            Console.WriteLine("  Install certificate:");
            Console.WriteLine("    StoreAgent -installcert -certPath \"C:\\path\\to\\certificate.cer\"");
            Console.WriteLine("  Install local package:");
            Console.WriteLine("    StoreAgent -installpkg -pkgPath \"C:\\path\\to\\app.msix\"");
        }
        
        private static async Task WaitForDeploymentCompletion(Windows.Foundation.IAsyncOperationWithProgress<Windows.Management.Deployment.DeploymentResult, Windows.Management.Deployment.DeploymentProgress> operation)
        {
            var completionSource = new System.Threading.Tasks.TaskCompletionSource<bool>();
            
            operation.Completed = (asyncInfo, asyncStatus) =>
            {
                if (asyncStatus == Windows.Foundation.AsyncStatus.Completed)
                {
                    var result = asyncInfo.GetResults();
                    if (result.ErrorCode != 0)
                    {
                        completionSource.TrySetException(new Exception($"Deployment failed with error code: {result.ErrorCode}, message: {result.ErrorText}"));
                    }
                    else
                    {
                        completionSource.TrySetResult(true);
                    }
                }
                else if (asyncStatus == Windows.Foundation.AsyncStatus.Error)
                {
                    completionSource.TrySetException(new Exception("Deployment operation failed with error status"));
                }
            };
            
            await completionSource.Task;
        }
    }
}