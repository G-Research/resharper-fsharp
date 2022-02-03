namespace JetBrains.ReSharper.Plugins.FSharp.Services.Formatter

open System
open System.Collections.Generic
open System.Runtime.InteropServices
open JetBrains.Application.Notifications
open JetBrains.Application.UI.Components
open JetBrains.Application.UI.Options
open JetBrains.Application.UI.Options.OptionsDialog
open JetBrains.Collections.Viewable
open JetBrains.ProjectModel
open JetBrains.ReSharper.Plugins.FSharp.Settings
open JetBrains.ReSharper.Resources.Resources.Icons
open JetBrains.IDE.UI.Extensions
open JetBrains.Rider.Model
open JetBrains.Rider.Model.UIAutomation
open JetBrains.Threading
open JetBrains.IDE.UI.Extensions.Validation

[<AutoOpen>]
module private FantomasLiterals =
    let [<Literal>] minimalSupportedVersion = "3.2"

type FantomasValidationResult =
    | Ok
    | FailedToRun
    | UnsupportedVersion
    | SelectedButNotFound
    | NotFound

type FantomasVersion =
    | Bundled
    | SolutionDotnetTool
    | GlobalDotnetTool

type FantomasRunSettings =
    { Version: FantomasVersion * string
      Path: string
      mutable Status: FantomasValidationResult }

type FantomasDiagnosticNotification =
    { Event: FantomasValidationResult
      Version: FantomasVersion
      FallbackVersion: FantomasVersion }

[<SolutionInstanceComponent>]
type FantomasDetector(lifetime, settingsProvider: FSharpFantomasSettingsProvider) =
    let minimalSupportedVersion = Version(minimalSupportedVersion)
    let notificationSignal = Signal<FantomasDiagnosticNotification>()
    let rwLock = JetFastSemiReenterableRWLock()

    let notificationsFired =
       let hashset = HashSet<FantomasVersion>()
       hashset.Add(FantomasVersion.Bundled) |> ignore
       hashset

    let settingsData =
        let dict = Dictionary(3)
        dict[Bundled] <- { Version = Bundled, "4.5.11"; Path = null; Status = Ok }
        dict[SolutionDotnetTool] <- { Version = SolutionDotnetTool, "1.1.1"; Path = "path1"; Status = Ok }
        dict[GlobalDotnetTool] <- { Version = GlobalDotnetTool, "1.1.1"; Path = "path2"; Status = Ok }
        dict

    let mutable versionToRun = ViewableProperty(settingsData[Bundled])
    let mutable delayedNotifications = []

    let calculateVersionToRun version =
        let rec calculateVersionRec version warnings =
            let nextVersionToSearch =
                match version with
                | FantomasVersion.SolutionDotnetTool -> FantomasVersion.GlobalDotnetTool
                | _ -> FantomasVersion.Bundled

            match settingsData.TryGetValue version with
            | true, { Status = Ok } -> version, warnings
            | true, { Status = status } ->
                let versionToRun, warnings = calculateVersionRec nextVersionToSearch warnings

                let warnings =
                    if notificationsFired.Contains(version) then warnings else
                    notificationsFired.Add(version) |> ignore
                    { Event = status; Version = version; FallbackVersion = versionToRun } :: warnings

                versionToRun, warnings
            | false, _ -> calculateVersionRec nextVersionToSearch warnings

        calculateVersionRec version []

    let rec calculateRunSettings setting =
        let startToSearchVersion =
            match setting with
            | FantomasVersionSettings.AutoDetected
            | FantomasVersionSettings.SolutionDotnetTool -> FantomasVersion.SolutionDotnetTool
            | FantomasVersionSettings.GlobalDotnetTool -> FantomasVersion.GlobalDotnetTool
            | _ -> FantomasVersion.Bundled

        let version, warnings = calculateVersionToRun startToSearchVersion
        settingsData[version], warnings

    let fireNotifications () =
        for notification in delayedNotifications do
            notificationSignal.Fire(notification)
        delayedNotifications <- []

    let recalculateState versionSetting =
        let version, warnings = calculateRunSettings versionSetting
        versionToRun.Value <- version
        delayedNotifications <- warnings

    let validate version =
        //if Version.Parse(version) < minimalSupportedVersion then UnsupportedVersion
        //else Ok
        Ok

    do
        if isNull settingsProvider then () else

        settingsProvider.Version.Change.Advise(lifetime, fun x ->
            if not x.HasNew then () else
            use _ = rwLock.UsingWriteLock()
            recalculateState x.New)

        use _ = rwLock.UsingWriteLock()
        let dotnetToolVersions = HashSet<FantomasVersion>()
        dotnetToolVersions.Add(FantomasVersion.SolutionDotnetTool) |> ignore

        let selectedVersionString = "1.2.3"
        let selectedVersionByUser = settingsProvider.Version.Value

        //TODO: replace with real one
        //TODO: move to separate function
        for version in dotnetToolVersions do
            let versionString = "1.2.3"
            let path = "фывфыв"
            settingsData[version] <-
                { Version = version, versionString; Path = path; Status = validate versionString }

    static member Create(lifetime) = FantomasDetector(lifetime, Unchecked.defaultof<FSharpFantomasSettingsProvider>)

    member x.TryRun(runAction: string -> unit) =
        use _ = rwLock.UsingWriteLock()
        fireNotifications()
        let { Path = path } = versionToRun.Value
        try runAction path
        with _ ->
            versionToRun.Value.Status <- FailedToRun
            recalculateState settingsProvider.Version.Value
            x.TryRun(runAction)

    member x.GetSettings() =
        let settings = Dictionary<_, _>()
        use _ = rwLock.UsingReadLock()

        for kvp in settingsData do
            let key =
                match kvp.Key with
                | FantomasVersion.Bundled -> FantomasVersionSettings.Bundled
                | FantomasVersion.SolutionDotnetTool -> FantomasVersionSettings.SolutionDotnetTool
                | FantomasVersion.GlobalDotnetTool -> FantomasVersionSettings.GlobalDotnetTool
            settings.Add(key, kvp.Value)

        let autoDetectedSettingsData, _ = calculateRunSettings FantomasVersionSettings.AutoDetected
        settings.Add(FantomasVersionSettings.AutoDetected, autoDetectedSettingsData)
        settings

    member x.VersionToRun = versionToRun
    member x.NotificationProducer = notificationSignal


[<OptionsPage(nameof(FantomasPage), "Fantomas", typeof<PsiFeaturesUnsortedOptionsThemedIcons.Indent>)>]
type FantomasPage(lifetime, smartContext: OptionsSettingsSmartContext, optionsPageContext: OptionsPageContext,
                  [<Optional; DefaultParameterValue(null: ISolution)>] solution: ISolution,
                  uiApplication: IUIApplication) as this =
    inherit FSharpOptionsPageBase(lifetime, optionsPageContext, smartContext)
    let _ = PsiFeaturesUnsortedOptionsThemedIcons.Indent // workaround to create assembly reference (dotnet/fsharp#3522)

    let fantomasDetector =
        if isNull solution then FantomasDetector.Create(lifetime) else solution.GetComponent<FantomasDetector>()

    let formatSettingItem setting (settingsData: Dictionary<_, _>) =
        let { Version = fantomasVersion, version; Status = status} = settingsData[setting]
        let description =
            match setting with
            | FantomasVersionSettings.SolutionDotnetTool -> "From dotnet-tools.json"
            | FantomasVersionSettings.GlobalDotnetTool -> "From .NET global tools"
            | FantomasVersionSettings.Bundled -> "Bundled"
            | _ -> "Auto detected"

        let version =
            match setting with
            | FantomasVersionSettings.AutoDetected ->
                match fantomasVersion with
                | FantomasVersion.SolutionDotnetTool -> $" (v.{version} dotnet-tools.json)"
                | FantomasVersion.GlobalDotnetTool -> $" (v.{version} global)"
                | _ -> $" (Bundled v.{version})"
            | _ ->

            match status with
            | Ok -> $" (v.{version})"
            | FailedToRun -> $" (v.{version} failed to run)"
            | UnsupportedVersion -> $" (v.{version} not supported)"
            | SelectedButNotFound
            | NotFound -> " (not found)"

        RichTextModel(List [| RichStringModel(description); RichStringModel(version, ThemeColorId(ColorId.Gray))|])
            .GetBeRichText() :> BeControl

    let validate (comboBox: BeComboBox) (settingsData: Dictionary<_, _>) =
        let settingName = settingsData.Keys |> Seq.sortBy id |> Seq.item comboBox.SelectedIndex.Value
        match settingsData[settingName] with
        | { Status = FailedToRun } ->
            ValidationResult(ValidationStates.validationWarning, "The specified Fantomas version failed to run.")
        | { Status = UnsupportedVersion } ->
            ValidationResult(ValidationStates.validationWarning, $"Supported Fantomas versions: {minimalSupportedVersion} and later.")
        | { Status = SelectedButNotFound } ->
            ValidationResult(ValidationStates.validationWarning, "The specified Fantomas version not found.")
        | _ ->
            ValidationResult(ValidationStates.validationPassed)

    let createComboBox (key: JetBrains.DataFlow.IProperty<FantomasVersionSettings>) =
        let settingsData = fantomasDetector.GetSettings()
        let beComboBoxFromEnum =
            key.GetBeComboBoxFromEnum(lifetime,
                PresentComboItem (fun x y z -> formatSettingItem y settingsData),
                seq {
                    if not (settingsData.ContainsKey FantomasVersionSettings.SolutionDotnetTool) then
                        FantomasVersionSettings.SolutionDotnetTool
                    if not (settingsData.ContainsKey FantomasVersionSettings.GlobalDotnetTool) then
                        FantomasVersionSettings.GlobalDotnetTool
                }
            )
        let beComboBoxFromEnum = beComboBoxFromEnum.WithValidationRule(lifetime, (fun () ->
            let res = validate beComboBoxFromEnum settingsData
            struct(res.ResultMessage, res.State)))

        let validationLabel = BeValidationLabel(BeControls.GetRichText())

        let beSpanGrid = BeControls.GetSpanGrid("auto,auto")

        beComboBoxFromEnum.ValidationResult.Change.Advise(lifetime, fun x -> validationLabel.ValidationResult.Value <- ValidationResult(x.State, x.ResultMessage) )
        beComboBoxFromEnum.SelectedIndex.Change.Advise(lifetime, fun x -> beComboBoxFromEnum.Revalidate.Fire(ValidationTrigger.ValueChanged))
        beComboBoxFromEnum.ValidationResult.Value <- validate beComboBoxFromEnum settingsData

        beSpanGrid
            .AddColumnElementsToNewRow(BeSizingType.Fit, true, [|"Fantomas version".GetBeLabel() :> BeControl; beComboBoxFromEnum.WithMinSize(BeControlSizeFixed(BeControlSizeType.FIT_TO_CONTENT, BeControlSizeType.FIT_TO_CONTENT), lifetime)|])
            .AddColumnElementsToNewRow(BeSizingType.Fit, false, [|"".GetBeLabel() :> BeControl; validationLabel |])

    do
        use indent = this.Indent()

        this.AddControl((fun (key: FSharpFantomasOptions) -> key.Version), createComboBox) |> ignore//.DisableDescendantsWhenDisabled(this.Lifetime)

        this.AddCommentText("To use a specified Fantomas version, install it globally via 'dotnet tool install fantomas-tool'\n" +
                            "or specify it in dotnet-tools.json file in the solution directory. Supported Fantomas versions: 3.2 and later.")
        this.AddLinkButton("DotnetToolsLink", "Learn more", fun () -> uiApplication.OpenUri("https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet-tool-install")) |> ignore


[<SolutionComponent>]
type FantomasNotificationsManager(lifetime, settings: FantomasDetector, notifications: UserNotifications,
                                  optionsManager: OptionsManager) =
    let goToSettings = [| UserNotificationCommand("Settings", fun _ -> optionsManager.BeginShowOptions(nameof(FantomasPage))) |]
    let openDotnetToolsOrGoToSettings = [| UserNotificationCommand("Open dotnet-tools.json", fun _ -> ()); goToSettings[0] |]

    let createFallbackMessage = function
        | SolutionDotnetTool -> ""
        | GlobalDotnetTool -> "<b>Falling back to the global dotnet tool Fantomas.</b>"
        | Bundled -> "<b>Falling back to the bundled formatter.</b>"

    let createBodyMessage { Event = event; Version = version; FallbackVersion = fallbackVersion } =
        let fallbackMessage = createFallbackMessage fallbackVersion

        match event with
        | SelectedButNotFound ->
            match version with
            //TODO: change message
            | FantomasVersion.SolutionDotnetTool ->
                $"""Fantomas version specified in "dotnet-tool.json" is not installed.<br>{fallbackMessage}"""
            | FantomasVersion.GlobalDotnetTool ->
                $"""Fantomas is not installed globally via 'dotnet tool install fantomas-tool' is not found.<br>{fallbackMessage}<br>Supported versions: {minimalSupportedVersion} and later."""
            | _ -> ""

        | FailedToRun ->
            match version with
            | FantomasVersion.SolutionDotnetTool ->
                $"""Fantomas specified in "dotnet-tool.json" failed to run.<br>{fallbackMessage}"""
            | FantomasVersion.GlobalDotnetTool ->
                $"""Fantomas installed globally via 'dotnet tool install fantomas-tool' failed to run.<br>{fallbackMessage}"""
            | _ -> ""

        | UnsupportedVersion ->
            match version with
            | FantomasVersion.SolutionDotnetTool ->
                $"""Fantomas version specified in "dotnet-tool.json" is not compatible with the current Rider version.<br>{fallbackMessage}<br>Supported formatter versions: {minimalSupportedVersion} and later."""
            | FantomasVersion.GlobalDotnetTool ->
                $"""Fantomas installed globally via 'dotnet tool install fantomas-tool' is not compatible with the current Rider version.<br>{fallbackMessage}<br>Supported versions: {minimalSupportedVersion} and later."""
            | _ -> ""
        | _ -> ""

    let getCommands = function
        | FantomasVersion.SolutionDotnetTool -> openDotnetToolsOrGoToSettings
        | _ -> goToSettings

    let createNotification notification =
        let body = createBodyMessage notification
        let commands = getCommands notification.Version

        notifications.CreateNotification(lifetime,
            title = "Unable to use specified Fantomas version",
            body = body,
            additionalCommands = commands) |> ignore

    do settings.NotificationProducer.Advise(lifetime, createNotification)
