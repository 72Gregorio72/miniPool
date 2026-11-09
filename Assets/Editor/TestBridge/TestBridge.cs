using System;
using System.Collections.Generic;
using System.IO;
using System.Text;
using UnityEditor;
using UnityEditor.Compilation;
using UnityEditor.TestTools.TestRunner.Api;
using UnityEngine;

/// <summary>
/// Lets tools outside Unity run tests in the editor that is already open (the project lock forbids a
/// second, batchmode Unity). Protocol, driven by .claude/scripts/unity-bridge.sh:
/// write "test" or "smoke" to Temp/testbridge.request; the bridge refreshes assets (picks up edited .cs),
/// waits for compilation, then writes test-results/bridge.log (first line: PASS / FAIL / COMPILE_ERROR)
/// and, for "test", test-results/editmode.xml. Own assembly, so it still runs when game code fails to compile.
/// </summary>
[InitializeOnLoad]
static class TestBridge
{
    const string Request = "Temp/testbridge.request";
    const string OutDir = "test-results";
    const string Log = OutDir + "/bridge.log";
    const double CompileGrace = 2; // seconds after Refresh before trusting isCompiling == false

    static readonly List<string> compileErrors = new List<string>();
    static bool running;

    static TestBridge()
    {
        EditorApplication.update += Poll;
        CompilationPipeline.compilationStarted += _ => compileErrors.Clear();
        CompilationPipeline.assemblyCompilationFinished += (asm, messages) =>
        {
            foreach (var m in messages)
                if (m.type == CompilerMessageType.Error) compileErrors.Add(m.message);
        };
    }

    static void Poll()
    {
        if (running || !File.Exists(Request)) return;
        if (EditorApplication.isPlayingOrWillChangePlaymode || EditorApplication.isCompiling || EditorApplication.isUpdating) return;

        string[] parts = File.ReadAllText(Request).Trim().Split(' ');
        string mode = parts[0];
        if (parts.Length < 2)
        {
            // first step: import edited files; may recompile and reload the domain, the request file survives it
            File.WriteAllText(Request, mode + " " + DateTime.UtcNow.Ticks);
            AssetDatabase.Refresh();
            return;
        }
        if ((DateTime.UtcNow - new DateTime(long.Parse(parts[1]))).TotalSeconds < CompileGrace) return;

        File.Delete(Request);
        Directory.CreateDirectory(OutDir);
        if (EditorUtility.scriptCompilationFailed)
        {
            var sb = new StringBuilder("COMPILE_ERROR\n");
            if (compileErrors.Count == 0) sb.Append("(errors from an earlier compilation: see Logs/Editor.log, search 'error CS')\n");
            foreach (var e in compileErrors) sb.Append(e).Append('\n');
            Write(sb.ToString());
            return;
        }
        if (!mode.StartsWith("test")) { Write("PASS\ncompiled, no errors\n"); return; }

        running = true;
        var api = ScriptableObject.CreateInstance<TestRunnerApi>();
        api.RegisterCallbacks(new Callbacks(api));
        // "test:<regex>" runs only the tests whose full name matches
        var only = mode.StartsWith("test:") ? new[] { mode.Substring(5) } : null;
        api.Execute(new ExecutionSettings(new Filter { testMode = TestMode.EditMode, groupNames = only }));
    }

    static void Write(string text)
    {
        // write then move, so the shell side never reads a half-written log
        File.WriteAllText(Log + ".tmp", text);
        if (File.Exists(Log)) File.Delete(Log);
        File.Move(Log + ".tmp", Log);
    }

    class Callbacks : ICallbacks
    {
        readonly TestRunnerApi api;
        public Callbacks(TestRunnerApi api) { this.api = api; }

        public void RunStarted(ITestAdaptor tests) { }
        public void TestStarted(ITestAdaptor test) { }
        public void TestFinished(ITestResultAdaptor result) { }

        public void RunFinished(ITestResultAdaptor result)
        {
            TestRunnerApi.SaveResultToFile(result, OutDir + "/editmode.xml");
            var sb = new StringBuilder(result.FailCount == 0 && result.PassCount > 0 ? "PASS" : "FAIL");
            sb.Append($"\npassed={result.PassCount} failed={result.FailCount} skipped={result.SkipCount} inconclusive={result.InconclusiveCount}\n");
            if (result.PassCount + result.FailCount == 0) sb.Append("no tests ran\n");
            Failures(result, sb);
            Write(sb.ToString());
            api.UnregisterCallbacks(this);
            running = false;
        }

        static void Failures(ITestResultAdaptor r, StringBuilder sb)
        {
            if (!r.HasChildren && r.TestStatus == TestStatus.Failed)
                sb.Append($"- {r.FullName}: {r.Message}\n");
            foreach (var c in r.Children) Failures(c, sb);
        }
    }
}
