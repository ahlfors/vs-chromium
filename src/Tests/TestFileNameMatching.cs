﻿// Copyright 2013 The Chromium Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license that can be
// found in the LICENSE file.

using System.Diagnostics;
using System.IO;
using System.Linq;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using VsChromium.Core.FileNames;
using VsChromium.Core.FileNames.PatternMatching;

namespace VsChromium.Tests {
  [TestClass]
  public class TestFileNameMatching {
    [TestMethod]
    public void MatchFileNameNameWorks() {
      object[,] expectedResults = {
        // Pattern, Path, Expected Result
        {"*", "a/b/foo.bar", true},
        {"*.*", "a/b/foo.bar", true},
        {"*.*", "a/b/foobar", false},
        {"*.so.*", "a/b/foobar.so.txt", true},
        {"*.so.*", "a/b/foobar.so", false},
        {"**.so.**", "a/b/foobar.so", false},
        {"foo*bar", "a/b/foo.bar", true},
        {"foo*bar", "a/b/foobar", true},
        {"foo*bar", "a/b/fo.bar", false},
        {"foo*bar", "a/b/foo.ar", false},
        {"bar", "bar2", false},
        {"bar", "foo/bar", true},
        {"bar", "foo/bar2", false},
        {"bar", "foo/bar/blah", true}, // Weird, but correct: path is a file, and it contains "bar".
        {"bar", "test/foo/bar", true},
        {"bar", "test/foo/bar/blah", true}, // Weird, but correct: path is a file, and it contains "bar".

        {"foo/bar", "foo/bar", true},
        {"foo/bar", "foo/bar2", false},
        {"foo/bar", "foo/bar/blah", true},
        {"foo/bar", "test/foo/bar", true},
        {"foo/bar", "test/foo/bar/blah", true},
        {"foo/bar/", "foo/bar", false},
        {"foo/bar/", "foo/bar2", false},
        {"foo/bar/", "foo/bar/blah", true},
        {"foo/bar/", "test/foo/bar", false},
        {"foo/bar/", "test/foo/bar/blah", true},
        {"/foo/bar", "foo/bar", true},
        {"/foo/bar", "foo/bar2", false},
        {"/foo/bar", "foo/bar/blah", true},
        {"/foo/bar", "test/foo/bar", false},
        {"/foo/bar", "test/foo/bar/blah", false},
        {"/foo/bar/", "foo/bar", false},
        {"/foo/bar/", "foo/bar2", false},
        {"/foo/bar/", "foo/bar/blah", true},
        {"/foo/bar/", "test/foo/bar", false},
        {"/foo/bar/", "test/foo/bar/blah", false},
        {"foo/*bar", "foo/blah/bar", false},
        {"foo/**/bar", "foo/blah/bar", true},
        {"foo/**/bar", "foo/blah/boo/bar", true},
        {"foo/**/bar", "foo/blah/boobar", false},
        // any other form of "**" means "double asterisk" which means "asterisk" really.
        {"foo/**bar", "foo/blahbar", true},
        {"foo/**bar", "foo/blah/blahbar", false},
        {"foo/*bar*", "foo/blah/bar", false},
        {"foo/**/bar*", "foo/blah/bar", true},
        {"foo/**/bar*", "foo/blah/boo/bar", true},
        {"foo/**/bar*", "foo/blah/boobar", false},
        // any other form of "**" means "double asterisk" which means "asterisk" really.
        {"foo/**bar*", "foo/blahbar", true},
        {"foo/**bar*", "foo/blah/blahbar", false},
        {"foo/*bar.*", "foo/blah/bar.cs", false},
        {"foo/**/bar.*", "foo/blah/bar.cs", true},
        {"foo/**/bar.*", "foo/blah/boo/bar.cs", true},
        {"foo/**/bar.*", "foo/blah/boobar.cs", false},
        // any other form of "**" means "double asterisk" which means "asterisk" really.
        {"foo/**bar.*", "foo/blahbar.cs", true},
        {"foo/**bar.*", "foo/blah/blahbar.cs", false},
      };

      AssertMatch(MatchKind.File, expectedResults);
    }

    [TestMethod]
    public void MatchDirectoryNameNameWorks() {
      object[,] expectedResults = {
        // Pattern, Path, Expected Result
        {"foo/**/bar", "bar/test/foo", false},
        {"foo/bar", "foo/bar", true},
        {"foo/bar", "foo/bar2", false},
        {"foo/bar", "foo/bar/blah", true},
        {"foo/bar", "test/foo/bar", true},
        {"foo/bar", "test/foo/bar/blah", true},
        {"/foo/bar", "foo/bar", true},
        {"/foo/bar", "foo/bar2", false},
        {"/foo/bar", "foo/bar/blah", true},
        {"/foo/bar", "test/foo/bar", false},
        {"/foo/bar", "test/foo/bar/blah", false},
        {"/foo/bar/", "foo/bar", true},
        {"/foo/bar/", "foo/bar2", false},
        {"/foo/bar/", "foo/bar/blah", true},
        {"/foo/bar/", "test/foo/bar", false},
        {"/foo/bar/", "test/foo/bar/blah", false},
        {"foo/*bar", "foo/blah/bar", false},

        // **/ has special meaning
        {"**/bar", "bar", true},
        {"**/bar", "blah/bar", true},
        {"**/bar", "foo/blah/bar", true},
        // /**/ has special meaning
        {"foo/**/bar", "foo/blah/bar", true},
        {"foo/**/bar", "foo/blah/boo/bar", true},
        {"foo/**/bar", "foo/blah/boobar", false},

        // any other form of "**" means "double asterisk" which means "asterisk" really.
        {"foo/**bar", "foo/blahbar", true},
        {"foo/**bar", "foo/blah/blahbar", false},
      };

      AssertMatch(MatchKind.Directory, expectedResults);
    }

    private static void AssertMatch(MatchKind kind, object[,] expectedResults) {
      AssertMatch(kind, expectedResults, false);
      AssertMatch(kind, expectedResults, true);
    }

    private static void AssertMatch(MatchKind kind, object[,] expectedResults, bool optimize) {
      Debug.WriteLine(string.Format("===================================================================="));
      Debug.WriteLine(string.Format("Verifying expected result for {0} entries with optimization={1}.",
                                    expectedResults.GetLength(0), optimize));
      Debug.WriteLine(string.Format("===================================================================="));
      for (var i = 0; i < expectedResults.GetLength(0); i++) {
        var pattern = (string)expectedResults[i, 0];
        var path = (string)expectedResults[i, 1];
        var result = (bool)expectedResults[i, 2];
        Debug.WriteLine(string.Format("Matching \"{0}\" pattern \"{1}\" against path \"{2}\" should return {3}.", kind,
                                      pattern, path, result));

        pattern = pattern.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);
        path = path.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

        IPathMatcher matcher;
        if (optimize) {
          matcher = new AggregatePathMatcher(Enumerable.Repeat(PatternParser.ParsePattern(pattern), 1));
        } else {
          matcher = PatternParser.ParsePattern(pattern);
        }

        if (kind == MatchKind.Directory)
          Assert.AreEqual(result, matcher.MatchDirectoryName(path, SystemPathComparer.Instance));
        else
          Assert.AreEqual(result, matcher.MatchFileName(path, SystemPathComparer.Instance));
      }
    }

    [TestMethod]
    public void StringCompareWorks() {
      Assert.IsTrue(string.Compare("foo", 0, "foo", 0, 3, SystemPathComparer.Instance.Comparison) == 0);
      Assert.IsTrue(string.Compare("foo", 0, "barfoo", 3, 3, SystemPathComparer.Instance.Comparison) == 0);
      Assert.IsTrue(string.Compare("foo", 0, "fo", 0, 3, SystemPathComparer.Instance.Comparison) != 0);
      Assert.IsTrue(string.Compare("fo", 0, "foo", 0, 2, SystemPathComparer.Instance.Comparison) == 0);
    }
  }
}
