using System;
using System.Text;
using System.Xml.Linq;

namespace Kroeg.Server.Salmon
{
    public class MagicEnvelope
    {
        private string _data;
        private string _dataType;
        private string _alg;
        private string _encoding;
        private string _signature;

        private static XNamespace MagicEnvelopeNS = "http://salmon-protocol.org/ns/magic-env";
        private static XNamespace NoNamespace = "";

        private static byte[] _decodeBase64Url(string data)
        {
            return Convert.FromBase64String(data.Replace('-', '+').Replace('_', '/'));
        }

        private static string _encodeBase64Url(byte[] data)
        {
            return Convert.ToBase64String(data, Base64FormattingOptions.None).Replace('+', '-').Replace('/', '_');
        }

        public MagicEnvelope(XDocument doc)
        {
            var elem = doc.Root;
            _data = elem.Element(MagicEnvelopeNS + "data").Value.Replace(" ", "").Replace("\n", "").Replace("\r", "");
            _dataType = elem.Element(MagicEnvelopeNS + "data").Attribute(NoNamespace + "type").Value;
            _alg = elem.Element(MagicEnvelopeNS + "alg").Value;
            _encoding = elem.Element(MagicEnvelopeNS + "encoding").Value;
            _signature = elem.Element(MagicEnvelopeNS + "sig").Value;
        }

        public MagicEnvelope(string data, string type, MagicKey key)
        {
            _data = _encodeBase64Url(Encoding.UTF8.GetBytes(data));
            _dataType = type;
            _alg = "RSA-SHA256";
            _encoding = "base64url";
            _signature = _encodeBase64Url(key.Sign(key.BuildSignedData(_data, _dataType, _encoding, _alg)));
        }

        public XDocument Build()
        {
            var e = new XElement(MagicEnvelopeNS + "env",
                new XElement(MagicEnvelopeNS + "data",
                new XAttribute(NoNamespace + "type", _dataType), _data),
                new XElement(MagicEnvelopeNS + "alg", _alg),
                new XElement(MagicEnvelopeNS + "encoding", _encoding),
                new XElement(MagicEnvelopeNS + "sig", _signature));
            return new XDocument(e);
        }

        public string RawData => Encoding.UTF8.GetString(_decodeBase64Url(_data));

        public bool VerifySignatureAgainst(MagicKey key)
        {
            return key.Verify(_signature, key.BuildSignedData(_data, _dataType, _encoding, _alg));
        }
    }
}
