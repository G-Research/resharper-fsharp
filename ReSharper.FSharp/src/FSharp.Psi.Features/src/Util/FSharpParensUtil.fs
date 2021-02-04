[<AutoOpen>]
module JetBrains.ReSharper.Plugins.FSharp.Psi.Features.Util.FSharpParensUtil

open System
open FSharp.Compiler
open JetBrains.Application.Settings
open JetBrains.ReSharper.Plugins.FSharp.Psi
open JetBrains.ReSharper.Plugins.FSharp.Psi.Tree
open JetBrains.ReSharper.Plugins.FSharp.Psi.Impl
open JetBrains.ReSharper.Plugins.FSharp.Services.Formatter
open JetBrains.ReSharper.Psi.ExtensionsAPI
open JetBrains.ReSharper.Psi.ExtensionsAPI.Tree
open JetBrains.ReSharper.Psi.Tree

let deindentsBody (expr: IFSharpExpression) =
    match expr with
    | :? IMatchClauseListOwnerExpr as matchExpr ->
        if expr.IsSingleLine then false else

        let clause = matchExpr.Clauses.LastOrDefault()
        if isNull clause then false else

        let clauseExpr = clause.Expression
        isNotNull clauseExpr && clauseExpr.Indent = expr.Indent

    | :? IIfExpr as ifExpr ->
        if expr.IsSingleLine then false else

        let elseExpr = ifExpr.ElseExpr
        isNotNull elseExpr && elseExpr.Indent = expr.Indent

    | _ -> false

let contextRequiresParens (context: IFSharpExpression) =
    // todo: check nested parens
    isNotNull (TypeInheritNavigator.GetByCtorArgExpression(context)) ||
    isNotNull (ObjExprNavigator.GetByArgExpression(context)) ||
    isNotNull (NewExprNavigator.GetByArgumentExpression(context))


let isHighPrecedenceApp (appExpr: IPrefixAppExpr) =
    if isNull appExpr then false else

    let funExpr = appExpr.FunctionExpression
    let argExpr = appExpr.ArgumentExpression

    // todo: attribute arg :(
    isNotNull funExpr && isNotNull argExpr && funExpr.NextSibling == argExpr


let (|Prefix|_|) (other: string) (str: string) =
    if str.StartsWith(other, StringComparison.Ordinal) then someUnit else None

let operatorName (binaryApp: IBinaryAppExpr) =
    let refExpr = binaryApp.Operator
    if isNull refExpr then SharedImplUtil.MISSING_DECLARATION_NAME else

    // todo: fix op tokens in references
    let name = refExpr.GetText()
    PrettyNaming.DecompileOpName(name)

let operatorPrecedence (binaryApp: IBinaryAppExpr) =
    let name = operatorName binaryApp
    if name.Length = 0 then 0 else

    match name with
    | "|" | "||" -> 1
    | "&" | "&&" -> 2
    | Prefix "!=" | Prefix "<" | Prefix ">" | Prefix "|" | Prefix "&" | "$" | "=" -> 4
    | Prefix "^" -> 5
    | Prefix "::" -> 6
    | Prefix "+" | Prefix "-" -> 8
    | Prefix "*" | Prefix "/" | Prefix "%" -> 9
    | Prefix "**" -> 10
    | _ -> 0

let precedence (expr: ITreeNode) =
    match expr with
    | :? ILetOrUseExpr -> 1

    | :? IIfThenElseExpr
    | :? IMatchLikeExpr -> 3

    // todo: type test, cast, typed
    | :? ITypedLikeExpr -> 4
    | :? ILambdaExpr -> 5
    | :? ISequentialExpr -> 6
    | :? ITupleExpr -> 7

    | :? IBinaryAppExpr as binaryAppExpr ->
        // todo: remove this hack and align common precedence
        match operatorName binaryAppExpr with
        | "|>" -> 2
        | _ -> 8

    | :? IDoLikeExpr -> 9

    | :? IPrefixAppExpr as prefixApp ->
        if isHighPrecedenceApp prefixApp then 10 else 9

    | :? IFSharpExpression -> 11

    | _ -> 0

let startsBlock (context: IFSharpExpression) =
//    isNotNull (BinaryAppExprNavigator.GetByRightArgument(context)) || // todo: not really a block here :(
    isNotNull (SetExprNavigator.GetByRightExpression(context))

let getContextPrecedence (context: IFSharpExpression) =
    if isNotNull (QualifiedExprNavigator.GetByQualifier(context)) then 10 else

    if startsBlock context then 0 else precedence context.Parent

let checkPrecedence (context: IFSharpExpression) node =
    let nodePrecedence = precedence node
    let contextPrecedence = getContextPrecedence context
    nodePrecedence < contextPrecedence


let rec getContainingCompoundExpr (context: IFSharpExpression): IFSharpExpression =
    match BinaryAppExprNavigator.GetByArgument(context) with
    | null -> context
    | binaryAppExpr -> getContainingCompoundExpr binaryAppExpr

let rec getLongestBinaryAppParentViaRightArg (context: IFSharpExpression): IFSharpExpression =
    match BinaryAppExprNavigator.GetByRightArgument(context) with
    | null -> context
    | binaryAppExpr -> getLongestBinaryAppParentViaRightArg binaryAppExpr

let rec getQualifiedExpr (expr: IFSharpExpression) =
    match QualifiedExprNavigator.GetByQualifier(expr.IgnoreParentParens()) with
    | null -> expr.IgnoreParentParens()
    | expr -> getQualifiedExpr expr

let rec getFirstQualifier (expr: IQualifiedExpr) =
    match expr.Qualifier with
    | null -> expr :> IFSharpExpression
    | :? IQualifiedExpr as qualifier -> getFirstQualifier qualifier
    | qualifier -> qualifier


//let private canBeTopLevelArgInHighPrecedenceApp (expr: IFSharpExpression) =
//    // todo: check `ignore{| Field = 1 + 1 |}.Field` vs `ignore[].Head` 
//    expr :? IArrayOrListExpr || expr :? IObjExpr || expr :? IRecordLikeExpr

let isHighPrecedenceAppArg context =
    let appExpr = PrefixAppExprNavigator.GetByArgumentExpression(context)
    if isNotNull appExpr then
        let funExpr = appExpr.FunctionExpression
        isNotNull funExpr && funExpr.NextSibling == context else

    // todo: add test with spaces
    let chameleonExpr = ChameleonExpressionNavigator.GetByExpression(context)
    let attribute = AttributeNavigator.GetByArgExpression(chameleonExpr)
    if isNotNull attribute then
        let referenceName = attribute.ReferenceName
        isNotNull referenceName && referenceName.NextSibling == chameleonExpr else

    false

let rec needsParens (context: IFSharpExpression) (expr: IFSharpExpression) =
    if expr :? IParenExpr then false else

    let expr = expr.IgnoreInnerParens()
    if isNull expr|| contextRequiresParens context then true else

    let ParentPrefixAppExpr = PrefixAppExprNavigator.GetByArgumentExpression(context)
    if isHighPrecedenceApp ParentPrefixAppExpr && isNotNull (QualifiedExprNavigator.GetByQualifier(ParentPrefixAppExpr)) then true else

    // todo: calc once?
    let allowHighPrecedenceAppParens = 
        let settingsStore = context.GetSettingsStoreWithEditorConfig()
        settingsStore.GetValue(fun (key: FSharpFormatSettingsKey) -> key.AllowHighPrecedenceAppParens)

    if isHighPrecedenceAppArg context && allowHighPrecedenceAppParens then true else

    match expr with
    | :? IIfThenElseExpr as ifExpr ->
        isNotNull (IfThenElseExprNavigator.GetByThenExpr(context)) ||
        isNotNull (ConditionOwnerExprNavigator.GetByConditionExpr(context)) ||
        isNotNull (BinaryAppExprNavigator.GetByLeftArgument(context)) ||
        isNotNull (PrefixAppExprNavigator.GetByFunctionExpression(context)) ||
        isNotNull (TypedLikeExprNavigator.GetByExpression(context)) ||
        isNotNull (WhenExprClauseNavigator.GetByExpression(getContainingCompoundExpr context)) ||

        let tupleExpr = TupleExprNavigator.GetByExpression(context)
        isNotNull tupleExpr && tupleExpr.Expressions.LastOrDefault() != context ||

        checkPrecedence context expr ||
        needsParens context ifExpr.ElseExpr

    | :? IMatchClauseListOwnerExpr as matchExpr ->
        isNotNull (WhenExprClauseNavigator.GetByExpression(getContainingCompoundExpr context)) ||
        checkPrecedence context expr ||

        let lastClause = matchExpr.ClausesEnumerable.LastOrDefault()
        let lastClauseExpr = if isNull lastClause then null else lastClause.Expression
        if isNull lastClauseExpr then false else // todo: or true?

        needsParens context lastClauseExpr ||
        lastClauseExpr.Indent = matchExpr.Indent ||

        let binaryAppExpr = BinaryAppExprNavigator.GetByLeftArgument(context)
        let opExpr = if isNull binaryAppExpr then null else binaryAppExpr.Operator

        isNotNull opExpr && opExpr.Indent <> matchExpr.Indent ||
        
        false

    | :? ITupleExpr ->
        isNotNull (WhenExprClauseNavigator.GetByExpression(getContainingCompoundExpr context)) ||
        isNotNull (AttributeNavigator.GetByExpression(context)) ||
        isNotNull (TupleExprNavigator.GetByExpression(context)) ||

        checkPrecedence context expr

    | :? IReferenceExpr as refExpr ->
        let typeArgumentList = refExpr.TypeArgumentList

        let attribute = AttributeNavigator.GetByExpression(context)
        isNotNull attribute && (isNotNull attribute.Target || isNotNull typeArgumentList) ||

        isNotNull (AppExprNavigator.GetByArgument(context)) && getFirstQualifier refExpr :? IAppExpr ||

        // todo: tests
        isNull typeArgumentList && isNull refExpr.Qualifier && PrettyNaming.IsOperatorName (refExpr.GetText()) ||

        checkPrecedence context expr

    | :? ITypedLikeExpr ->
        isNotNull (WhenExprClauseNavigator.GetByExpression(getContainingCompoundExpr context)) ||
        isNotNull (AttributeNavigator.GetByExpression(context)) ||
        checkPrecedence context expr

    | :? IBinaryAppExpr as binaryAppExpr ->
        let precedence = operatorPrecedence binaryAppExpr

        // todo: check assoc

        let parentViaLeftArg = BinaryAppExprNavigator.GetByLeftArgument(context)
        isNotNull parentViaLeftArg && operatorPrecedence parentViaLeftArg > precedence ||

        let parentViaRightArg = BinaryAppExprNavigator.GetByRightArgument(context)
        isNotNull parentViaRightArg && operatorPrecedence parentViaRightArg >= precedence ||

        checkPrecedence context expr ||
        needsParens context binaryAppExpr.RightArgument ||
        needsParens context binaryAppExpr.LeftArgument

    | :? IPrefixAppExpr as prefixAppExpr ->
        isNotNull (PrefixAppExprNavigator.GetByArgumentExpression(getQualifiedExpr context)) ||

        checkPrecedence context expr ||
        needsParens context prefixAppExpr.ArgumentExpression

    | :? ISequentialExpr ->
        isNotNull (WhenExprClauseNavigator.GetByExpression(getContainingCompoundExpr context)) ||
        checkPrecedence context expr

    | _ ->

    let binaryApp = BinaryAppExprNavigator.GetByLeftArgument(context)
    if isNull binaryApp then checkPrecedence context expr else

    if deindentsBody expr then true else

    let operator = binaryApp.Operator
    if isNotNull operator && context.Indent = operator.Indent then false else

    let rightArgument = binaryApp.RightArgument
    if isNotNull rightArgument && context.Indent = rightArgument.Indent then false else

    precedence binaryApp.LeftArgument < precedence binaryApp


let addParens (expr: IFSharpExpression) =
    let exprCopy = expr.Copy()
    let factory = expr.CreateElementFactory()

    let parenExpr = factory.CreateParenExpr()
    let parenExpr = ModificationUtil.ReplaceChild(expr, parenExpr)
    let expr = parenExpr.SetInnerExpression(exprCopy)

    shiftNode 1 expr
    expr


let addParensIfNeeded (expr: IFSharpExpression) =
    let context = expr.IgnoreParentParens()
    if context != expr || not (needsParens context expr) then expr else
    addParens expr
