﻿// Copyright 2013 The Chromium Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license that can be
// found in the LICENSE file.

using System.IO;
using VsChromium.Core.Ipc.TypedMessages;
using VsChromium.Threads;
using VsChromium.Views;

namespace VsChromium.Features.ToolWindows.SourceExplorer {
  public abstract class FileSystemEntryViewModel : SourceExplorerItemViewModelBase {
    protected FileSystemEntryViewModel(
        IUIRequestProcessor uiRequestProcessor,
        IStandarImageSourceFactory imageSourceFactory,
        TreeViewItemViewModel parentViewModel,
        bool lazyLoadChildren)
      : base(uiRequestProcessor, imageSourceFactory, parentViewModel, lazyLoadChildren) {
    }

    public abstract FileSystemEntry FileSystemEntry { get; }

    public string Name { get { return FileSystemEntry.Name; } }

    public virtual string DisplayText {
      get {
        return this.Name;
      }
    }

    public static FileSystemEntryViewModel Create(
        IUIRequestProcessor uiRequestProcessor,
        IStandarImageSourceFactory imageSourceFactory,
        TreeViewItemViewModel parentViewModel,
        FileSystemEntry fileSystemEntry) {
      var fileEntry = fileSystemEntry as FileEntry;
      if (fileEntry != null)
        return new FileEntryViewModel(uiRequestProcessor, imageSourceFactory, parentViewModel, fileEntry);
      else
        return new DirectoryEntryViewModel(uiRequestProcessor, imageSourceFactory, parentViewModel, (DirectoryEntry)fileSystemEntry);
    }

    public string GetPath() {
      var parent = ParentViewModel as FileSystemEntryViewModel;
      if (parent == null)
        return Name;
      return Path.Combine(parent.GetPath(), Name);
    }
  }
}
