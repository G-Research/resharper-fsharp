using System.Collections.Generic;
using System.Xml;
using JetBrains.Annotations;
using JetBrains.Diagnostics;
using JetBrains.ReSharper.Plugins.FSharp.Psi.Impl.DeclaredElement.CompilerGenerated;
using JetBrains.ReSharper.Plugins.FSharp.Psi.Util;
using JetBrains.ReSharper.Plugins.FSharp.TypeProviders.Protocol.Models;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.Modules;
using JetBrains.ReSharper.Psi.Resolve;
using JetBrains.ReSharper.Psi.Util;
using JetBrains.Util;
using static FSharp.Compiler.ExtensionTyping;

namespace JetBrains.ReSharper.Plugins.FSharp.Psi.Impl.Cache2
{
  public abstract class FSharpProvidedMember : FSharpGeneratedElementBase, IOverridableMember,
    ISecondaryDeclaredElement,
    IFSharpTypeParametersOwner
  {
    private readonly ProvidedMemberInfo myInfo;

    protected FSharpProvidedMember(ProvidedMemberInfo info, ITypeElement containingType)
    {
      myInfo = info;
      Module = containingType.Module;
      ContainingType = containingType;
    }

    protected FSharpProvidedMember(ProvidedMemberInfo info, IPsiModule module)
    {
      myInfo = info;
      Module = module;
      ContainingType = null;
    }

    //TODO: remove new modifier
    public new XmlNode GetXMLDoc(bool inherit) => myInfo.GetXmlDoc(this);

    public AccessRights GetAccessRights() => AccessRights.PUBLIC;

    public override IPsiModule Module { get; }

    public virtual bool IsAbstract => false;
    public virtual bool IsSealed => false;
    public virtual bool IsVirtual => false;
    public virtual bool IsOverride => false;
    public virtual bool IsStatic => false;
    public virtual bool IsReadonly => false;
    public virtual bool IsExtern => false;
    public virtual bool IsUnsafe => false;
    public virtual bool IsVolatile => false;
    public string XMLDocId => XMLDocUtil.GetTypeMemberXmlDocId(this, ShortName); //"M:Test.Union.NewB(System.Int32)";

    public IList<TypeMemberInstance> GetHiddenMembers() => EmptyList<TypeMemberInstance>.Instance;

    public Hash? CalcHash() => null;

    [NotNull] public ITypeElement ContainingType { get; }

    public AccessibilityDomain AccessibilityDomain => new(AccessibilityDomain.AccessibilityDomainType.PUBLIC, null);

    public MemberHidePolicy HidePolicy => MemberHidePolicy.HIDE_BY_NAME;
    public bool IsExplicitImplementation => false;
    public IList<IExplicitImplementation> ExplicitImplementations => EmptyList<IExplicitImplementation>.Instance;
    public bool CanBeImplicitImplementation => false;

    public IList<ITypeParameter> AllTypeParameters => EmptyList<ITypeParameter>.Instance;

    public override ITypeElement GetContainingType() => ContainingType;

    public override ITypeMember GetContainingTypeMember() => ContainingType as ITypeMember;
    public override string ShortName => myInfo.Name;
    protected override IClrDeclaredElement ContainingElement => ContainingType;
    public override ISubstitution IdSubstitution => EmptySubstitution.INSTANCE;

    public IClrDeclaredElement OriginElement
    {
      get
      {
        var declaringType = myInfo.DeclaringType;
        while (declaringType.DeclaringType != null) declaringType = declaringType.DeclaringType;

        var generatedType = (declaringType as IProxyProvidedType).NotNull();
        return generatedType.GetClrName().CreateTypeByClrName(Module).GetTypeElement();
      }
    }

    public bool IsReadOnly => true;
  }
}
