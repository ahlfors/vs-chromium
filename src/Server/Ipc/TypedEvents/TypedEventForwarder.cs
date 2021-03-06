﻿// Copyright 2013 The Chromium Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license that can be
// found in the LICENSE file.

using System.ComponentModel.Composition;
using VsChromium.Core.Ipc.TypedMessages;
using VsChromium.Server.FileSystem;
using VsChromium.Server.FileSystemSnapshot;
using VsChromium.Server.Search;

namespace VsChromium.Server.Ipc.TypedEvents {
  [Export(typeof(ITypedEventForwarder))]
  public class TypedEventForwarder : ITypedEventForwarder {
    private readonly IFileSystemProcessor _fileSystemProcessor;
    private readonly ISearchEngine _searchEngine;
    private readonly ITypedEventSender _typedEventSender;

    [ImportingConstructor]
    public TypedEventForwarder(
      ITypedEventSender typedEventSender,
      IFileSystemProcessor fileSystemProcessor,
      ISearchEngine searchEngine) {
      _typedEventSender = typedEventSender;
      _fileSystemProcessor = fileSystemProcessor;
      _searchEngine = searchEngine;
    }

    public void RegisterEventHandlers() {
      _fileSystemProcessor.SnapshotComputing += FileSystemProcessorOnSnapshotComputing;
      _fileSystemProcessor.SnapshotComputed += FileSystemProcessorOnSnapshotComputed;

      _searchEngine.FilesLoading += SearchEngineOnFilesLoading;
      _searchEngine.FilesLoaded += SearchEngineOnFilesLoaded;
    }

    private void FileSystemProcessorOnSnapshotComputing(long operationId) {
      _typedEventSender.SendEventAsync(new FileSystemTreeComputing {
        OperationId = operationId
      });
    }

    private void FileSystemProcessorOnSnapshotComputed(long operationId, FileSystemTreeSnapshot previousSnapshot, FileSystemTreeSnapshot newSnapshot) {
      _typedEventSender.SendEventAsync(new FileSystemTreeComputed {
        OperationId = operationId,
        OldVersion = previousSnapshot.Version,
        NewVersion = newSnapshot.Version
      });
    }

    private void SearchEngineOnFilesLoading(long operationId) {
      _typedEventSender.SendEventAsync(new SearchEngineFilesLoading {
        OperationId = operationId
      });
    }

    private void SearchEngineOnFilesLoaded(long operationId) {
      _typedEventSender.SendEventAsync(new SearchEngineFilesLoaded {
        OperationId = operationId
      });
    }
  }
}
