using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;

namespace Kroeg.Server.Models
{
    public class WebSubClient
    {
        public int WebSubClientId { get; set; }

        public APEntity ForUser { get; set; }
        public string ForUserId { get; set; }

        public APEntity TargetUser { get; set; }
        public string TargetUserId { get; set; }

        public string Topic { get; set; }

        public DateTime Expiry { get; set; }
        public string Secret { get; set; }
    }
}
