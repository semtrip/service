using System;
using TwitchViewerBot.Core.Enums;

namespace TwitchViewerBot.Core.Models
{
    public class BotTask
    {
        public int Id { get; set; }
        public string ChannelUrl { get; set; }
        public string ChannelName => ChannelUrl?.Split('/').LastOrDefault();
        public int MaxViewers { get; set; }
        public int CurrentViewers { get; set; }
        public int AuthViewersCount { get; set; }
        public int GuestViewersCount { get; set; }
        public DateTime? CompletedTime { get; set; }
        public int RampUpTime { get; set; } // в минутах
        public TimeSpan Duration { get; set; }
        public TwitchViewerBot.Core.Enums.TaskStatus Status { get; set; }
        public TimeSpan ElapsedTime { get; set; }
        public DateTime? StartTime { get; set; }
        public DateTime? EndTime { get; set; }
        public DateTime LastUpdated { get; set; }
        public string ErrorMessage { get; set; }

        // Расчетные свойства
        public int ViewersPerMinute => (int)Math.Ceiling((double)MaxViewers / RampUpTime);
        public TimeSpan TimeRemaining => Duration - (DateTime.UtcNow - StartTime.GetValueOrDefault());
        public bool IsExpired => Status == TwitchViewerBot.Core.Enums.TaskStatus.Running && TimeRemaining <= TimeSpan.Zero;

    }
}