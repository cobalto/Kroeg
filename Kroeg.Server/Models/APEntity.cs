using System.ComponentModel.DataAnnotations;
using System.ComponentModel.DataAnnotations.Schema;
using System.Linq;
using Kroeg.ActivityStreams;
using System;

namespace Kroeg.Server.Models
{
    public class APEntity
    {
        [Key]
        public string Id { get; set; }

        [Column(TypeName = "jsonb")]
        public string SerializedData { get; set; }

        public string Type { get; set; }

        public DateTime Updated { get; set; }

        public bool IsOwner { get; set; }

        public static APEntity From(string id, ASObject @object)
        {
            var type = (string)@object["type"].FirstOrDefault()?.Primitive;
            if (type?.StartsWith("_") != false) type = "Unknown";

            @object.Replace("id", new ASTerm(id));

            return new APEntity
            {
                Id = id,
                Data = @object,
                Type = type
            };
        }


        public static APEntity From(ASObject @object, bool isOwner = false)
        {
            var type = (string)@object["type"].FirstOrDefault()?.Primitive;
            if (type?.StartsWith("_") != false) type = "Unknown";

            var id = (string)@object["id"].FirstOrDefault()?.Primitive;

            return new APEntity
            {
                Id = id,
                Data = @object,
                Type = type
            };
        }

        [NotMapped]
        public ASObject Data
        {
            get => ASObject.Parse(SerializedData);

            set => SerializedData = value.Serialize().ToString(Newtonsoft.Json.Formatting.None);
        }
    }
}
