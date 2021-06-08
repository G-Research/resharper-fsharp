namespace JetBrains.ReSharper.Plugins.FSharp.Psi.Features.Daemon.QuickFixes

open JetBrains.ReSharper.Plugins.FSharp.Psi.Features.Daemon.Highlightings
open JetBrains.ReSharper.Plugins.FSharp.Psi.Features.Refactorings
open JetBrains.ReSharper.Plugins.FSharp.Psi.Tree
open JetBrains.ReSharper.Psi.Tree
open JetBrains.TextControl

type IntroduceVarFix(expr: IFSharpExpression, escapeLambdas) =
    inherit FSharpQuickFixBase()

    let mutable expr = expr

    let suggestInnerExpression (expr: IFSharpExpression) =
        let binaryAppExpr = expr.As<IBinaryAppExpr>()
        if isNotNull binaryAppExpr && FSharpIntroduceVariable.CanInsertBeforeRightOperand(binaryAppExpr) then
            binaryAppExpr.RightArgument
        else
            null

    new (warning: UnitTypeExpectedWarning) =
        IntroduceVarFix(warning.Expr, false)

    new (warning: FunctionValueUnexpectedWarning) =
        IntroduceVarFix(warning.Expr, false)

    new (error: UnitTypeExpectedError) =
        IntroduceVarFix(error.Expr, false)

    // for protected/base members access in lambda
    new () =
        // todo: check object expressions?
        // todo: check no local values are moved out of scope
        //   * implement for properties only at first?
        //   * for method calls: check that values are defined outside lambda
        IntroduceVarFix(null, true)

    override x.Text = "Introduce 'let' binding"

    override x.IsAvailable _ =
        FSharpIntroduceVariable.CanIntroduceVar(expr)

    member x.SelectExpression(solution, textControl) =
        let innerExpression = suggestInnerExpression expr
        if isNull innerExpression then expr else

        let expressions =
            [| expr, "Whole expression"
               innerExpression, "Last operand" |]

        x.SelectExpression(expressions, solution, textControl)

    override x.Execute(solution, textControl) =
        expr <- x.SelectExpression(solution, textControl)
        if isNull expr then () else

        base.Execute(solution, textControl)

        textControl.Selection.SetRange(expr.GetDocumentRange().TextRange)
        FSharpIntroduceVariable.IntroduceVar(expr, textControl, true, escapeLambdas)
