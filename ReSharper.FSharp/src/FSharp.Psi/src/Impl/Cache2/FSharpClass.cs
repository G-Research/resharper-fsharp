using System;
using System.Collections.Generic;
using System.Xml;
using JetBrains.Annotations;
using JetBrains.ReSharper.Plugins.FSharp.Psi.Impl.Cache2.Parts;
using JetBrains.ReSharper.Plugins.FSharp.TypeProviders.Protocol.Cache;
using JetBrains.ReSharper.Psi;
using JetBrains.ReSharper.Psi.ExtensionsAPI.Caches2;
using JetBrains.Util;

namespace JetBrains.ReSharper.Plugins.FSharp.Psi.Impl.Cache2
{
  public class FSharpClass : Class, IFSharpTypeElement, IFSharpTypeParametersOwner
  {
    public int MeasureTypeParametersCount { get; }

    public FSharpClass([NotNull] IClassPart part) : base(part)
    {
      if (part is IFSharpClassPart fsClassPart)
        MeasureTypeParametersCount = fsClassPart.MeasureTypeParametersCount;
    }

    protected override bool AcceptsPart(TypePart part) =>
      part.ShortName == ShortName &&
      part is IFSharpClassPart classPart && classPart.MeasureTypeParametersCount == MeasureTypeParametersCount;

    protected override MemberDecoration Modifiers => myParts.GetModifiers();
    public virtual string SourceName => this.GetSourceName();

    public override IClass GetSuperClass()
    {
      foreach (var part in EnumerateParts())
        if (part is IFSharpClassPart fsPart && fsPart.GetSuperClass() is { } super)
          return super;
      return null;
    }

    public override IList<ITypeElement> GetSuperTypeElements()
    {
      var result = new HashSet<ITypeElement>();
      foreach (var part in EnumerateParts())
        if (part is IFSharpClassLikePart fsPart)
          result.AddRange(fsPart.GetSuperTypeElements());
      return result.ToArray();
    }

    public virtual IList<ITypeParameter> AllTypeParameters =>
      this.GetAllTypeParametersReversed();
  }

  public class FSharpClassOrProvidedTypeAbbreviation : FSharpClass
  {
    private readonly Lazy<FSharpProvidedTypeElement> myProvidedClass;
    private bool IsProvided => myProvidedClass.Value != null;
    private FSharpProvidedTypeElement ProvidedClass => myProvidedClass.Value;

    public FSharpClassOrProvidedTypeAbbreviation([NotNull] IClassPart part) : base(part)
    {
      myProvidedClass = new(() =>
        part is TypeAbbreviationOrDeclarationPart { IsProvidedAndGenerated: true } &&
        TypeProvidersContext.ProvidedAbbreviations.TryGetValue(GetClrName().FullName, out var type)
          ? new FSharpProvidedTypeElement(type, this)
          : null);
    }

    protected override MemberDecoration Modifiers => myParts.GetModifiers();

    public override IClass GetSuperClass() => IsProvided ? ProvidedClass.GetSuperClass() : base.GetSuperClass();

    public override IList<ITypeElement> GetSuperTypeElements() =>
      IsProvided ? ProvidedClass.GetSuperTypeElements() : base.GetSuperTypeElements();

    public override IEnumerable<ITypeMember> GetMembers() =>
      IsProvided ? ProvidedClass.GetMembers() : base.GetMembers();

    public override IList<ITypeElement> NestedTypes => IsProvided ? ProvidedClass.NestedTypes : base.NestedTypes;

    public override IList<ITypeParameter> AllTypeParameters =>
      IsProvided ? ProvidedClass.AllTypeParameters : base.AllTypeParameters;

    public override bool HasMemberWithName(string shortName, bool ignoreCase) =>
      IsProvided
        ? ProvidedClass.HasMemberWithName(shortName, ignoreCase)
        : base.HasMemberWithName(shortName, ignoreCase);

    public override IEnumerable<IConstructor> Constructors =>
      IsProvided ? ProvidedClass.Constructors : base.Constructors;

    public override IEnumerable<IOperator> Operators => IsProvided ? ProvidedClass.Operators : base.Operators;
    public override IEnumerable<IMethod> Methods => IsProvided ? ProvidedClass.Methods : base.Methods;
    public override IEnumerable<IProperty> Properties => IsProvided ? ProvidedClass.Properties : base.Properties;
    public override IEnumerable<IEvent> Events => IsProvided ? ProvidedClass.Events : base.Events;
    public override IEnumerable<string> MemberNames => IsProvided ? ProvidedClass.MemberNames : base.MemberNames;
    public override IEnumerable<IField> Constants => IsProvided ? ProvidedClass.Constants : base.Constants;
    public override IEnumerable<IField> Fields => IsProvided ? ProvidedClass.Fields : base.Fields;

    public new XmlNode GetXMLDoc(bool inherit) =>
      IsProvided ? ProvidedClass.GetXmlDoc() : base.GetXMLDoc(inherit);
  }
}
