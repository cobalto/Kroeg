using System;
using Microsoft.IdentityModel.Tokens;

// For more information on enabling MVC for empty projects, visit https://go.microsoft.com/fwlink/?LinkID=397860

namespace Kroeg.Server.Configuration
{
    public class JwtTokenSettings
    {
        public SigningCredentials Credentials { get; set; }
        public string Audience { get; set; }
        public string Issuer { get; set; }
        public TimeSpan ExpiryTime { get; set; }

        public static string ActorClaim => "actor";

        public TokenValidationParameters ValidationParameters { get; set; }
    }
}
