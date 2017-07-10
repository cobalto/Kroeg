using System;
using System.Collections.Generic;
using System.Linq;
using System.Threading.Tasks;
using Kroeg.ActivityStreams;
using Kroeg.Server.BackgroundTasks;
using Kroeg.Server.Models;
using Kroeg.Server.Services.EntityStore;
using Kroeg.Server.Tools;
using Microsoft.EntityFrameworkCore;
using Newtonsoft.Json;
using Microsoft.IdentityModel.Tokens;
using System.Net.Http;
using System.Security.Cryptography;
using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;
using Kroeg.Server.Configuration;

namespace Kroeg.Server.Services
{
    public class DeliveryService
    {
        private readonly APContext _context;
        private readonly EntityData _configuration;
        private readonly CollectionTools _collectionTools;
        private readonly IEntityStore _store;
        private readonly JwtTokenSettings _tokenSettings;

        public DeliveryService(APContext context, CollectionTools collectionTools, EntityData configuration, IEntityStore store)
        {
            _context = context;
            _collectionTools = collectionTools;
            _configuration = configuration;
            _store = store;
        }

        public static bool IsPublic(ASObject @object)
        {
            var targetIds = new List<string>();

            targetIds.AddRange(@object["to"].Select(a => (string)a.Primitive));
            targetIds.AddRange(@object["bto"].Select(a => (string)a.Primitive));
            targetIds.AddRange(@object["cc"].Select(a => (string)a.Primitive));
            targetIds.AddRange(@object["bcc"].Select(a => (string)a.Primitive));
            targetIds.AddRange(@object["audience"].Select(a => (string)a.Primitive));

            return targetIds.Contains("https://www.w3.org/ns/activitystreams#Public");
        }

        public async Task QueueDeliveryForEntity(APEntity entity, int collectionId, bool forwardOnly = false)
        {
            var audienceInbox = await _buildAudienceInbox(entity.Data, forward: forwardOnly, actor: false);
            // Is public post?
            if (audienceInbox.Item2 && !forwardOnly)
            {
                await _queueWebsubDelivery((string)entity.Data["actor"].First().Primitive, collectionId, entity.Id);
            }

            foreach (var target in audienceInbox.Item1)
                _queueInboxDelivery(target, entity);

            foreach (var salmon in audienceInbox.Item3)
                _queueSalmonDelivery(salmon, entity);

            await _context.SaveChangesAsync();
        }

        private async Task<JsonWebKeySet> _getKey(string url)
        {
            var hc = new HttpClient();
            return JsonConvert.DeserializeObject<JsonWebKeySet>(await hc.GetStringAsync(url));
        }

        public async Task<JWKEntry> GetKey(APEntity actor, string kid = null)
        {
            if (!actor.IsOwner)
            {
                if (kid == null) return null; // can't do that for remote actors

                var key = await _context.JsonWebKeys.FirstOrDefaultAsync(a => a.Owner == actor && a.Id == kid);
                if (key == null)
                {
                    // well here we go

                    var endpoints = actor.Data["endpoints"].FirstOrDefault();
                    if (endpoints == null) return null;
                    ASObject endpointsData;
                    if (endpoints.Primitive != null)
                        endpointsData = (await _store.GetEntity((string)endpoints.Primitive, true)).Data;
                    else
                        endpointsData = endpoints.SubObject;

                    var jwks = (string) endpointsData["jwks"].FirstOrDefault()?.Primitive; // not actually an entity!
                    if (jwks == null) return null;

                    var keys = await _getKey(jwks);
                    var jwkey = keys.Keys.FirstOrDefault(a => a.Kid == kid);

                    if (jwkey == null) return null; // couldn't find key

                    key = new JWKEntry
                    {
                        Owner = actor,
                        Id = kid,
                        SerializedData = JsonConvert.SerializeObject(jwkey)
                    };

                    _context.JsonWebKeys.Add(key);
                    await _context.SaveChangesAsync();
                }

                return key;
            }
            else
            {
                var key = await _context.JsonWebKeys.FirstOrDefaultAsync(a => a.Owner == actor);
                if (key == null)
                {
                    var jwk = new JsonWebKey();
                    jwk.KeyOps.Add("sign");
                    jwk.Kty = "EC";
                    jwk.Crv = "P-256";
                    jwk.Use = JsonWebKeyUseNames.Sig;
                    jwk.Kid = Guid.NewGuid().ToString().Substring(0, 8);

                    var ec = ECDsa.Create(ECCurve.NamedCurves.nistP256);
                    var parms = ec.ExportParameters(true);
                    jwk.X = Base64UrlEncoder.Encode(parms.Q.X);
                    jwk.Y = Base64UrlEncoder.Encode(parms.Q.Y);
                    jwk.D = Base64UrlEncoder.Encode(parms.D);

                    key = new JWKEntry { Id = jwk.Kid, Owner = actor, SerializedData = JsonConvert.SerializeObject(jwk) };
                    _context.JsonWebKeys.Add(key);
                    await _context.SaveChangesAsync();
                }

                return key;
            }
        }

        private class _cryptoProviderFactory : CryptoProviderFactory
        {
            private class _signatureProvider : SignatureProvider
            {
                private JsonWebKey _key;
                private ECDsa _ecdsa;

                private static Dictionary<string, ECCurve> _curves = new Dictionary<string, ECCurve>
                {
                    [JsonWebKeyECTypes.P256] = ECCurve.NamedCurves.nistP256,
                    [JsonWebKeyECTypes.P384] = ECCurve.NamedCurves.nistP384,
                    [JsonWebKeyECTypes.P521] = ECCurve.NamedCurves.nistP521,
                };

                private static Dictionary<string, HashAlgorithmName> _hashes = new Dictionary<string, HashAlgorithmName>
                {
                    [SecurityAlgorithms.EcdsaSha256] = HashAlgorithmName.SHA256,
                    [SecurityAlgorithms.EcdsaSha256Signature] = HashAlgorithmName.SHA256,
                    [SecurityAlgorithms.EcdsaSha384] = HashAlgorithmName.SHA384,
                    [SecurityAlgorithms.EcdsaSha384Signature] = HashAlgorithmName.SHA384,
                    [SecurityAlgorithms.EcdsaSha512] = HashAlgorithmName.SHA512,
                    [SecurityAlgorithms.EcdsaSha512Signature] = HashAlgorithmName.SHA512
                };

                public _signatureProvider(SecurityKey key, string algorithm) : base(key, algorithm)
                {
                    _key = key as JsonWebKey;
                    if (_key.Kty == JsonWebAlgorithmsKeyTypes.EllipticCurve)
                    {
                        var ecpa = new ECParameters
                        {
                            Curve = _curves[_key.Crv],
                            D = _key.D != null ? Base64UrlEncoder.DecodeBytes(_key.D) : null,
                            Q = new ECPoint
                            {
                                X = Base64UrlEncoder.DecodeBytes(_key.X),
                                Y = Base64UrlEncoder.DecodeBytes(_key.Y)
                            }
                        };

                        _ecdsa = ECDsa.Create(ecpa);
                    }
                    else
                    {
                        throw new InvalidOperationException("Algorithm not yet supported");
                    }
                }

                public override byte[] Sign(byte[] input)
                {
                    return _ecdsa.SignData(input, _hashes[Algorithm]);
                }

                public override bool Verify(byte[] input, byte[] signature)
                {
                    return _ecdsa.VerifyData(input, signature, _hashes[Algorithm]);
                }

                protected override void Dispose(bool disposing)
                {
                    _ecdsa.Dispose();
                }
            }

            public override SignatureProvider CreateForSigning(SecurityKey key, string algorithm)
            {
                return new _signatureProvider(key, algorithm);
            }

            public override SignatureProvider CreateForVerifying(SecurityKey key, string algorithm)
            {
                return new _signatureProvider(key, algorithm);
            }
        }

        public async Task<string> BuildFederatedJWS(APEntity subject, string inbox)
        {
            var key = await GetKey(subject);

            var subUri = new Uri(inbox);
            var dom = $"{subUri.Scheme}://{subUri.Host}";

            var claims = new Claim[]
            {
                new Claim(JwtRegisteredClaimNames.Sub, subject.Id)
            };

            var signingCreds = new SigningCredentials(key.Key, SecurityAlgorithms.EcdsaSha256);
            signingCreds.Key.KeyId = key.Id;
            signingCreds.CryptoProviderFactory = new _cryptoProviderFactory();

            var jwt = new JwtSecurityToken(
                audience: dom,
                claims: claims,
                notBefore: DateTime.UtcNow,
                expires: DateTime.UtcNow.Add(TimeSpan.FromMinutes(10)),
                signingCredentials: signingCreds
                );

            return new JwtSecurityTokenHandler().WriteToken(jwt);
        }

        public async Task<string> VerifyJWS(string inbox, string serialized)
        {
            var handler = new JwtSecurityTokenHandler();
            var jwt = new JwtSecurityTokenHandler().ReadJwtToken(serialized);

            var subUri = new Uri(inbox);
            var dom = $"{subUri.Scheme}://{subUri.Host}";

            if (jwt.Header.Kid == null) return null;
            if (!jwt.Audiences.Contains(dom)) return null;

            var originId = jwt.Subject;
            var originEntity = await _store.GetEntity(originId, true);
            var verifyKey = await GetKey(originEntity, jwt.Header.Kid);
            if (verifyKey == null) return null;

            var tokenValidation = new TokenValidationParameters()
            {
                IssuerSigningKey = verifyKey.Key,
                RequireSignedTokens = true,
                ValidateAudience = false,
                ValidateIssuer = false,
                CryptoProviderFactory = new _cryptoProviderFactory()
            };

            return handler.ValidateToken(serialized, tokenValidation, out var ser) != null ? originId : null;
        }

        public static HashSet<string> GetAudienceIds(ASObject @object)
        {
            var targetIds = new List<string>();

            targetIds.AddRange(@object["to"].Select(a => (string)a.Primitive));
            targetIds.AddRange(@object["bto"].Select(a => (string)a.Primitive));
            targetIds.AddRange(@object["cc"].Select(a => (string)a.Primitive));
            targetIds.AddRange(@object["bcc"].Select(a => (string)a.Primitive));
            targetIds.AddRange(@object["audience"].Select(a => (string)a.Primitive));
            targetIds.AddRange(@object["attributedTo"].Select(a => (string)a.Primitive));
            targetIds.AddRange(@object["actor"].Select(a => (string)a.Primitive));

            return new HashSet<string>(targetIds);
        }

        private async Task<Tuple<HashSet<string>, bool, HashSet<string>>> _buildAudienceInbox(ASObject @object, int depth = 3, bool forward = false, bool actor = true)
        {
            var targetIds = new List<string>();

            targetIds.AddRange(@object["to"].Select(a => (string)a.Primitive));
            targetIds.AddRange(@object["bto"].Select(a => (string)a.Primitive));
            targetIds.AddRange(@object["cc"].Select(a => (string)a.Primitive));
            targetIds.AddRange(@object["bcc"].Select(a => (string)a.Primitive));
            targetIds.AddRange(@object["audience"].Select(a => (string)a.Primitive));

            if (!actor) targetIds.Remove((string)@object["actor"].First().Primitive);

            bool isPublic = targetIds.Contains("https://www.w3.org/ns/activitystreams#Public");
            targetIds.Remove("https://www.w3.org/ns/activitystreams#Public");

            var targets = new HashSet<string>();
            var stack = new Stack<Tuple<int, APEntity>>();
            var salmons = new HashSet<string>();
            foreach (var item in targetIds)
            {
                var entity = await _store.GetEntity(item, true);
                var data = entity.Data;
                // if it's local collectionTools, or we don't need the forwarding thing
                if (!forward || ((data["type"].Contains(new ASTerm("CollectionTools")) || data["type"].Contains(new ASTerm("OrderedCollection"))) && entity.IsOwner))
                    stack.Push(new Tuple<int, APEntity>(0, entity));
            }

            while (stack.Any())
            {
                var entity = stack.Pop();

                var data = entity.Item2.Data;
                if ((data["type"].Contains(new ASTerm("CollectionTools")) || data["type"].Contains(new ASTerm("OrderedCollection"))) && entity.Item2.IsOwner && entity.Item1 < depth)
                {
                    foreach (var item in await _collectionTools.GetAll(entity.Item2.Id))
                        stack.Push(new Tuple<int, APEntity>(entity.Item1 + 1, item));
                }
                else if (_configuration.IsActor(data))
                {
                    if (data["inbox"].Any())
                        targets.Add((string)data["inbox"].First().Primitive);
                    else if (data["_:salmonUrl"].Any())
                        salmons.Add((string)data["_:salmonUrl"].First().Primitive);
                }
            }

            return new Tuple<HashSet<string>, bool, HashSet<string>>(targets, isPublic, salmons);
        }

        private void _queueInboxDelivery(string targetUrl, APEntity entity)
        {
            _context.EventQueue.Add(
                DeliverToActivityPubTask.Make(new DeliverToActivityPubData
                {
                    ObjectId = entity.Id,
                    TargetInbox = targetUrl
                }));
        }

        private void _queueSalmonDelivery(string targetUrl, APEntity entity)
        {
            _context.EventQueue.Add(
                DeliverToSalmonTask.Make(new DeliverToSalmonData
                {
                    EntityId = entity.Id,
                    SalmonUrl = targetUrl
                }));
        }

        private async Task _queueWebsubDelivery(string userId, int collectionItem, string objectId)
        {
            foreach (var sub in await _context.WebsubSubscriptions.Where(a => a.UserId == userId && a.Expiry > DateTime.Now).ToListAsync())
            {
                _context.EventQueue.Add(
                    DeliverToWebSubTask.Make(new DeliverToWebSubData
                    {
                        CollectionItem = collectionItem,
                        ObjectId = objectId,
                        SourceUserId = userId,
                        Subscription = sub.Id
                    }));
            }
        }
    }
}
