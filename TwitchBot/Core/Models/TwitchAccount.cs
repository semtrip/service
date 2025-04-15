namespace TwitchViewerBot.Core.Models
{
    public class TwitchAccount
    {
        public int Id { get; set; }
        public string Username { get; set; }
        public string AuthToken { get; set; }
        public bool IsValid { get; set; } = true;
        public DateTime LastChecked { get; set; } = DateTime.Now;
        public int? ProxyId { get; set; }
        public ProxyServer Proxy { get; set; }
    }
}