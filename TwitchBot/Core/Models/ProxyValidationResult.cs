namespace TwitchViewerBot.Core.Models
{
    public class ProxyValidationResult
    {
        public ProxyServer Proxy { get; set; }
        public bool IsValid { get; set; }
        public string ErrorMessage { get; set; }
        public TimeSpan ResponseTime { get; set; }
        public string TestUrl { get; set; }
    }
}