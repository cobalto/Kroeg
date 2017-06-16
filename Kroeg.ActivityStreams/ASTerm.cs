namespace Kroeg.ActivityStreams
{
    public class ASTerm
    {
        public ASTerm() { }
        public ASTerm(string value) { Primitive = value; }
        public ASTerm(int value) { Primitive = value; }
        public ASTerm(double value) { Primitive = value; }
        public ASTerm(ASObject value) { SubObject = value; }

        public object Primitive { get; set; }
        public ASObject SubObject { get; set; }
        public string Language { get; set; }

        public ASTerm Clone()
        {
            if (SubObject == null)
                return new ASTerm { Primitive = Primitive, Language = Language };
            else
                return new ASTerm { SubObject = SubObject.Clone(), Language = Language };
        }
    }
}
