using System;

namespace StoreCrack.StoreAgent.Services
{
    internal class StorePackage
    {
        public string FileName { get; set; } = string.Empty;
        public Uri DownloadUrl { get; set; } = new Uri("https://example.com");
        public bool IsFramework { get; set; }
        public string Architecture { get; set; } = string.Empty;
        public string Extension { get; set; } = string.Empty;
        public string PackageFamilyName { get; set; } = string.Empty;
        public int Priority { get; set; }
    }
}