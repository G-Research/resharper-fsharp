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
    //TODO: add comment
    private FSharpProvidedTypeElement ProvidedClass =>
      myParts is TypeAbbreviationOrDeclarationPart { IsProvidedAndGenerated: true } &&
      TypeProvidersContext.ProvidedAbbreviations.TryGetValue(GetClrName().FullName, out var type)
        ? new FSharpProvidedTypeElement(type, this)
        : null;

    public FSharpClassOrProvidedTypeAbbreviation([NotNull] IClassPart part) : base(part)
    {
      /*myProvidedClass = new(() =>
        part is TypeAbbreviationOrDeclarationPart { IsProvidedAndGenerated: true } &&
        TypeProvidersContext.ProvidedAbbreviations.TryGetValue(GetClrName().FullName, out var type)
          ? new FSharpProvidedTypeElement(type, this)
          : null);*/
    }

    protected override MemberDecoration Modifiers => myParts.GetModifiers();

    public override IClass GetSuperClass() => ProvidedClass is { } x ? x.GetSuperClass() : base.GetSuperClass();

    public override IList<ITypeElement> GetSuperTypeElements() =>
      ProvidedClass is { } x ? x.GetSuperTypeElements() : base.GetSuperTypeElements();

    public override IEnumerable<ITypeMember> GetMembers() =>
      ProvidedClass is { } x ? x.GetMembers() : base.GetMembers();

    public override IList<ITypeElement> NestedTypes => ProvidedClass is { } x ? x.NestedTypes : base.NestedTypes;

    public override IList<ITypeParameter> AllTypeParameters =>
      ProvidedClass is { } x ? x.AllTypeParameters : base.AllTypeParameters;

    public override bool HasMemberWithName(string shortName, bool ignoreCase) =>
      ProvidedClass is { } x
        ? x.HasMemberWithName(shortName, ignoreCase)
        : base.HasMemberWithName(shortName, ignoreCase);

    public override IEnumerable<IConstructor> Constructors =>
      ProvidedClass is {} x ? x.Constructors : base.Constructors;

    public override IEnumerable<IOperator> Operators => ProvidedClass is {} x ? x.Operators : base.Operators;
    public override IEnumerable<IMethod> Methods => ProvidedClass is {} x ? x.Methods : base.Methods;
    public override IEnumerable<IProperty> Properties => ProvidedClass is {} x ? x.Properties : base.Properties;
    public override IEnumerable<IEvent> Events => ProvidedClass is {} x ? x.Events : base.Events;
    public override IEnumerable<string> MemberNames => ProvidedClass is {} x ? x.MemberNames : base.MemberNames;
    public override IEnumerable<IField> Constants => ProvidedClass is {} x ? x.Constants : base.Constants;
    public override IEnumerable<IField> Fields => ProvidedClass is {} x ? x.Fields : base.Fields;

    public new XmlNode GetXMLDoc(bool inherit) =>
      ProvidedClass is {} x ? x.GetXmlDoc() : base.GetXMLDoc(inherit);
  }
}
