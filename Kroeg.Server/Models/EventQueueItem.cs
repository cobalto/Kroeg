using System;

namespace Kroeg.Server.Models
{
    public class EventQueueItem
    {
        public int Id { get; set; }

        public DateTime Added { get; set; }
        public DateTime NextAttempt { get; set; }
        public int AttemptCount { get; set; }

        public string Action { get; set; }
        public string Data { get; set; }
    }
}
