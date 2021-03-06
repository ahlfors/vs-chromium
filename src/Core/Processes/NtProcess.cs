﻿// Copyright 2013 The Chromium Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license that can be
// found in the LICENSE file.

using System;
using System.Text;
using VsChromium.Core.Utility;
using VsChromium.Core.Win32;
using VsChromium.Core.Win32.Processes;

namespace VsChromium.Core.Processes {
  public class NtProcess {
    public NtProcess(int pid) {
      _processId = pid;
      _isBeingDebugged = false;
      _machineType = MachineType.Unknown;
      _commandLine = null;
      _nativeProcessImagePath = null;
      _win32ProcessImagePath = null;
      _isValid = false;

      LoadProcessInfo();
    }

    private void LoadProcessInfo() {
      ProcessAccessFlags flags;
      try {
        using (SafeProcessHandle handle = OpenProcessHandle(out flags)) {
          if (handle.IsInvalid)
            return;

          _nativeProcessImagePath = QueryProcessImageName(handle, ProcessQueryImageNameMode.NativeSystemFormat);
          _win32ProcessImagePath = QueryProcessImageName(handle, ProcessQueryImageNameMode.Win32);

          // If our extension is running in a 32-bit process (which it is), then attempts to access
          // files in C:\windows\system (and a few other files) will redirect to C:\Windows\SysWOW64
          // and we will mistakenly think that the image file is a 32-bit image.  The way around this
          // is to use a native system format path, of the form:
          //    \\?\GLOBALROOT\Device\HarddiskVolume0\Windows\System\foo.dat
          // NativeProcessImagePath gives us the full process image path in the desired format.
          _machineType = ProcessUtility.GetMachineType(NativeProcessImagePath);

          // If we're not on a 32-bit machine, we can't use ReadProcessMemory, so this is as much as
          // we can do.
          if (_machineType != MachineType.X86)
            return;

          ProcessBasicInformation basicInfo = new ProcessBasicInformation();
          int size;
          int status = NativeMethods.NtQueryInformationProcess(
              handle,
              ProcessInfoClass.BasicInformation,
              ref basicInfo,
              MarshalUtility.UnmanagedStructSize<ProcessBasicInformation>(),
              out size);
          _parentProcessId = basicInfo.ParentProcessId.ToInt32();

          // If we can't load the ProcessBasicInfo, then we can't really do anything.
          if (status != NtStatus.Success || basicInfo.PebBaseAddress == IntPtr.Zero)
            return;

          if (flags.HasFlag(ProcessAccessFlags.VmRead)) {
            // Follows a pointer from the PROCESS_BASIC_INFORMATION structure in the target process's
            // address space to read the PEB.
            Peb peb = MarshalUtility.ReadUnmanagedStructFromProcess<Peb>(
                handle,
                basicInfo.PebBaseAddress);

            _isBeingDebugged = peb.IsBeingDebugged;

            if (peb.ProcessParameters != IntPtr.Zero) {
              // Follows a pointer from the PEB structure in the target process's address space to read
              // the RTL_USER_PROCESS_PARAMS.
              RtlUserProcessParameters processParameters = new RtlUserProcessParameters();
              processParameters = MarshalUtility.ReadUnmanagedStructFromProcess<RtlUserProcessParameters>(
                  handle,
                  peb.ProcessParameters);

              _commandLine = MarshalUtility.ReadStringUniFromProcess(
                  handle,
                  processParameters.CommandLine.Buffer,
                  processParameters.CommandLine.Length / 2);
            }
          }
        }
        _isValid = true;
      } catch (Exception) {
        _isValid = false;
      }
    }

    private SafeProcessHandle OpenProcessHandle(out ProcessAccessFlags flags) {
      // Try to open a handle to the process with the highest level of privilege, but if we can't
      // do that then fallback to requesting access with a lower privilege level.
      flags = ProcessAccessFlags.QueryInformation | ProcessAccessFlags.VmRead;
      SafeProcessHandle handle;
      handle = NativeMethods.OpenProcess(flags, false, _processId);
      if (!handle.IsInvalid)
        return handle;

      flags = ProcessAccessFlags.QueryLimitedInformation;
      handle = NativeMethods.OpenProcess(flags, false, _processId);
      if (handle.IsInvalid)
        flags = ProcessAccessFlags.None;
      return handle;
    }

    public MachineType MachineType {
      get { return _machineType; }
    }

    public int ProcessId {
      get { return _processId; }
    }

    public int ParentProcessId {
      get { return _parentProcessId; }
    }

    public string NativeProcessImagePath {
      get { return _nativeProcessImagePath; }
    }

    public string Win32ProcessImagePath {
      get { return _win32ProcessImagePath; }
    }

    public string CommandLine {
      get { return _commandLine; }
    }

    public bool IsBeingDebugged {
      get { return _isBeingDebugged; }
    }

    public bool IsValid {
      get { return _isValid; }
    }

    private string QueryProcessImageName(SafeProcessHandle handle, ProcessQueryImageNameMode mode) {
      StringBuilder moduleBuffer = new StringBuilder(1024);
      int size = moduleBuffer.Capacity;
      NativeMethods.QueryFullProcessImageName(
        handle, mode, moduleBuffer, ref size);
      if (mode == ProcessQueryImageNameMode.NativeSystemFormat)
        moduleBuffer.Insert(0, "\\\\?\\GLOBALROOT");
      return moduleBuffer.ToString();
    }

    private readonly int _processId;
    private bool _isValid;
    private bool _isBeingDebugged;
    private int _parentProcessId;
    private string _nativeProcessImagePath;
    private string _win32ProcessImagePath;
    private MachineType _machineType;
    private string _commandLine;
  }
}
