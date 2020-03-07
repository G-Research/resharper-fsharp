namespace JetBrains.ReSharper.Plugins.FSharp.Psi.Features.Refactorings

open JetBrains.ReSharper.Plugins.FSharp.Psi
open JetBrains.ReSharper.Psi
open JetBrains.ReSharper.Refactorings.Workflow

[<Language(typeof<FSharpLanguage>)>]
type FSharpRefactoringLanguageService() =
    inherit InternalRefactoringLanguageService()

    override x.CreateIntroduceVariableHelper() = FSharpIntroduceVarHelper() :> _

    override x.CreateIntroduceVariable(workflow, solution, driver) =
            if not (solution.FSharpExperimentalFeaturesEnabled()) then null else
            FSharpIntroduceVariable(workflow, solution, driver) :> _

    override x.CreateInlineVar(workflow, solution, driver) =
//        FSharpInlineVar(workflow, solution, driver) :> _
        null

