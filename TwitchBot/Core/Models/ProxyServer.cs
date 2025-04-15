using System;
using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;

namespace TwitchViewerBot.Core.Models
{
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
        public bool IsValid { get; set; }
        public DateTime LastChecked { get; set; }
    }
}