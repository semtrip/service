using TwitchViewerBot.Core.Enums;

namespace TwitchViewerBot.Core.Models
{
    public class BotTask
    {
        public int Id { get; set; }
        public string ChannelUrl { get; set; }
        public int MaxViewers { get; set; }
        public int RampUpTime { get; set; } // minutes
        public TimeSpan Duration { get; set; }
        public bool UseAuth { get; set; }
        public Core.Enums.TaskStatus Status { get; set; } = Core.Enums.TaskStatus.Pending;
        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public int CurrentViewers { get; set; }
    }
    
}