package com.jetbrains.rider.plugins.fsharp.test

import com.intellij.execution.process.impl.ProcessListUtil
import com.intellij.openapi.project.Project
import com.jetbrains.rdclient.protocol.protocolHost
import com.jetbrains.rider.inTests.TestHost
import com.jetbrains.rider.plugins.fsharp.rdFSharpModel
import com.jetbrains.rider.projectView.solution
import com.jetbrains.rider.test.base.BaseTestWithSolution
import com.jetbrains.rider.test.framework.frameworkLogger
import com.jetbrains.rider.test.scriptingApi.dumpSevereHighlighters
import java.io.PrintStream

fun com.intellij.openapi.editor.Editor.dumpTypeProviders(stream: PrintStream) {
    with(stream) {
        println((project ?: return).solution.rdFSharpModel.fsharpTestHost.dumpTypeProvidersProcess.sync(Unit))
        println("\nSevereHighlighters:")
        dumpSevereHighlighters(this)
    }
}

fun withSetting(project: Project, setting: String, function: () -> Unit) {
    TestHost.getInstance(project.protocolHost).setSetting(setting, "true")
    try {
        function()
    } finally {
        TestHost.getInstance(project.protocolHost).setSetting(setting, "false")
    }
}

fun BaseTestWithSolution.withOutOfProcessTypeProviders(function: () -> Unit) {
    withSetting(project, "FSharp/FSharpOptions/FSharpExperimentalFeatures/OutOfProcessTypeProviders/@EntryValue") {
        try {
            function()
        } finally {
            val tpProcessCount = ProcessListUtil
                    .getProcessList()
                    .count { it.executableName.startsWith("JetBrains.ReSharper.Plugins.FSharp.TypeProviders.Host") }
            if (tpProcessCount != 1) frameworkLogger.warn("Expected single type providers process, but was $tpProcessCount")
        }
    }
}

fun withEditorConfig(project: Project, function: () -> Unit) {
    withSetting(project, "CodeStyle/EditorConfig/EnableEditorConfigSupport", function)
}

fun withCultureInfo(project: Project, culture: String, function: () -> Unit) {
    val getCultureInfoAndSetNew = project.fcsHost.getCultureInfoAndSetNew
    val oldCulture = getCultureInfoAndSetNew.sync(culture)
    try {
        function()
    } finally {
        getCultureInfoAndSetNew.sync(oldCulture)
    }
}

val Project.fcsHost get() = this.solution.rdFSharpModel.fsharpTestHost
