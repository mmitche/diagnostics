// Licensed to the .NET Foundation under one or more agreements.
// The .NET Foundation licenses this file to you under the MIT license.
// See the LICENSE file in the project root for more information.

using Microsoft.Diagnostics.DebugServices;
using Microsoft.Diagnostics.DebugServices.Implementation;
using Microsoft.Diagnostics.Runtime;
using Microsoft.Diagnostics.Runtime.Interop;
using Microsoft.Diagnostics.Runtime.Utilities;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace SOS.Extensions
{
    /// <summary>
    /// Module service implementation for the native debugger services
    /// </summary>
    internal class ModuleServiceFromDebuggerServices : ModuleService
    {
        class ModuleFromDebuggerServices : Module
        {
            private readonly ModuleServiceFromDebuggerServices _moduleService;
            private string _versionString;

            public ModuleFromDebuggerServices(
                ModuleServiceFromDebuggerServices moduleService,
                int moduleIndex,
                string imageName,
                ulong imageBase,
                ulong imageSize,
                int indexFileSize,
                int indexTimeStamp)
            {
                _moduleService = moduleService;
                ModuleIndex = moduleIndex;
                FileName = imageName;
                ImageBase = imageBase;
                ImageSize = imageSize;
                IndexFileSize = indexFileSize;
                IndexTimeStamp = indexTimeStamp;
            }

            #region IModule

            public override int ModuleIndex { get; }

            public override string FileName { get; }

            public override ulong ImageBase { get; }

            public override ulong ImageSize { get; }

            public override int IndexFileSize { get; }

            public override int IndexTimeStamp { get; }

            public override VersionInfo? Version
            {
                get
                {
                    if (InitializeValue(Module.Flags.InitializeVersion))
                    {
                        int hr = _moduleService._debuggerServices.GetModuleVersionInformation(ModuleIndex, out VS_FIXEDFILEINFO fileInfo);
                        if (hr == HResult.S_OK)
                        {
                            int major = (int)fileInfo.dwFileVersionMS >> 16;
                            int minor = (int)fileInfo.dwFileVersionMS & 0xffff;
                            int revision = (int)fileInfo.dwFileVersionLS >> 16;
                            int patch = (int)fileInfo.dwFileVersionLS & 0xffff;
                            base.Version = new VersionInfo(major, minor, revision, patch);
                        }
                        else
                        {
                            if (_moduleService.Target.OperatingSystem != OSPlatform.Windows)
                            {
                                GetVersionFromVersionString();
                            }
                        }
                    }
                    return base.Version;
                }
            }

            public override string VersionString
            {
                get
                {
                    if (InitializeValue(Module.Flags.InitializeProductVersion))
                    {
                        int hr = _moduleService._debuggerServices.GetModuleVersionString(ModuleIndex, out _versionString);
                        if (hr != HResult.S_OK)
                        {
                            if (_moduleService.Target.OperatingSystem != OSPlatform.Windows && !IsPEImage)
                            {
                                _versionString = _moduleService.GetVersionString(ImageBase, ImageSize);
                            }
                        }
                    }
                    return _versionString;
                }
            }

            #endregion

            protected override ModuleService ModuleService => _moduleService;
        }

        private readonly DebuggerServices _debuggerServices;

        internal ModuleServiceFromDebuggerServices(ITarget target, DebuggerServices debuggerServices)
            : base(target)
        {
            Debug.Assert(debuggerServices != null);
            _debuggerServices = debuggerServices;
        }

        /// <summary>
        /// Get/create the modules dictionary.
        /// </summary>
        protected override Dictionary<ulong, IModule> GetModulesInner()
        {
            var modules = new Dictionary<ulong, IModule>();

            HResult hr = _debuggerServices.GetNumberModules(out uint loadedModules, out uint unloadedModules);
            if (hr == HResult.S_OK)
            {
                for (int moduleIndex = 0; moduleIndex < loadedModules; moduleIndex++)
                {
                    hr = _debuggerServices.GetModuleInfo(moduleIndex, out ulong imageBase, out ulong imageSize, out uint timestamp, out uint checksum);
                    if (hr == HResult.S_OK)
                    {
                        hr = _debuggerServices.GetModuleName(moduleIndex, out string imageName);
                        if (hr < 0)
                        {
                            Trace.TraceError("GetModuleName({0}) {1:X16} FAILED {2:X8}", moduleIndex, imageBase, hr);
                        }
                        var module = new ModuleFromDebuggerServices(this, moduleIndex, imageName, imageBase, imageSize, unchecked((int)imageSize), unchecked((int)timestamp));
                        if (!modules.TryGetValue(imageBase, out IModule original))
                        {
                            modules.Add(imageBase, module);
                        }
                        else
                        {
                            Trace.TraceError("Duplicate imageBase {0:X16} new {1} original {2}", imageBase, imageName, original.FileName);
                        }
                    }
                    else
                    {
                        Trace.TraceError("GetModuleInfo({0}) FAILED {1:X8}", moduleIndex, hr);
                    }
                }
            }
            else
            {
                Trace.TraceError("GetNumberModules() FAILED {0:X8}", hr);
            }
            return modules;
        }
    }
}