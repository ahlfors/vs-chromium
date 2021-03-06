﻿// Copyright 2013 The Chromium Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license that can be
// found in the LICENSE file.

using System;
using System.Collections.Generic;
using VsChromium.Core.Ipc.TypedMessages;
using VsChromium.Server.Search;

namespace VsChromium.Server.FileSystemContents {
  public class StringFileContents : FileContents {
    private static readonly StringFileContents _empty = new StringFileContents("");
    private readonly string _text;

    public StringFileContents(string text)
      : base(DateTime.MinValue) {
      _text = text;
    }

    public override long ByteLength { get { return _text.Length * 2; } }

    public static StringFileContents Empty { get { return _empty; } }

    public override List<FilePositionSpan> Search(SearchContentsData searchContentsData) {
      if (ReferenceEquals(this, _empty))
        return NoSpans;
      // TODO(rpaquay): Maybe we will need this someday. For now, we use this class only for empty file content placeholder.
      throw new NotImplementedException();
#if false
      Logger.Log("Searching file contents");
      List<FilePositionSpan> result = null;
      var index = 0;
      while (true) {
        var newIndex = _text.IndexOf(searchContentsData.Text, index, StringComparison.Ordinal);
        if (newIndex < 0)
          break;

        if (result == null) {
          result = new List<FilePositionSpan>();
        }
        result.Add(newIndex);
        index = newIndex + searchContentsData.Text.Length;
      }
      return result ?? NoPositions;
#endif
    }
  }
}
