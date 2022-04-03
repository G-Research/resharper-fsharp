using System.Collections.Generic;
using JetBrains.ReSharper.Plugins.FSharp.Psi.Util;
using JetBrains.ReSharper.Psi;
using JetBrains.Util;
using static FSharp.Compiler.ExtensionTyping;

namespace JetBrains.ReSharper.Plugins.FSharp.Psi.Impl.Cache2
{
  public class FSharpProvidedMethod : FSharpProvidedMethodBase<ProvidedMethodInfo>, IMethod
  {
    public FSharpProvidedMethod(ProvidedMethodInfo info, ITypeElement containingType) : base(info, containingType)
    {
    }

    public override IType ReturnType => Info.ReturnType.MapType(Module);
    public IList<ITypeParameter> TypeParameters => EmptyList<ITypeParameter>.InstanceList;
    public bool IsExtensionMethod => false;
    public bool IsAsync => false;
    public bool IsVarArg => false;
    public override bool IsStatic => Info.IsStatic;
    public override bool IsAbstract => Info.IsAbstract;
    public override DeclaredElementType GetElementType() => CLRDeclaredElementType.METHOD;
  }
}
