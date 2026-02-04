using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Net.Http;
using System.Net.Http.Headers;
using System.Security.Cryptography.X509Certificates;
using System.Text;
using System.Text.Json;
using System.Text.RegularExpressions;
using System.Threading.Tasks;
using Windows.Management.Deployment;

namespace StoreCrack.StoreAgent.Services
{
    internal class StoreService
    {
        private readonly HttpClient _httpClient;
        private readonly PackageManager _packageManager;
        private readonly string _downloadsDirectory;
        
        public StoreService()
        {
            _httpClient = new HttpClient();
            _httpClient.DefaultRequestHeaders.UserAgent.ParseAdd("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36");
            
            _packageManager = new PackageManager();
            
            // Create downloads directory
            _downloadsDirectory = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.CommonApplicationData), "StoreCrack", "Downloads");
            Directory.CreateDirectory(_downloadsDirectory);
        }
        
        public async Task<InstallResult> ProcessApp(string input, bool isProductId)
        {
            try
            {
                Console.WriteLine($"Processing: {input}");
                
                // Step 1: Get packages from rg-adguard
                Console.WriteLine("1. Fetching packages from rg-adguard...");
                var packages = await GetPackagesFromRgAdguard(input, isProductId);
                
                if (packages.Count == 0)
                {
                    throw new Exception("No packages found for the given input.");
                }
                
                // Step 2: Download packages
                Console.WriteLine($"2. Downloading {packages.Count} packages...");
                var downloadedPackages = await DownloadPackages(packages);
                
                // Step 3: Install packages
                Console.WriteLine("3. Installing packages...");
                await InstallPackages(downloadedPackages);
                
                // Step 4: Get installed package info
                Console.WriteLine("4. Retrieving installation info...");
                var packageInfo = await GetInstalledPackageInfo(packages.First().PackageFamilyName);
                
                return packageInfo;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error during processing: {ex.Message}");
                throw;
            }
        }
        
        private async Task<List<StorePackage>> GetPackagesFromRgAdguard(string input, bool isProductId)
        {
            // Prepare request
            var content = new FormUrlEncodedContent(new[]
            {
                new KeyValuePair<string, string>("type", isProductId ? "ProductId" : "PackageFamilyName"),
                new KeyValuePair<string, string>("url", input),
                new KeyValuePair<string, string>("ring", "Retail")
            });
            
            // Send request
            var response = await _httpClient.PostAsync("https://store.rg-adguard.net/api/GetFiles", content);
            response.EnsureSuccessStatusCode();
            
            // Parse response
            var htmlContent = await response.Content.ReadAsStringAsync();
            var packages = ParseRgAdguardResponse(htmlContent);
            
            // Filter and sort packages
            return FilterAndSortPackages(packages);
        }
        
        private List<StorePackage> ParseRgAdguardResponse(string htmlContent)
        {
            var packages = new List<StorePackage>();
            
            // Use Regex to find all download links
            var matches = Regex.Matches(
                htmlContent,
                "<a[^>]+href=\"(https://[^\"]+)\"",
                RegexOptions.IgnoreCase
            );
            
            foreach (Match m in matches)
            {
                var url = m.Groups[1].Value;
                var fileName = Path.GetFileName(new Uri(url).AbsolutePath);
                
                if (string.IsNullOrEmpty(fileName))
                    continue;
                
                var extension = Path.GetExtension(fileName).ToLowerInvariant();
                if (extension is not (".msix" or ".msixbundle" or ".appx"))
                    continue;
                
                var isFramework = 
                    fileName.Contains("Framework", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Contains("VCLibs", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Contains("NET.Native", StringComparison.OrdinalIgnoreCase) ||
                    fileName.Contains("UI.Xaml", StringComparison.OrdinalIgnoreCase);
                
                var architecture = 
                    fileName.Contains("_x64_") ? "x64" :
                    fileName.Contains("_x86_") ? "x86" :
                    fileName.Contains("_arm64_") ? "arm64" :
                    "neutral";
                
                packages.Add(new StorePackage
                {
                    FileName = fileName,
                    DownloadUrl = new Uri(url),
                    Extension = extension,
                    IsFramework = isFramework,
                    Architecture = architecture,
                    PackageFamilyName = ExtractPackageFamilyName(fileName)
                });
            }
            
            return packages;
        }
        
        private List<StorePackage> FilterAndSortPackages(List<StorePackage> packages)
        {
            // Filter packages
            var filtered = packages.Where(p => 
                p.Extension == ".msix" || 
                p.Extension == ".msixbundle" || 
                (p.Extension == ".appx" && p.IsFramework)
            ).ToList();
            
            // Calculate priority for each package
            foreach (var p in filtered)
            {
                p.Priority = 100;
                
                // Highest priority: neutral msixbundle
                if (p.Extension == ".msixbundle" && p.FileName.Contains("_neutral_~"))
                    p.Priority = 0;
                
                // Second priority: x64 msix
                else if (p.Extension == ".msix" && p.Architecture == "x64")
                    p.Priority = 10;
                
                // Framework packages have lower priority
                else if (p.IsFramework)
                    p.Priority = 50;
            }
            
            // Sort packages by priority
            return filtered
                .OrderBy(p => p.Priority)
                .ThenBy(p => p.IsFramework)
                .ToList();
        }
        
        private async Task<List<string>> DownloadPackages(List<StorePackage> packages)
        {
            var downloadedPaths = new List<string>();
            
            foreach (var package in packages)
            {
                try
                {
                    string filePath = Path.Combine(_downloadsDirectory, package.FileName);
                    
                    // Skip if file already exists
                    if (File.Exists(filePath))
                    {
                        Console.WriteLine($"Skipping {package.FileName} (already exists)");
                        downloadedPaths.Add(filePath);
                        continue;
                    }
                    
                    // Download file
                    Console.WriteLine($"Downloading {package.FileName}...");
                    using (var response = await _httpClient.GetAsync(package.DownloadUrl, HttpCompletionOption.ResponseHeadersRead))
                    {
                        response.EnsureSuccessStatusCode();
                        
                        using (var stream = await response.Content.ReadAsStreamAsync())
                        using (var fileStream = new FileStream(filePath, FileMode.Create, FileAccess.Write, FileShare.None))
                        {
                            await stream.CopyToAsync(fileStream);
                        }
                    }
                    
                    downloadedPaths.Add(filePath);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error downloading {package.FileName}: {ex.Message}");
                    // Continue with other packages
                }
            }
            
            if (downloadedPaths.Count == 0)
            {
                throw new Exception("Failed to download any packages.");
            }
            
            return downloadedPaths;
        }
        
        private async Task InstallPackages(List<string> packagePaths)
        {
            // Separate main packages and frameworks
            var mainPackages = packagePaths.Where(p => Path.GetExtension(p) == ".msix" || Path.GetExtension(p) == ".msixbundle").ToList();
            var frameworkPackages = packagePaths.Where(p => Path.GetExtension(p) == ".appx" || Path.GetFileName(p).Contains("Framework")).ToList();
            
            // Install frameworks first
            foreach (var frameworkPath in frameworkPackages)
            {
                try
                {
                    Console.WriteLine($"Processing framework: {Path.GetFileName(frameworkPath)}");
                    
                    // Ensure certificate is installed
                    if (!EnsureCertificateInstalled(frameworkPath))
                    {
                        Console.WriteLine($"Warning: Failed to ensure certificate for framework {Path.GetFileName(frameworkPath)}");
                    }
                    
                    // Install the framework
                    Console.WriteLine($"Installing framework: {Path.GetFileName(frameworkPath)}");
                    var result = await _packageManager.AddPackageAsync(new Uri(frameworkPath), null, DeploymentOptions.None);
                    await WaitForDeploymentCompletion(result);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error installing framework {Path.GetFileName(frameworkPath)}: {ex.Message}");
                    // Continue with other packages
                }
            }
            
            // Install main packages
            foreach (var mainPath in mainPackages)
            {
                try
                {
                    Console.WriteLine($"Processing main package: {Path.GetFileName(mainPath)}");
                    
                    // Ensure certificate is installed
                    if (!EnsureCertificateInstalled(mainPath))
                    {
                        Console.WriteLine($"Warning: Failed to ensure certificate for main package {Path.GetFileName(mainPath)}");
                    }
                    
                    // Install the main package
                    Console.WriteLine($"Installing main package: {Path.GetFileName(mainPath)}");
                    var result = await _packageManager.AddPackageAsync(new Uri(mainPath), null, DeploymentOptions.ForceApplicationShutdown);
                    await WaitForDeploymentCompletion(result);
                }
                catch (Exception ex)
                {
                    Console.WriteLine($"Error installing main package {Path.GetFileName(mainPath)}: {ex.Message}");
                    // Continue with other packages
                }
            }
        }
        
        private async Task WaitForDeploymentCompletion(Windows.Foundation.IAsyncOperationWithProgress<Windows.Management.Deployment.DeploymentResult, Windows.Management.Deployment.DeploymentProgress> operation)
        {
            var completionSource = new TaskCompletionSource<bool>();
            
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
        
        private async Task<InstallResult> GetInstalledPackageInfo(string packageFamilyName)
        {
            // Find the package
            var package = _packageManager.FindPackageForUser(string.Empty, packageFamilyName);
            
            if (package == null)
            {
                throw new Exception($"Package {packageFamilyName} not found installed.");
            }
            
            // Create result
            return new InstallResult
            {
                FullName = package.Id.FullName,
                Version = $"{package.Id.Version.Major}.{package.Id.Version.Minor}.{package.Id.Version.Build}.{package.Id.Version.Revision}",
                InstallPath = package.InstalledLocation.Path
            };
        }
        
        private string ExtractPackageFamilyName(string fileName)
        {
            // Try to extract package family name from file name
            // Format: [PackageName]_[Version]_[Architecture]_[PackageFamilyName].extension
            var parts = fileName.Split('_');
            if (parts.Length >= 4)
            {
                // The package family name is usually the last part before the extension
                var pfnPart = parts[^2]; // Second to last part
                if (pfnPart.Contains("wekyb3d8bbwe")) // Common suffix for Store packages
                {
                    return $"{parts[0]}_{pfnPart}";
                }
            }
            return string.Empty;
        }
        
        /// <summary>
        /// Uninstalls an app using PackageManager
        /// </summary>
        /// <param name="packageFamilyName">PackageFamilyName of the app to uninstall</param>
        /// <returns>True if uninstallation succeeded, false otherwise</returns>
        public async Task<bool> UninstallAsync(string packageFamilyName)
        {
            var pm = new PackageManager();
            
            // Find the package
            var pkg = pm.FindPackageForUser(string.Empty, packageFamilyName);
            if (pkg == null)
            {
                Console.WriteLine($"Package {packageFamilyName} not found.");
                return false;
            }
            
            // Get full name
            string fullName = pkg.Id.FullName;
            Console.WriteLine($"Uninstalling package: {fullName}");
            
            // Uninstall the package
            var operation = pm.RemovePackageAsync(fullName);
            await operation.AsTask();
            
            // Check status
            if (operation.Status == Windows.Foundation.AsyncStatus.Completed)
            {
                Console.WriteLine("Uninstallation completed successfully.");
                return true;
            }
            else
            {
                Console.WriteLine($"Uninstallation failed with status: {operation.Status}");
                if (operation.ErrorCode != null)
                {
                    Console.WriteLine($"Error code: {operation.ErrorCode.HResult}");
                }
                return false;
            }
        }
        
        /// <summary>
        /// Installs a certificate to the system trust store
        /// </summary>
        /// <param name="cerPath">Path to the .cer file</param>
        /// <returns>True if certificate installation succeeded, false otherwise</returns>
        public bool InstallCertificate(string cerPath)
        {
            try
            {
                if (!File.Exists(cerPath))
                {
                    Console.WriteLine($"Certificate file not found: {cerPath}");
                    return false;
                }
                
                // Load the certificate
                var cert = new X509Certificate2(cerPath);
                Console.WriteLine($"Installing certificate: {cert.Subject}");
                
                // Open the TrustedPeople store
                using var store = new X509Store(
                    StoreName.TrustedPeople,
                    StoreLocation.LocalMachine
                );
                
                store.Open(OpenFlags.ReadWrite);
                
                // Check if certificate already exists
                var existing = store.Certificates
                    .Find(X509FindType.FindByThumbprint, cert.Thumbprint, false);
                
                if (existing.Count > 0)
                {
                    Console.WriteLine("Certificate already installed.");
                    store.Close();
                    return true;
                }
                
                // Add the certificate
                store.Add(cert);
                store.Close();
                
                Console.WriteLine("Certificate installed successfully.");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error installing certificate: {ex.Message}");
                return false;
            }
        }
        
        /// <summary>
        /// Ensures the certificate for a package is installed
        /// </summary>
        /// <param name="packagePath">Path to the MSIX/APPX package</param>
        /// <returns>True if certificate is installed or doesn't need to be installed, false otherwise</returns>
        public bool EnsureCertificateInstalled(string packagePath)
        {
            try
            {
                if (!File.Exists(packagePath))
                {
                    Console.WriteLine($"Package file not found: {packagePath}");
                    return false;
                }
                
                // Try to extract certificate from the package
                var cert = new X509Certificate2(
                    X509Certificate.CreateFromSignedFile(packagePath)
                );
                
                Console.WriteLine($"Found certificate in package: {cert.Subject}");
                
                // Open the TrustedPeople store
                using var store = new X509Store(
                    StoreName.TrustedPeople,
                    StoreLocation.LocalMachine
                );
                
                store.Open(OpenFlags.ReadWrite);
                
                // Check if certificate already exists
                var existing = store.Certificates
                    .Find(X509FindType.FindByThumbprint, cert.Thumbprint, false);
                
                if (existing.Count > 0)
                {
                    Console.WriteLine("Certificate already installed.");
                    store.Close();
                    return true;
                }
                
                // Add the certificate
                store.Add(cert);
                store.Close();
                
                Console.WriteLine("Certificate installed successfully.");
                return true;
            }
            catch (Exception ex)
            {
                Console.WriteLine($"Error ensuring certificate: {ex.Message}");
                return false;
            }
        }
    }
}