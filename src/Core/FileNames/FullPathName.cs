﻿// Copyright 2013 The Chromium Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license that can be
// found in the LICENSE file.

using System;
using System.Collections.Generic;
using System.IO;

namespace VsChromium.Core.FileNames {
  /// <summary>
  /// Wraps a string representing the full path of a file or directory.
  /// </summary>
  public struct FullPathName : IEquatable<FullPathName>, IComparable<FullPathName> {
    private readonly string _path;

    public FullPathName(string path) {
      if (!PathHelpers.IsAbsolutePath(path))
        ThrowInvalidPath(path);
      _path = path;
    }

    private static void ThrowInvalidPath(string path) {
      throw new ArgumentException(string.Format("Path must be absolute: \"{0}\".", path), "path");
    }

    public FullPathName Parent {
      get {
        var parent = Directory.GetParent(_path);
        return parent == null ? default(FullPathName) : new FullPathName(parent.FullName);
      }
    }

    public string FullName { get { return _path; } }

    public string Name { get { return Path.GetFileName(_path); } }

    public FullPathName Combine(string name) {
      return new FullPathName(PathHelpers.PathCombine(_path, name));
    }

    public bool FileExists { get { return File.Exists(_path); } }

    public bool DirectoryExists { get { return Directory.Exists(_path); } }

    public static bool operator ==(FullPathName x, FullPathName y) {
      return x.Equals(y);
    }

    public static bool operator !=(FullPathName x, FullPathName y) {
      return !(x == y);
    }

    public bool Equals(FullPathName other) {
      return SystemPathComparer.Instance.Comparer.Equals(_path, other._path);
    }

    public int CompareTo(FullPathName other) {
      return SystemPathComparer.Instance.Comparer.Compare(_path, other._path);
    }

    public override string ToString() {
      return _path;
    }

    public override bool Equals(object obj) {
      if (obj is FullPathName)
        return Equals((FullPathName)obj);
      return false;
    }

    public override int GetHashCode() {
      return SystemPathComparer.Instance.Comparer.GetHashCode(_path);
    }

    public bool StartsWith(FullPathName x) {
      return _path.StartsWith(x._path, SystemPathComparer.Instance.Comparison);
    }

    public bool HasComponent(string component) {
      foreach (string currentComponent in _path.Split(Path.DirectorySeparatorChar)) {
        if (currentComponent.Equals(component, StringComparison.CurrentCultureIgnoreCase))
          return true;
      }
      return false;
    }

    public IEnumerable<FullPathName> EnumerateParents() {
      for (var parent = Parent; parent != default(FullPathName); parent = parent.Parent) {
        yield return parent;
      }
    }
  }
}
