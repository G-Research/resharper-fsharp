using System.Collections.Generic;
using JetBrains.ReSharper.Plugins.FSharp.Psi.Util;
using JetBrains.ReSharper.Psi;
using JetBrains.Util;
using static FSharp.Compiler.ExtensionTyping;

namespace JetBrains.ReSharper.Plugins.FSharp.Psi.Impl.Cache2
{
  public class FSharpProvidedMethod : FSharpProvidedMethodBase, IMethod
  {
    private readonly ProvidedMethodInfo myInfo;

    public FSharpProvidedMethod(ProvidedMethodInfo info, ITypeElement containingType) : base(info, containingType)
    {
      myInfo = info;
    }

    public override IType ReturnType => myInfo.ReturnType.MapType(Module);
    public IList<ITypeParameter> TypeParameters => EmptyList<ITypeParameter>.InstanceList;
    public bool IsExtensionMethod => false;
    public bool IsAsync => false;
    public bool IsVarArg => false;
    public override bool IsStatic => myInfo.IsStatic;
    public override bool IsAbstract => myInfo.IsAbstract;
    public override DeclaredElementType GetElementType() => CLRDeclaredElementType.METHOD;
  }
}
