using System;
using System.Linq;
using System.Threading.Tasks;
using Kroeg.ActivityStreams;
using Kroeg.Server.Models;
using Kroeg.Server.Salmon;
using Kroeg.Server.Tools;

namespace Kroeg.Server.Services
{
    public class FakeEntityService
    {
        private readonly APContext _context;
        private readonly EntityData _configuration;

        public FakeEntityService(APContext context, EntityData configuration)
        {
            _context = context;
            _configuration = configuration;
        }

        public async Task<ASObject> BuildFakeObject(APEntity entity, string fragment)
        {
            if (!_configuration.IsActor(entity.Data)) return null;

            if (fragment == "key")
            {
                var key = await _context.GetKey(entity.Id);
                var salm = new MagicKey(key.PrivateKey);
                var pemData = salm.AsPEM;

                var keyObj = new ASObject();
                keyObj.Replace("owner", new ASTerm(entity.Id));
                keyObj.Replace("publicKeyPem", new ASTerm(pemData));
                keyObj.Replace("id", new ASTerm($"{entity.Id}#key"));
                return keyObj;
            }
            else if (fragment == "endpoints")
            {
                var data = entity.Data;
                var idu = new Uri(entity.Id);

                var basePath = $"{idu.Scheme}://{idu.Host}{(idu.IsDefaultPort?"":$":{idu.Port}")}{_configuration.BasePath}";

                var endpoints = new ASObject();
                endpoints.Replace("oauthAuthorizationEndpoint", new ASTerm(basePath + "auth/oauth?id=" + Uri.EscapeDataString(entity.Id)));
                endpoints.Replace("oauthTokenEndpoint", new ASTerm(basePath + "auth/token?"));
                endpoints.Replace("settingsEndpoint", new ASTerm(basePath + "settings/auth"));
                endpoints.Replace("uploadMedia", new ASTerm((string)data["outbox"].Single().Primitive));
                endpoints.Replace("relevantObjects", new ASTerm(basePath + "settings/relevant"));
                endpoints.Replace("proxyUrl", new ASTerm(basePath + "auth/proxy"));
                endpoints.Replace("jwks", new ASTerm(basePath + "auth/jwks?id=" + Uri.EscapeDataString(entity.Id)));
                endpoints.Replace("id", new ASTerm(entity.Id + "#endpoints"));
                return endpoints;
            }

            return null;
        }
    }
}