namespace JetBrains.ReSharper.Plugins.FSharp.Services.Formatter

open System
open System.Collections.Generic
open System.IO
open System.Runtime.InteropServices
open JetBrains.Application.Notifications
open JetBrains.Application.UI.Components
open JetBrains.Application.UI.Options
open JetBrains.Application.UI.Options.OptionsDialog
open JetBrains.Collections.Viewable
open JetBrains.ProjectModel
open JetBrains.ProjectModel.NuGet.DotNetTools
open JetBrains.ReSharper.Plugins.FSharp.Settings
open JetBrains.ReSharper.Resources.Resources.Icons
open JetBrains.IDE.UI.Extensions
open JetBrains.Rider.Model
open JetBrains.Rider.Model.UIAutomation
open JetBrains.Threading
open JetBrains.IDE.UI.Extensions.Validation

[<AutoOpen>]
module private FantomasVersions =
    let [<Literal>] MinimalSupportedVersion = "3.2"
    let [<Literal>] BundledVersion = "4.5.11"

type FantomasValidationResult =
    | Ok
    | FailedToRun
    | UnsupportedVersion
    | SelectedButNotFound

type FantomasVersion =
    | Bundled
    | LocalDotnetTool
    | GlobalDotnetTool

type FantomasRunSettings =
    { Version: FantomasVersion * string
      Path: VirtualFileSystemPath
      mutable Status: FantomasValidationResult }

type FantomasDiagnosticNotification =
    { Event: FantomasValidationResult
      Version: FantomasVersion
      FallbackVersion: FantomasVersion }

[<SolutionInstanceComponent>]
type FantomasDetector(lifetime, fantomasSettingsProvider: FSharpFantomasSettingsProvider,
                      dotnetToolsTracker: NuGetDotnetToolsTracker) =
    let [<Literal>] fantomasToolPackageId = "fantomas-tool"
    let [<Literal>] fantomasPackageId = "fantomas"
    let minimalSupportedVersion = Version(MinimalSupportedVersion)
    let notificationSignal = Signal<FantomasDiagnosticNotification>()
    let rwLock = JetFastSemiReenterableRWLock()

    let calculateGlobalFantomasToolPath version =
        Path.Combine(CliFolderPathCalculator.ToolPackageFolderPath, fantomasToolPackageId, version, fantomasToolPackageId, version, "tools", "netcoreapp3.1", "any", "Fantomas.dll")

    let notificationsFired =
       let hashset = HashSet(3)
       hashset.Add(FantomasVersion.Bundled) |> ignore
       hashset

    let versionsData =
        let dict = Dictionary(3)
        dict[Bundled] <- { Version = Bundled, BundledVersion; Path = null; Status = Ok }
        dict

    let mutable versionToRun = ViewableProperty(versionsData[Bundled])
    let mutable delayedNotifications = []

    let calculateVersionToRun version =
        let rec calculateVersionRec version warnings =
            let nextVersionToSearch =
                match version with
                | FantomasVersion.LocalDotnetTool -> FantomasVersion.GlobalDotnetTool
                | _ -> FantomasVersion.Bundled

            match versionsData.TryGetValue(version) with
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
        let searchStartVersion =
            match setting with
            | FantomasVersionSettings.AutoDetected
            | FantomasVersionSettings.LocalDotnetTool -> FantomasVersion.LocalDotnetTool
            | FantomasVersionSettings.GlobalDotnetTool -> FantomasVersion.GlobalDotnetTool
            | _ -> FantomasVersion.Bundled

        let version, warnings = calculateVersionToRun searchStartVersion
        versionsData[version], warnings

    let fireNotifications () =
        for notification in delayedNotifications do
            notificationSignal.Fire(notification)
        delayedNotifications <- []

    let recalculateState versionSetting =
        let version, warnings = calculateRunSettings versionSetting
        versionToRun.Value <- version
        delayedNotifications <- warnings

    let validate version (pathToExecutable: VirtualFileSystemPath) =
        match Version.TryParse(version) with
        | true, version when version >= minimalSupportedVersion ->
            if isNotNull pathToExecutable && pathToExecutable.ExistsDirectory then Ok else FailedToRun
        | _ -> UnsupportedVersion

    let invalidateDotnetTool toolVersion toolInfo =
        match toolInfo with
        | Some (version, pathToExecutable: VirtualFileSystemPath) ->
            match versionsData.TryGetValue(toolVersion) with
            | true, { Version = _, cachedVersion } when version = cachedVersion -> ()
            | _ ->
                versionsData.Remove(toolVersion) |> ignore
                notificationsFired.Remove(toolVersion) |> ignore
                let fantomasDirectory = if isNull pathToExecutable then null else pathToExecutable.Directory
                let status = validate version fantomasDirectory
                versionsData.Add(toolVersion, { Version = toolVersion, version; Path = fantomasDirectory; Status = status })
        | _ ->
            versionsData.Remove(toolVersion) |> ignore
            notificationsFired.Remove(toolVersion) |> ignore
    
    let invalidateDotnetTools (cache: ToolManifestCache) selectedVersion =
        cache.LocalToolManifestInfo.Tools
        |> Seq.tryFind (fun x -> x.Info.PackageId = fantomasPackageId || x.Info.PackageId = fantomasToolPackageId)
        |> Option.map (fun x -> x.Info.Version, x.PathToExecutable)
        |> invalidateDotnetTool LocalDotnetTool

        cache.GlobalToolManifests
        |> Seq.tryFind (fun x -> (x.Name = fantomasPackageId || x.Name = fantomasToolPackageId) && x.Versions.Count > 0)
        |> Option.map (fun x ->
            let version = (Seq.max x.Versions).OriginalVersion
            version, VirtualFileSystemPath.Parse(calculateGlobalFantomasToolPath version, InteractionContext.Local))
        |> invalidateDotnetTool GlobalDotnetTool

        match selectedVersion with
        | FantomasVersionSettings.LocalDotnetTool ->
            if versionsData.ContainsKey(LocalDotnetTool) then () else
            versionsData.Add(LocalDotnetTool, { Version = LocalDotnetTool, ""; Path = null; Status = SelectedButNotFound })

        | FantomasVersionSettings.GlobalDotnetTool ->
            if versionsData.ContainsKey(GlobalDotnetTool) then () else
            versionsData.Add(GlobalDotnetTool, { Version = GlobalDotnetTool, ""; Path = null; Status = SelectedButNotFound })

        | _ -> ()

    do
        if isNull fantomasSettingsProvider || isNull dotnetToolsTracker then () else

        fantomasSettingsProvider.Version.Change.Advise(lifetime, fun x ->
            if not x.HasNew then () else
            use _ = rwLock.UsingWriteLock()
            recalculateState x.New)
        
        dotnetToolsTracker.ToolManifestCache.Change.Advise(lifetime, fun x ->
            if not x.HasNew || isNull x.New then () else
            use _ = rwLock.UsingWriteLock()
            let settingsVersion = fantomasSettingsProvider.Version.Value
            invalidateDotnetTools x.New settingsVersion
            recalculateState settingsVersion)

    static member Create(lifetime) =
        FantomasDetector(lifetime, Unchecked.defaultof<FSharpFantomasSettingsProvider>, Unchecked.defaultof<NuGetDotnetToolsTracker>)

    member x.TryRun(runAction: VirtualFileSystemPath -> unit) =
        use _ = rwLock.UsingWriteLock()
        fireNotifications()
        let { Path = path } as versionToRun = versionToRun.Value
        try runAction path
        with _ ->
            versionToRun.Status <- FailedToRun
            recalculateState fantomasSettingsProvider.Version.Value
            x.TryRun(runAction)

    member x.GetSettings() =
        let settings = Dictionary()
        use _ = rwLock.UsingReadLock()

        for kvp in versionsData do
            let key =
                match kvp.Key with
                | FantomasVersion.Bundled -> FantomasVersionSettings.Bundled
                | FantomasVersion.LocalDotnetTool -> FantomasVersionSettings.LocalDotnetTool
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

    let fantomasDetector =
        if isNull solution then FantomasDetector.Create(lifetime) else solution.GetComponent<FantomasDetector>()

    let formatSettingItem setting (settingsData: Dictionary<_, _>) =
        let { Version = fantomasVersion, version; Status = status} = settingsData[setting]
        let description =
            match setting with
            | FantomasVersionSettings.LocalDotnetTool -> "From dotnet-tools.json"
            | FantomasVersionSettings.GlobalDotnetTool -> "From .NET global tools"
            | FantomasVersionSettings.Bundled -> "Bundled"
            | _ -> "Auto detected"

        let version =
            match setting with
            | FantomasVersionSettings.AutoDetected ->
                match fantomasVersion with
                | FantomasVersion.LocalDotnetTool -> $" (v.{version} dotnet-tools.json)"
                | FantomasVersion.GlobalDotnetTool -> $" (v.{version} global)"
                | _ -> $" (Bundled v.{version})"
            | _ ->

            match status with
            | Ok -> $" (v.{version})"
            | FailedToRun -> $" (v.{version} failed to run)"
            | UnsupportedVersion -> $" (v.{version} not supported)"
            | SelectedButNotFound -> " (not found)"

        RichTextModel(List [| RichStringModel(description); RichStringModel(version, ThemeColorId(ColorId.Gray))|])
            .GetBeRichText() :> BeControl

    let validate (comboBox: BeComboBox) (settingsData: Dictionary<_, _>) =
        let settingName = settingsData.Keys |> Seq.sortBy id |> Seq.item comboBox.SelectedIndex.Value
        match settingsData[settingName] with
        | { Status = FailedToRun } ->
            ValidationResult(ValidationStates.validationWarning, "The specified Fantomas version failed to run.")
        | { Status = UnsupportedVersion } ->
            ValidationResult(ValidationStates.validationWarning, $"Supported Fantomas versions: {MinimalSupportedVersion} and later.")
        | { Status = SelectedButNotFound } ->
            ValidationResult(ValidationStates.validationWarning, "The specified Fantomas version not found.")
        | _ ->
            ValidationResult(ValidationStates.validationPassed)

    let createComboBox (key: JetBrains.DataFlow.IProperty<FantomasVersionSettings>) =
        let settingsData = fantomasDetector.GetSettings()
        let beComboBoxFromEnum =
            key.GetBeComboBoxFromEnum(lifetime,
                PresentComboItem (fun x y z -> formatSettingItem y settingsData),
                seq { FantomasVersionSettings.LocalDotnetTool
                      FantomasVersionSettings.GlobalDotnetTool } |> Seq.filter (not << settingsData.ContainsKey)
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
            .AddColumnElementsToNewRow(BeSizingType.Fit, false, [|"Fantomas version".GetBeLabel() :> BeControl; beComboBoxFromEnum.WithMinSize(BeControlSizeFixed(BeControlSizeType.FIT_TO_CONTENT, BeControlSizeType.FIT_TO_CONTENT), lifetime)|])
            .AddColumnElementsToNewRow(BeSizingType.Fit, false, [|"".GetBeLabel() :> BeControl; validationLabel |])

    do
        use indent = this.Indent()

        this.AddControl((fun (key: FSharpFantomasOptions) -> key.Version), createComboBox) |> ignore//.DisableDescendantsWhenDisabled(this.Lifetime)

        this.AddCommentText("To use a specified Fantomas version, install it globally via 'dotnet tool install fantomas-tool'\n" +
                            "or specify it in dotnet-tools.json file in the solution directory.")
        this.AddLinkButton("DotnetToolsLink", "Learn more", fun () -> uiApplication.OpenUri("https://docs.microsoft.com/en-us/dotnet/core/tools/dotnet-tool-install")) |> ignore


[<SolutionComponent>]
type FantomasNotificationsManager(lifetime, settings: FantomasDetector, notifications: UserNotifications,
                                  optionsManager: OptionsManager, dotnetToolsTracker: NuGetDotnetToolsTracker,
                                  uiApplication: IUIApplication) =
    let goToSettings = [| UserNotificationCommand("Settings", fun _ -> optionsManager.BeginShowOptions(nameof(FantomasPage))) |]
    let openDotnetToolsOrGoToSettings toolsManifestPath =
        [| UserNotificationCommand("Open dotnet-tools.json", fun _ -> uiApplication.OpenUri(toolsManifestPath))
           goToSettings[0] |]

    let createFallbackMessage = function
        | LocalDotnetTool -> ""
        | GlobalDotnetTool -> "<b>Falling back to the global dotnet tool Fantomas.</b>"
        | Bundled -> "<b>Falling back to the bundled formatter.</b>"

    let createBodyMessage { Event = event; Version = version; FallbackVersion = fallbackVersion } =
        let fallbackMessage = createFallbackMessage fallbackVersion

        match event with
        | SelectedButNotFound ->
            match version with
            | FantomasVersion.LocalDotnetTool ->
                $"""dotnet-tools.json file not found in the solution directory.<br>{fallbackMessage}"""
            | FantomasVersion.GlobalDotnetTool ->
                $"""Fantomas is not installed globally.<br>{fallbackMessage}"""
            | _ -> ""

        | FailedToRun ->
            match version with
            | FantomasVersion.LocalDotnetTool ->
                $"""Fantomas specified in 'dotnet-tool.json' failed to run.<br>{fallbackMessage}"""
            | FantomasVersion.GlobalDotnetTool ->
                $"""Fantomas installed globally via 'dotnet tool install fantomas-tool' failed to run.<br>{fallbackMessage}"""
            | _ -> ""

        | UnsupportedVersion ->
            match version with
            | FantomasVersion.LocalDotnetTool ->
                $"""Fantomas version specified in 'dotnet-tool.json' is not compatible with the current Rider version.<br>{fallbackMessage}<br>Supported formatter versions: {MinimalSupportedVersion} and later."""
            | FantomasVersion.GlobalDotnetTool ->
                $"""Fantomas installed globally via 'dotnet tool install fantomas-tool' is not compatible with the current Rider version.<br>{fallbackMessage}<br>Supported versions: {MinimalSupportedVersion} and later."""
            | _ -> ""
        | _ -> ""

    let getCommands = function
        | FantomasVersion.LocalDotnetTool ->
            let manifestPath = dotnetToolsTracker.GetSolutionManifestPath()
            if isNotNull manifestPath && manifestPath.ExistsFile then openDotnetToolsOrGoToSettings manifestPath.FullPath
            else goToSettings    
        | _ -> goToSettings

    let createNotification notification =
        let body = createBodyMessage notification
        let commands = getCommands notification.Version

        notifications.CreateNotification(lifetime,
            title = "Unable to use specified Fantomas version",
            body = body,
            additionalCommands = commands) |> ignore

    do settings.NotificationProducer.Advise(lifetime, createNotification)
