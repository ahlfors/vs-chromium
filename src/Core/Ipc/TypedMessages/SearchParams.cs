﻿// Copyright 2013 The Chromium Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license that can be
// found in the LICENSE file.

using ProtoBuf;

namespace VsChromium.Core.Ipc.TypedMessages {
  [ProtoContract]
  public class SearchParams {
    public SearchParams() {
      MaxResults = int.MaxValue;
    }

    [ProtoMember(1)]
    public string SearchString { get; set; }

    [ProtoMember(2)]
    public int MaxResults { get; set; }

    [ProtoMember(3)]
    public bool MatchCase { get; set; }
  }
}
