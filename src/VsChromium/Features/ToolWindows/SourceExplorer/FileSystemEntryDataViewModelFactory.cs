﻿// Copyright 2013 The Chromium Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license that can be
// found in the LICENSE file.

using System.Collections.Generic;
using System.Linq;
using VsChromium.Core.Ipc.TypedMessages;
using VsChromium.Threads;
using VsChromium.Views;

namespace VsChromium.Features.ToolWindows.SourceExplorer {
  public static class FileSystemEntryDataViewModelFactory {
    public static IEnumerable<TreeViewItemViewModel> CreateViewModels(
      IUIRequestProcessor uiRequestProcessor,
      IStandarImageSourceFactory imageSourceFactory,
      TreeViewItemViewModel parent,
      FileSystemEntryData data) {
      var positionsData = data as FilePositionsData;
      if (positionsData != null)
        return positionsData.Positions.Select(
            x => new FilePositionViewModel(uiRequestProcessor, imageSourceFactory, parent, x));

      return Enumerable.Empty<TreeViewItemViewModel>();
    }
  }
}
