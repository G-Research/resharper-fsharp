using System;
using System.Collections.Generic;
using System.Linq;
using System.Xml;
using JetBrains.Annotations;
using JetBrains.Diagnostics;
using JetBrains.Metadata.Reader.API;
using JetBrains.Metadata.Reader.Impl;
using JetBrains.ReSharper.Plugins.FSharp.Psi.Impl.DeclaredElement.CompilerGenerated;
using JetBrains.ReSharper.Plugins.FSharp.Psi.Util;
using JetBrains.ReSharper.Plugins.FSharp.TypeProviders.Protocol.Models;
using JetBrains.ReSharper.Plugins.FSharp.TypeProviders.Protocol.Utils;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.ExtensionsAPI.Caches2;
using JetBrains.ReSharper.Psi.Modules;
using JetBrains.ReSharper.Psi.Resolve;
using JetBrains.ReSharper.Psi.Util;
using JetBrains.Util;
using static FSharp.Compiler.ExtensionTyping;

namespace JetBrains.ReSharper.Plugins.FSharp.Psi.Impl.Cache2
{
  public class FSharpProvidedTypeElement
  {
    private ProvidedType Type { get; }
    private IFSharpTypeElement TypeElement { get; }
    public XmlNode GetXmlDoc() => Type.GetXmlDoc(null); //TODO: should be not null

    public FSharpProvidedTypeElement(ProvidedType type, [NotNull] IFSharpTypeElement typeElement)
    {
      Type = type;
      TypeElement = typeElement;
    }

    public static IEnumerable<IOperator> Operators => EmptyList<IOperator>.InstanceList;

    public IEnumerable<IConstructor> Constructors => Type
      .GetConstructors()
      .Select(t => new FSharpProvidedConstructor(t, TypeElement));

    private IEnumerable<IMethod> FilterMethods(IEnumerable<IMethod> methodInfos)
    {
      var methodGroups = methodInfos.ToDictionary(t => t.XMLDocId);

      foreach (var property in Properties)
      {
        if (property.IsReadable && property.Getter is { } getter)
          methodGroups.Remove(XMLDocUtil.GetTypeMemberXmlDocId(getter, getter.ShortName));

        if (property.IsWritable && property.Setter is { } setter)
          methodGroups.Remove(XMLDocUtil.GetTypeMemberXmlDocId(setter, setter.ShortName));
      }

      foreach (var @event in Events)
      {
        if (@event.Adder is { } adder)
          methodGroups.Remove(XMLDocUtil.GetTypeMemberXmlDocId(adder, adder.ShortName));

        if (@event.Remover is { } remover)
          methodGroups.Remove(XMLDocUtil.GetTypeMemberXmlDocId(remover, remover.ShortName));

        if (@event.Raiser is { } raiser)
          methodGroups.Remove(XMLDocUtil.GetTypeMemberXmlDocId(raiser, raiser.ShortName));
      }

      return methodGroups.Values;
    }

    public IEnumerable<IMethod> Methods =>
      FilterMethods(Type.GetMethods().Select(t => new FSharpProvidedMethod(t, TypeElement)));

    public IEnumerable<IProperty> Properties =>
      Type
        .GetProperties()
        .Select(t => new FSharpProvidedProperty(t, TypeElement));

    public IEnumerable<IEvent> Events =>
      Type
        .GetEvents()
        .Select(t => new FSharpProvidedEvent(t, TypeElement));

    public IList<ITypeElement> NestedTypes =>
      Type.GetNestedTypes() //TODO: all?
        .Select(t => (ITypeElement)new FSharpProvidedNestedClass(t, TypeElement.Module, TypeElement))
        .ToList();

    public IEnumerable<ITypeMember> GetMembers() =>
      Methods.Cast<ITypeMember>()
        .Union(Properties)
        .Union(Events)
        .Union(Fields)
        .Union(Constructors)
        .Union(NestedTypes.Cast<ITypeMember>())
        .ToList();

    public IList<IDeclaredType> GetSuperTypes() => EmptyList<IDeclaredType>.Instance;

    public IList<ITypeElement> GetSuperTypeElements() => EmptyList<ITypeElement>.Instance;
    public static IEnumerable<IField> Constants => EmptyList<IField>.InstanceList;

    public IEnumerable<IField> Fields =>
      Type.GetFields()
        .Select(t => new FSharpProvidedField(t, TypeElement))
        .ToList();

    public MemberPresenceFlag GetMemberPresenceFlag() => MemberPresenceFlag.NONE;
    public IEnumerable<string> MemberNames => GetMembers().Select(t => t.ShortName);

    public IClass GetSuperClass()
    {
      var clrTypeName = new ClrTypeName(Type.BaseType.Assembly.FullName + "." + Type.BaseType.FullName);
      return clrTypeName.CreateTypeByClrName(TypeElement.Module).GetTypeElement() as IClass;
    }
    /*Type.BaseType == null
        ? null
        : new FSharpProvidedClass(Type.BaseType, this);*/

    //TODO: common & hashset
    public bool HasMemberWithName(string shortName, bool ignoreCase)
    {
      var comparisonRule = ignoreCase ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;

      foreach (var name in MemberNames)
        if (string.Equals(name, shortName, comparisonRule))
          return true;

      return false;
    }
  }

  public class FSharpProvidedAbbreviatedClass : Class, IFSharpTypeElement
  {
    private ProxyProvidedTypeWithContext Type { get; }
    private readonly FSharpProvidedTypeElement myTypeElement;

    public FSharpProvidedAbbreviatedClass(ProxyProvidedTypeWithContext type, [NotNull] IClassPart part) : base(part)
    {
      Type = type;
      myTypeElement = new FSharpProvidedTypeElement(type, this);
    }

    public override MemberPresenceFlag GetMemberPresenceFlag() => myTypeElement.GetMemberPresenceFlag();

    public string SourceName => Type.DisplayName;

    public override bool HasMemberWithName(string shortName, bool ignoreCase) =>
      myTypeElement.HasMemberWithName(shortName, ignoreCase);

    public override IEnumerable<IConstructor> Constructors => myTypeElement.Constructors;
    public override IEnumerable<IOperator> Operators => FSharpProvidedTypeElement.Operators;
    public override IEnumerable<IMethod> Methods => myTypeElement.Methods;
    public override IEnumerable<IProperty> Properties => myTypeElement.Properties;
    public override IEnumerable<IEvent> Events => myTypeElement.Events;
    public override IList<ITypeElement> NestedTypes => myTypeElement.NestedTypes;
    public override IEnumerable<ITypeMember> GetMembers() => myTypeElement.GetMembers();
    public override IEnumerable<string> MemberNames => myTypeElement.MemberNames;
    public override IEnumerable<IField> Constants => FSharpProvidedTypeElement.Constants;
    public override IEnumerable<IField> Fields => myTypeElement.Fields;
    public new XmlNode GetXMLDoc(bool inherit) => myTypeElement.GetXmlDoc();

    /*public override IClass GetSuperClass()
    {
      var clrTypeName = new ClrTypeName(Type.BaseType.Assembly.FullName + "." + Type.BaseType.FullName);
      return clrTypeName.CreateTypeByClrName(Module).GetTypeElement() as IClass;
    }*/
    /*Type.BaseType == null
        ? null
        : new FSharpProvidedClass(Type.BaseType, this);*/
  }

  public class FSharpProvidedNestedClass : FSharpGeneratedElementBase, IClass, IFSharpTypeElement,
    IFSharpTypeParametersOwner,
    ISecondaryDeclaredElement
  {
    public FSharpProvidedNestedClass(ProvidedType type, IPsiModule module, ITypeElement containingType = null)
    {
      Module = module;
      Type = type;
      myTypeElement = new FSharpProvidedTypeElement(type, this);
      ContainingType = containingType ??
                       (type.DeclaringType is { DeclaringType: { } }
                         ? new FSharpProvidedNestedClass(type.DeclaringType, module)
                         : (ITypeElement)OriginElement);
    }

    public ProvidedType Type { get; }
    private readonly FSharpProvidedTypeElement myTypeElement;
    public override ITypeElement GetContainingType() => ContainingType;
    public override ITypeMember GetContainingTypeMember() => ContainingType as ITypeMember;
    public override DeclaredElementType GetElementType() => CLRDeclaredElementType.CLASS;
    public override bool IsVisibleFromFSharp => true;

    public override string ShortName => Type.Name;
    public override bool IsValid() => OriginElement is { } x && x.IsValid();

    //get from type and cache
    public IClrTypeName GetClrName() => new ClrTypeName($"{Type.Namespace}.{Type.Name}"); //get from type?

    public IList<IDeclaredType> GetSuperTypes() => myTypeElement.GetSuperTypes();
    public IList<ITypeElement> GetSuperTypeElements() => myTypeElement.GetSuperTypeElements();
    public MemberPresenceFlag GetMemberPresenceFlag() => myTypeElement.GetMemberPresenceFlag();

    public INamespace GetContainingNamespace() =>
      ContainingType.GetContainingNamespace(); //get abbreviated type and then hist namespace


    public IPsiSourceFile GetSingleOrDefaultSourceFile() => null;

    public bool HasMemberWithName(string shortName, bool ignoreCase) =>
      myTypeElement.HasMemberWithName(shortName, ignoreCase);

    public IEnumerable<ITypeMember> GetMembers() => myTypeElement.GetMembers();
    public IEnumerable<IConstructor> Constructors => myTypeElement.Constructors;
    public IEnumerable<IOperator> Operators => FSharpProvidedTypeElement.Operators;
    public IEnumerable<IMethod> Methods => myTypeElement.Methods;
    public IEnumerable<IProperty> Properties => myTypeElement.Properties;
    public IEnumerable<IEvent> Events => myTypeElement.Events;
    public IEnumerable<string> MemberNames => myTypeElement.MemberNames;
    public IEnumerable<IField> Constants => FSharpProvidedTypeElement.Constants;
    public IEnumerable<IField> Fields => myTypeElement.Fields;
    public IList<ITypeElement> NestedTypes => myTypeElement.NestedTypes;

    public IClrDeclaredElement OriginElement
    {
      get
      {
        var declaringType = Type.DeclaringType;
        while (declaringType.DeclaringType != null) declaringType = declaringType.DeclaringType;

        var generatedType = (declaringType as IProxyProvidedType).NotNull();
        return generatedType.GetClrName().CreateTypeByClrName(Module).GetTypeElement();
      }
    }

    public bool IsReadOnly => true;

    protected override IClrDeclaredElement ContainingElement => ContainingType;
    public IList<ITypeParameter> AllTypeParameters => TypeParameters;
    public IList<ITypeParameter> TypeParameters => EmptyList<ITypeParameter>.Instance;

    public AccessRights GetAccessRights() =>
      Type.IsPublic || Type.IsNestedPublic ? AccessRights.PUBLIC : AccessRights.PRIVATE;

    public bool IsAbstract => Type.IsAbstract;
    public bool IsSealed => Type.IsSealed;
    public bool IsVirtual => false;
    public bool IsOverride => false;
    public bool IsStatic => true;
    public bool IsReadonly => false;
    public bool IsExtern => false;
    public bool IsUnsafe => false;
    public bool IsVolatile => false;
    public string XMLDocId => XMLDocUtil.GetTypeElementXmlDocId(this);
    public IList<TypeMemberInstance> GetHiddenMembers() => EmptyList<TypeMemberInstance>.Instance;
    public Hash? CalcHash() => null;
    public ITypeElement ContainingType { get; }

    //TODO
    public AccessibilityDomain AccessibilityDomain => new(AccessibilityDomain.AccessibilityDomainType.PUBLIC, null);

    public MemberHidePolicy HidePolicy => MemberHidePolicy.HIDE_BY_NAME;

    public IDeclaredType GetBaseClassType() => //
      Module.GetPredefinedType().Object; //TypeFactory.CreateType(new FSharpProvidedClass(myType.BaseType));

    public IClass GetSuperClass() => null;
    public override ISubstitution IdSubstitution => EmptySubstitution.INSTANCE;
    public override IPsiModule Module { get; }
    public override IPsiServices GetPsiServices() => Module.GetPsiServices();
    public new XmlNode GetXMLDoc(bool inherit) => myTypeElement.GetXmlDoc();

    public override bool Equals(object obj) =>
      obj switch
      {
        FSharpProvidedNestedClass x => ProvidedTypesComparer.Instance.Equals(x.Type, Type), //TODO: compare entity ids
        _ => false
      };

    public override int GetHashCode() => ProvidedTypesComparer.Instance.GetHashCode(Type);
  }
}
