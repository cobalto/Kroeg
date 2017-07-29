using Kroeg.ActivityStreams;
using Kroeg.Server.Models;
using Kroeg.Server.Services.EntityStore;
using Kroeg.Server.Tools;
using Microsoft.AspNetCore.Http;
using Microsoft.EntityFrameworkCore;
using Microsoft.IdentityModel.Tokens;
using Newtonsoft.Json;
using System;
using System.Collections.Generic;
using System.IdentityModel.Tokens.Jwt;
using System.Linq;
using System.Net.Http;
using System.Security.Claims;
using System.Security.Cryptography;
using System.Text;
using System.Threading.Tasks;

namespace Kroeg.Server.Services
{
    public class SignatureVerifier
    {
        private readonly IEntityStore _entityStore;
        private readonly APContext _context;

        public SignatureVerifier(IEntityStore entityStore, APContext context)
        {
            _entityStore = entityStore;
            _context = context;
        }

        public async Task<Tuple<bool, string>> VerifyHttpSignature(HttpContext context)
        {
            var signatureHeader = context.Request.Headers["Signature"].FirstOrDefault();
            if (signatureHeader == null) return new Tuple<bool, string>(true, null);
            var parameters = signatureHeader.Replace("\\\"", "\n").Split(',').Select((a) => a.Split(new[] { '=' }, 2)).ToDictionary((a) => a[0], (a) => a[1].Trim('"').Replace("\n", "\\\""));

            if (!parameters.ContainsKey("keyId") || !parameters.ContainsKey("algorithm") || !parameters.ContainsKey("signature")) return new Tuple<bool, string>(false, null);
            if (!parameters.ContainsKey("headers")) parameters["headers"] = "date";
            var key = await _entityStore.GetEntity(parameters["keyId"], true);
            if (key == null) return new Tuple<bool, string>(false, null);

            var owner = await _entityStore.GetEntity((string)key.Data["owner"].First().Primitive, true);
            if (!owner.Data["publicKey"].Any((a) => (string)a.Primitive == key.Id)) return new Tuple<bool, string>(false, null);

            var stringKey = (string)key.Data["publicKeyPem"].First().Primitive;
            if (!stringKey.StartsWith("-----BEGIN PUBLIC KEY-----")) return new Tuple<bool, string>(false, null);

            var toDecode = stringKey.Remove(0, stringKey.IndexOf('\n'));
            toDecode = toDecode.Remove(toDecode.LastIndexOf('\n')).Replace("\n", "");

            var signKey = ASN1.ToRSA(Convert.FromBase64String(toDecode));

            var toSign = new StringBuilder();
            foreach (var headerKey in parameters["headers"].Split(' '))
            {
                if (headerKey == "(request-target)") toSign.Append($"(request-target): {context.Request.Method.ToLower()} {context.Request.Path}{context.Request.QueryString}\n");
                else toSign.Append($"{headerKey}: {string.Join(", ", context.Request.Headers[headerKey])}\n");
            }
            toSign.Remove(toSign.Length - 1, 1);

            var signature = Convert.FromBase64String(parameters["signature"]);

            switch (parameters["algorithm"])
            {
                case "rsa-sha256":
                    return new Tuple<bool, string>(signKey.VerifyData(Encoding.UTF8.GetBytes(toSign.ToString()), signature, HashAlgorithmName.SHA256, RSASignaturePadding.Pkcs1), owner.Id);
            }

            return new Tuple<bool, string>(false, owner.Id);
        }

        public async Task<string> Verify(string fullpath, HttpContext context)
        {
            if (context.Request.Headers["Signature"].Count > 0)
            {
                var httpSignature = await VerifyHttpSignature(context);
                if (httpSignature.Item1) return httpSignature.Item2;
            }

            if (context.Request.Headers["Authorization"].Count > 0)
            {
                var jws = context.Request.Headers["Authorization"][0].Split(new[] { ' ' }, 2)[1];
                var verified = await VerifyJWS(fullpath, jws);
                if (verified != null) return verified;
            }

            return null;
        }

        private async Task<JsonWebKeySet> _getKey(string url)
        {
            var hc = new HttpClient();
            return JsonConvert.DeserializeObject<JsonWebKeySet>(await hc.GetStringAsync(url));
        }

        public async Task<JWKEntry> GetJWK(APEntity actor, string kid = null)
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
                        endpointsData = (await _entityStore.GetEntity((string)endpoints.Primitive, true)).Data;
                    else
                        endpointsData = endpoints.SubObject;

                    var jwks = (string)endpointsData["jwks"].FirstOrDefault()?.Primitive; // not actually an entity!
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

        public async Task<string> BuildJWS(APEntity subject, string inbox)
        {
            var key = await GetJWK(subject);

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
            var originEntity = await _entityStore.GetEntity(originId, true);
            var verifyKey = await GetJWK(originEntity, jwt.Header.Kid);
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
    }
}
