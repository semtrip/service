using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TwitchViewerBot.Core.Models
{
    public enum ProxyType
    {
        HTTP,
        SOCKS5
    }

    public class ProxyServer
    {
        [Key]
        [DatabaseGenerated(DatabaseGeneratedOption.Identity)]
        public int Id { get; set; }
        
        [Required]
        public string Address { get; set; } = string.Empty;
        
        [Required]
        public int Port { get; set; }
        
        public string Username { get; set; } = string.Empty;
        public string Password { get; set; } = string.Empty;

        [Column(TypeName = "nvarchar(10)")]
        public ProxyType Type { get; set; } = ProxyType.HTTP;

        public bool IsValid { get; set; } = false;
        public DateTime LastChecked { get; set; }
    }
}