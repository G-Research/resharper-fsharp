using System;
using System.Collections.Generic;
using System.Linq;
using System.Reflection;
using Fantomas;
using JetBrains.Diagnostics;
using JetBrains.Extension;
using JetBrains.ReSharper.Plugins.FSharp.Fantomas.Server;
using JetBrains.Util;
using Microsoft.FSharp.Collections;
using Microsoft.FSharp.Control;
using Microsoft.FSharp.Core;
using Microsoft.FSharp.Reflection;
using FormatConfig = Fantomas.FormatConfig.FormatConfig;
using FSharpType = Microsoft.FSharp.Reflection.FSharpType;

namespace JetBrains.ReSharper.Plugins.FSharp.Fantomas.Host
{
  // TODO: collect used Fantomas versions
  internal class FantomasCodeFormatter
  {
    private static dynamic GetFSharpChecker()
    {
      var searchedType = CurrentVersion < Version46
        ? "FSharp.Compiler.SourceCodeServices.FSharpChecker"
        : "FSharp.Compiler.CodeAnalysis.FSharpChecker";

      var qualifiedName = Assembly.CreateQualifiedName("FSharp.Compiler.Service", searchedType);
      var type = Type.GetType(qualifiedName).NotNull($"{qualifiedName} must exist");
      var method = type.GetMethod("Create").NotNull("FSharpChecker must contain static .Create method");

      var values = method
        .GetParameters()
        .Select(t => t.ParameterType.GetDefaultValue())
        .ToArray();
      return method.Invoke(null, values);
    }

    private static dynamic GetDiagnosticOptions()
    {
      var searchedType = CurrentVersion < Version45
        ? "FSharp.Compiler.ErrorLogger+FSharpErrorSeverityOptions"
        : "FSharp.Compiler.SourceCodeServices.FSharpDiagnosticOptions";

      var qualifiedName = Assembly.CreateQualifiedName("FSharp.Compiler.Service", searchedType);
      var options = Type.GetType(qualifiedName).NotNull($"{qualifiedName} must exist");
      return options.GetProperty("Default")?.GetValue(null).NotNull();
    }

    private static ConstructorInfo GetFSharpParsingOptionsConstructor()
    {
      var searchedType = CurrentVersion < Version46
        ? "FSharp.Compiler.SourceCodeServices.FSharpParsingOptions"
        : "FSharp.Compiler.CodeAnalysis.FSharpParsingOptions";

      var qualifiedName = Assembly.CreateQualifiedName("FSharp.Compiler.Service", searchedType);
      var options = Type.GetType(qualifiedName).NotNull($"{qualifiedName} must exist");
      return options.GetConstructors().Single();
    }

    private static readonly dynamic Checker = GetFSharpChecker();
    private static readonly dynamic DefaultDiagnosticOptions = GetDiagnosticOptions();
    private static readonly FormatConfig DefaultFormatConfig = FormatConfig.Default;
    private static readonly Type FormatConfigType = typeof(FormatConfig);
    private static readonly Version Version45 = Version.Parse("4.5");
    private static readonly Version Version46 = Version.Parse("4.6");
    private static readonly ConstructorInfo CreateFSharpParsingOptions = GetFSharpParsingOptionsConstructor();
    public static Version CurrentVersion { get; } = Version.Parse(CodeFormatter.GetVersion());

    public static readonly (string Name, object Value)[] FormatConfigFields =
      FSharpType.GetRecordFields(FormatConfigType, null)
        .Select(x => x.Name)
        .Zip(FSharpValue.GetRecordFields(DefaultFormatConfig, null), (name, value) => (name, value))
        .ToArray();

    private static readonly Dictionary<string, UnionCaseInfo> FormatConfigDUs =
      FormatConfigFields
        .Select(t => t.Value.GetType())
        .Where(t => FSharpType.IsUnion(t, null))
        .Distinct(t => t.FullName)
        .SelectMany(t => FSharpType.GetUnionCases(t, null))
        .ToDictionary(t => t.Name);

    public static string FormatSelection(RdFantomasFormatSelectionArgs args)
    {
      var rdRange = args.Range;

      var range =
        CodeFormatter.MakeRange(rdRange.FileName, rdRange.StartLine, rdRange.StartCol, rdRange.EndLine, rdRange.EndCol);
      return FSharpAsync.StartAsTask(
          CodeFormatter.FormatSelectionAsync(args.FileName, range,
            SourceOrigin.SourceOrigin.NewSourceString(args.Source), Convert(args.FormatConfig),
            CreateFSharpParsingOptions.Invoke(GetParsingOptions(args.ParsingOptions).ToArray()) as dynamic,
            Checker), null, null)
        .Result.Replace("\r\n", args.NewLineText);
    }

    public static string FormatDocument(RdFantomasFormatDocumentArgs args) =>
      FSharpAsync.StartAsTask(
          CodeFormatter.FormatDocumentAsync(args.FileName, SourceOrigin.SourceOrigin.NewSourceString(args.Source),
            Convert(args.FormatConfig),
            CreateFSharpParsingOptions.Invoke(GetParsingOptions(args.ParsingOptions).ToArray()) as dynamic,
            Checker), null, null)
        .Result.Replace("\r\n", args.NewLineText);

    private static IEnumerable<object> GetParsingOptions(RdFcsParsingOptions options)
    {
      yield return new[] { options.LastSourceFile };
      yield return ListModule.OfArray(options.ConditionalCompilationDefines);
      yield return DefaultDiagnosticOptions;
      if (CurrentVersion >= Version46) yield return options.LangVersion;
      yield return false; // isInteractive
      yield return options.LightSyntax ?? FSharpOption<bool>.None;
      yield return false; // compilingFsLib
      yield return options.IsExe;
    }

    private static FormatConfig Convert(string[] riderFormatConfigValues)
    {
      var riderFormatConfigDict =
        FormatConfigFields
          .Zip(riderFormatConfigValues,
            (field, valueData) =>
              (field.Name, Value: valueData == ""
                ? field.Value
                : field.Value switch
                {
                  int _ => int.Parse(valueData),
                  bool _ => bool.Parse(valueData),
                  { } => ConvertEnumValue(valueData)
                }))
          .ToDictionary(x => x.Name, x => x.Value);

      var formatConfigValues =
        FormatConfigFields
          .Select(field => riderFormatConfigDict.TryGetValue(field.Name, out var value) ? value : field.Value)
          .ToArray();
      return FSharpValue.MakeRecord(FormatConfigType, formatConfigValues, null) as FormatConfig;
    }

    // TODO: alternatively, we can reuse the logic from
    // https://github.com/fsprojects/fantomas/blob/master/src/Fantomas.Extras/EditorConfig.fs
    // such as `parseOptionsFromEditorConfig`,
    // or take the OfConfigString methods of discriminated unions as a contract
    // https://github.com/fsprojects/fantomas/blob/master/src/Fantomas/FormatConfig.fs
    private static object ConvertEnumValue(string setting)
    {
      var camelCaseSetting = StringUtil.MakeUpperCamelCaseName(setting);

      return FormatConfigDUs.TryGetValue(camelCaseSetting, out var unionCase)
        ? FSharpValue.MakeUnion(unionCase, null, FSharpOption<BindingFlags>.None)
        : throw new ArgumentOutOfRangeException($"Unknown Fantomas FormatSetting {setting}");
    }
  }
}
