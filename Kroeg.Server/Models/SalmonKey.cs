namespace Kroeg.Server.Models
{
    public class SalmonKey
    {
        public int SalmonKeyId { get; set; }
        public string EntityId { get; set; }
        public APEntity Entity { get; set; }
        public string PrivateKey { get; set; }
    }
}
