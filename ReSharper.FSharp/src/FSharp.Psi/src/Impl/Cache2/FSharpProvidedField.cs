using JetBrains.ReSharper.Plugins.FSharp.Psi.Util;
using JetBrains.ReSharper.Psi;
using static FSharp.Compiler.ExtensionTyping;

namespace JetBrains.ReSharper.Plugins.FSharp.Psi.Impl.Cache2
{
  public class FSharpProvidedField : FSharpProvidedMember, IField
  {
    private readonly ProvidedFieldInfo myInfo;

    public FSharpProvidedField(ProvidedFieldInfo info, ITypeElement containingType) : base(info, containingType)
    {
      myInfo = info;
    }

    public override DeclaredElementType GetElementType() => CLRDeclaredElementType.FIELD;

    public IType Type => myInfo.FieldType.MapType(Module);

    public ConstantValue ConstantValue =>
      IsConstant ? new ConstantValue(myInfo.GetRawConstantValue(), type: null) : ConstantValue.BAD_VALUE;

    public bool IsField => true;
    public bool IsConstant => myInfo.IsLiteral;
    public bool IsEnumMember => false;
    public int? FixedBufferSize => null;
  }
}
