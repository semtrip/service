using System;
using TwitchViewerBot.Core.Enums;

namespace TwitchViewerBot.Core.Models
{
    public class BotTask
    {
        public int Id { get; set; }
        public string ChannelUrl { get; set; } = string.Empty;
        public int MaxViewers { get; set; }
        public int CurrentViewers { get; set; }
        public int RampUpTime { get; set; }
        public TimeSpan Duration { get; set; }
        public Core.Enums.TaskStatus Status { get; set; } = Core.Enums.TaskStatus.Pending;
        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public DateTime LastUpdated { get; set; } = DateTime.UtcNow;

        public int AuthViewersPercent => new Random(Id).Next(30, 71);
        public int AuthViewersCount => (int)(MaxViewers * (AuthViewersPercent / 100.0));
        public int GuestViewersCount => MaxViewers - AuthViewersCount;
    }
}