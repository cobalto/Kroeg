using System;

namespace Kroeg.Server.Models
{
    public class WebsubSubscription
    {
        public int Id { get; set; }

        public DateTime Expiry { get; set; }
        public string Callback { get; set; }
        public string Secret { get; set; }

        public string UserId { get; set; }
        public APEntity User { get; set; }
    }
}
