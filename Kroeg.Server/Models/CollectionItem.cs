using System.ComponentModel.DataAnnotations;

namespace Kroeg.Server.Models
{
    public class CollectionItem
    {
        [Key]
        public int CollectionItemId { get; set; }

        public string CollectionId { get; set; }
        public APEntity Collection { get; set; }

        public string ElementId { get; set; }
        public APEntity Element { get; set; }
    }
}
