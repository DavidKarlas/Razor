﻿// Copyright (c) .NET Foundation. All rights reserved.
// Licensed under the Apache License, Version 2.0. See License.txt in the project root for license information.

using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using Microsoft.AspNetCore.Razor.Tools;
using Microsoft.Build.Framework;
using Microsoft.Build.Utilities;
using Microsoft.Extensions.CommandLineUtils;

namespace Microsoft.AspNetCore.Razor.Tasks
{
    public abstract class DotNetToolTask : ToolTask
    {
        private CancellationTokenSource _razorServerCts;

        public bool Debug { get; set; }

        public bool DebugTool { get; set; }

        [Required]
        public string ToolAssembly { get; set; }

        public bool UseServer { get; set; }

        // Specifies whether we should fallback to in-process execution if server execution fails.
        public bool ForceServer { get; set; }

        public string PipeName { get; set; }

        protected override string ToolName => "dotnet";

        // If we're debugging then make all of the stdout gets logged in MSBuild
        protected override MessageImportance StandardOutputLoggingImportance => DebugTool ? MessageImportance.High : base.StandardOutputLoggingImportance;

        protected override MessageImportance StandardErrorLoggingImportance => MessageImportance.High;

        internal abstract string Command { get; }

        protected override string GenerateFullPathToTool()
        {
#if NETSTANDARD2_0
            if (!string.IsNullOrEmpty(DotNetMuxer.MuxerPath))
            {
                return DotNetMuxer.MuxerPath;
            }
#endif

            // use PATH to find dotnet
            return ToolExe;
        }

        protected override string GenerateCommandLineCommands()
        {
            return $"exec \"{ToolAssembly}\"" + (DebugTool ? " --debug" : "");
        }

        protected override string GetResponseFileSwitch(string responseFilePath)
        {
            return "@\"" + responseFilePath + "\"";
        }

        protected abstract override string GenerateResponseFileCommands();

        public override bool Execute()
        {
            if (Debug)
            {
                Log.LogMessage(MessageImportance.High, "Waiting for debugger in pid: {0}", Process.GetCurrentProcess().Id);
                while (!Debugger.IsAttached)
                {
                    Thread.Sleep(TimeSpan.FromSeconds(3));
                }
            }

            return base.Execute();
        }

        protected override int ExecuteTool(string pathToTool, string responseFileCommands, string commandLineCommands)
        {
            if (UseServer &&
                TryExecuteOnServer(pathToTool, responseFileCommands, commandLineCommands, out var result))
            {
                return result;
            }

            return base.ExecuteTool(pathToTool, responseFileCommands, commandLineCommands);
        }

        protected override void LogToolCommand(string message)
        {
            if (Debug)
            {
                Log.LogMessage(MessageImportance.High, message);
            }
            else
            {
                base.LogToolCommand(message);
            }
        }

        public override void Cancel()
        {
            base.Cancel();

            _razorServerCts?.Cancel();
        }

        protected virtual bool TryExecuteOnServer(
            string pathToTool,
            string responseFileCommands,
            string commandLineCommands,
            out int result)
        {
            Log.LogMessage(StandardOutputLoggingImportance, "Server execution started.");
            using (_razorServerCts = new CancellationTokenSource())
            {
                Log.LogMessage(StandardOutputLoggingImportance, $"CommandLine = '{commandLineCommands}'");
                Log.LogMessage(StandardOutputLoggingImportance, $"ServerResponseFile = '{responseFileCommands}'");

                // The server contains the tools for discovering tag helpers and generating Razor code.
                var clientDir = Path.GetDirectoryName(ToolAssembly);
                var workingDir = CurrentDirectoryToUse();
                var tempDir = ServerConnection.GetTempPath(workingDir);
                var serverPaths = new ServerPaths(
                    clientDir,
                    workingDir: workingDir,
                    tempDir: tempDir);

                var arguments = GetArguments(responseFileCommands);

                var responseTask = ServerConnection.RunOnServer(PipeName, arguments, serverPaths, _razorServerCts.Token, debug: DebugTool);
                responseTask.Wait(_razorServerCts.Token);

                var response = responseTask.Result;
                if (response.Type == ServerResponse.ResponseType.Completed &&
                    response is CompletedServerResponse completedResponse)
                {
                    result = completedResponse.ReturnCode;

                    if (result == 0)
                    {
                        Log.LogMessage(StandardOutputLoggingImportance, $"Server execution completed with return code {result}.");
                        return true;
                    }
                    else
                    {
                        Log.LogMessage(
                            StandardOutputLoggingImportance,
                            $"Server execution completed with return code {result}. For more info, check the server log file in the location specified by the RAZORBUILDSERVER_LOG environment variable.");
                    }
                }
                else
                {
                    Log.LogMessage(
                        StandardOutputLoggingImportance,
                        $"Server execution failed with response {response.Type}. For more info, check the server log file in the location specified by the RAZORBUILDSERVER_LOG environment variable.");
                }

                result = -1;

                if (ForceServer)
                {
                    // We don't want to fallback to in-process execution.
                    return true;
                }

                Log.LogMessage(StandardOutputLoggingImportance, "Fallback to in-process execution.");
            }

            return false;
        }

        /// <summary>
        /// Get the current directory that the compiler should run in.
        /// </summary>
        private string CurrentDirectoryToUse()
        {
            // ToolTask has a method for this. But it may return null. Use the process directory
            // if ToolTask didn't override. MSBuild uses the process directory.
            var workingDirectory = GetWorkingDirectory();
            if (string.IsNullOrEmpty(workingDirectory))
            {
                workingDirectory = Directory.GetCurrentDirectory();
            }
            return workingDirectory;
        }

        private IList<string> GetArguments(string responseFileCommands)
        {
            var list = responseFileCommands.Split(new[] { Environment.NewLine }, StringSplitOptions.RemoveEmptyEntries);
            return list;
        }

        protected override bool HandleTaskExecutionErrors()
        {
            if (!HasLoggedErrors)
            {
                var toolCommand = Path.GetFileNameWithoutExtension(ToolAssembly) + " " + Command;
                // Show a slightly better error than the standard ToolTask message that says "dotnet" failed.
                Log.LogError($"{toolCommand} exited with code {ExitCode}.");
                return false;
            }

            return base.HandleTaskExecutionErrors();
        }
    }
}
