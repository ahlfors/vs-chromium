﻿// Copyright 2013 The Chromium Authors. All rights reserved.
// Use of this source code is governed by a BSD-style license that can be
// found in the LICENSE file.

using System;
using System.Collections.Generic;
using System.ComponentModel.Composition;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Threading;
using VsChromium.Core;
using VsChromium.Core.FileNames;
using VsChromium.Core.FileNames.PatternMatching;
using VsChromium.Core.Ipc.TypedMessages;
using VsChromium.Core.Linq;
using VsChromium.Server.FileSystem;
using VsChromium.Server.FileSystemNames;
using VsChromium.Server.ProgressTracking;
using VsChromium.Server.Projects;
using VsChromium.Server.Threads;
using VsChromium.Server.NativeInterop;

namespace VsChromium.Server.Search {
  [Export(typeof(ISearchEngine))]
  public class SearchEngine : ISearchEngine {
    private const int MinimumSearchPatternLength = 2;
    private readonly ICustomThreadPool _customThreadPool;
    private readonly IFileContentsFactory _fileContentsFactory;
    private readonly IFileSystemNameFactory _fileSystemNameFactory;
    private readonly object _lock = new Object();
    private readonly IOperationIdFactory _operationIdFactory;
    private readonly IProgressTrackerFactory _progressTrackerFactory;
    private readonly IProjectDiscovery _projectDiscovery;
    private readonly ISearchStringParser _searchStringParser;
    private readonly TaskCancellation _taskCancellation = new TaskCancellation();
    private volatile FileDatabase _currentState;

    [ImportingConstructor]
    public SearchEngine(
      IFileSystemProcessor fileSystemProcessor,
      IFileSystemNameFactory fileSystemNameFactory,
      ICustomThreadPool customThreadPool,
      IFileContentsFactory fileContentsFactory,
      IOperationIdFactory operationIdFactory,
      IProgressTrackerFactory progressTrackerFactory,
      IProjectDiscovery projectDiscovery,
      ISearchStringParser searchStringParser) {
      _fileSystemNameFactory = fileSystemNameFactory;
      _customThreadPool = customThreadPool;
      _fileContentsFactory = fileContentsFactory;
      _operationIdFactory = operationIdFactory;
      _progressTrackerFactory = progressTrackerFactory;
      _projectDiscovery = projectDiscovery;
      _searchStringParser = searchStringParser;

      // Create a "Null" state
      _currentState = new FileDatabase(_projectDiscovery, _fileSystemNameFactory,
                                       _fileContentsFactory, _progressTrackerFactory);
      _currentState.Freeze();

      // Setup computing a new state everytime a new tree is computed.
      fileSystemProcessor.TreeComputed += FileSystemProcessorOnTreeComputed;
      fileSystemProcessor.FilesChanged += FileSystemProcessorOnFilesChanged;
    }

    public SearchFileNamesResult SearchFileNames(SearchParams searchParams) {
      var matchFunction = SearchPreProcessParams<FileName>(searchParams, MatchFileName, MatchFileRelativePath);
      if (matchFunction == null)
        return SearchFileNamesResult.Empty;

      // taskCancellation is used to make sure we cancel previous tasks as fast as possible
      // to avoid using too many CPU resources if the caller keeps asking us to search for
      // things. Note that this assumes the caller is only interested in the result of
      // the *last* query, while the previous queries will throw an OperationCanceled exception.
      _taskCancellation.CancelAll();

      var matches = _currentState.FileNames
        .AsParallel()
        // We need the line below because of "Take" (.net 4.0 PLinq limitation)
        .WithExecutionMode(ParallelExecutionMode.ForceParallelism)
        .WithCancellation(_taskCancellation.GetNewToken())
        .Where(item => matchFunction(item))
        .Take(searchParams.MaxResults)
        .ToList();

      return new SearchFileNamesResult {
        FileNames = matches,
        TotalCount = _currentState.FileNames.Count
      };
    }

    public SearchDirectoryNamesResult SearchDirectoryNames(SearchParams searchParams) {
      var matchFunction = SearchPreProcessParams<DirectoryName>(searchParams, MatchDirectoryName,
                                                                MatchDirectoryRelativePath);
      if (matchFunction == null)
        return SearchDirectoryNamesResult.Empty;

      // taskCancellation is used to make sure we cancel previous tasks as fast as possible
      // to avoid using too many CPU resources if the caller keeps asking us to search for
      // things. Note that this assumes the caller is only interested in the result of
      // the *last* query, while the previous queries will throw an OperationCanceled exception.
      _taskCancellation.CancelAll();

      var matches = _currentState.DirectoryNames
        .AsParallel()
        // We need the line below because of "Take" (.net 4.0 PLinq limitation)
        .WithExecutionMode(ParallelExecutionMode.ForceParallelism)
        .WithCancellation(_taskCancellation.GetNewToken())
        .Where(item => matchFunction(item))
        .Take(searchParams.MaxResults)
        .ToList();

      return new SearchDirectoryNamesResult {
        DirectoryNames = matches,
        TotalCount = _currentState.DirectoryNames.Count
      };
    }

    public SearchFileContentsResult SearchFileContents(SearchParams searchParams) {
      using (var parsedSearchString = _searchStringParser.Parse(searchParams.SearchString)) {
        CreateSearchAlgorithms(parsedSearchString, searchParams.MatchCase);

        // Don't search empty or very small strings -- no significant results.
        if (string.IsNullOrWhiteSpace(parsedSearchString.MainEntry.Text) ||
            (parsedSearchString.MainEntry.Text.Length < MinimumSearchPatternLength)) {
          return SearchFileContentsResult.Empty;
        }

        // taskCancellation is used to make sure we cancel previous tasks as fast as possible
        // to avoid using too many CPU resources if the caller keeps asking us to search for
        // things. Note that this assumes the caller is only interested in the result of
        // the *last* query, while the previous queries will throw an OperationCanceled exception.
        _taskCancellation.CancelAll();
        var cancellationToken = _taskCancellation.GetNewToken();
        return DoSearchFileContents(parsedSearchString, searchParams.MaxResults, cancellationToken);
      }
    }

    private void CreateSearchAlgorithms(ParsedSearchString parsedSearchString, bool matchCase) {
      var searchOptions = matchCase ? NativeMethods.SearchOptions.kMatchCase : NativeMethods.SearchOptions.kNone;
      CreateSearchAlgorithms(parsedSearchString.MainEntry, searchOptions);
      parsedSearchString.EntriesBeforeMainEntry.ForAll(e => CreateSearchAlgorithms(e, searchOptions));
      parsedSearchString.EntriesAfterMainEntry.ForAll(e => CreateSearchAlgorithms(e, searchOptions));
    }

    private static void CreateSearchAlgorithms(ParsedSearchString.Entry entry, NativeMethods.SearchOptions searchOptions) {
      entry.AsciiStringSearchAlgo = AsciiFileContents.CreateSearchAlgo(entry.Text, searchOptions);
      entry.UTF16StringSearchAlgo = UTF16FileContents.CreateSearchAlgo(entry.Text, searchOptions);
    }

    private SearchFileContentsResult DoSearchFileContents(ParsedSearchString parsedSearchString, int maxResults, CancellationToken cancellationToken) {
      var searchInfo = new SearchContentsData {
        ParsedSearchString = parsedSearchString,
      };
      var taskResults = new TaskResultCounter(maxResults);
      var searchedFileCount = 0;
      var matches = _currentState.FilesWithContents
        .AsParallel()
        .WithExecutionMode(ParallelExecutionMode.ForceParallelism)
        .WithCancellation(cancellationToken)
        .Where(x => !taskResults.Done)
        .Select(item => {
          Interlocked.Increment(ref searchedFileCount);
          return MatchFileContents(item, searchInfo, taskResults);
        })
        .Where(r => r != null)
        .ToList();

      return new SearchFileContentsResult {
        Entries = matches,
        SearchedFileCount = searchedFileCount,
        TotalFileCount = _currentState.FilesWithContents.Count,
        HitCount = taskResults.Count,
      };
    }


    public IEnumerable<FileExtract> GetFileExtracts(string path, IEnumerable<FilePositionSpan> spans) {
      var filename = _fileSystemNameFactory.PathToFileName(_projectDiscovery, path);
      if (filename == null)
        return Enumerable.Empty<FileExtract>();

      FileData fileData;
      if (!_currentState.Files.TryGetValue(filename, out fileData))
        return Enumerable.Empty<FileExtract>();

      if (fileData.Contents == null)
        return Enumerable.Empty<FileExtract>();

      return fileData.Contents.GetFileExtracts(spans);
    }

    public event Action<long> FilesLoading;
    public event Action<long> FilesLoaded;

    private void FileSystemProcessorOnFilesChanged(IEnumerable<FileName> paths) {
      _customThreadPool.RunAsync(() => UpdateFileContents(paths));
    }

    private void UpdateFileContents(IEnumerable<FileName> paths) {
      var operationId = _operationIdFactory.GetNextId();
      OnFilesLoading(operationId);
      // Concurrency: We capture the current state reference locally.
      // We may update the FileContents value of some entries, but we
      // ensure we do not update collections and so on. So, all in all,
      // it is safe to make this change "lock free".
      var state = _currentState;
      paths
        .Where(x => _projectDiscovery.IsFileSearchable(x))
        .ForAll(x => {
          FileData fileData;
          if (state.Files.TryGetValue(x, out fileData)) {
            fileData.UpdateContents(_fileContentsFactory.GetFileContents(x.GetFullName()));
          }
        });
      OnFilesLoaded(operationId);
    }

    private void FileSystemProcessorOnTreeComputed(long operationId, FileSystemTree oldTree, FileSystemTree newTree) {
      _customThreadPool.RunAsync(() => ComputeNewState(newTree));
    }

    private Func<T, bool> SearchPreProcessParams<T>(
      SearchParams searchParams,
      Func<IPathMatcher, T, IPathComparer, bool> matchName,
      Func<IPathMatcher, T, IPathComparer, bool> matchRelativeName) where T : FileSystemName {
      var pattern = ConvertUserSearchStringToSearchPattern(searchParams);
      if (pattern == null)
        return null;

      var matcher = FileNameMatching.ParsePattern(pattern);

      var comparer = searchParams.MatchCase ?
                       CaseSensitivePathComparer.Instance :
                       CaseInsensitivePathComparer.Instance;
      if (pattern.Contains(Path.DirectorySeparatorChar))
        return (item) => matchRelativeName(matcher, item, comparer);
      else
        return (item) => matchName(matcher, item, comparer);
    }

    private static string ConvertUserSearchStringToSearchPattern(SearchParams searchParams) {
      var pattern = searchParams.SearchString;

      pattern = pattern.Trim();
      if (string.IsNullOrWhiteSpace(pattern))
        return null;

      // We use "\\" internally for paths and patterns.
      pattern = pattern.Replace(Path.AltDirectorySeparatorChar, Path.DirectorySeparatorChar);

      // Exception to ".gitignore" syntax: If the search string doesn't contain any special
      // character, surround the pattern with "*" so that we match sub-strings.
      // TODO(rpaquay): What about "."? Special or not?
      if (pattern.IndexOf(Path.DirectorySeparatorChar) < 0 &&
          pattern.IndexOf('*') < 0) {
        pattern = "*" + pattern + "*";
      }

      return pattern;
    }

    private void ComputeNewState(FileSystemTree newTree) {
      var operationId = _operationIdFactory.GetNextId();
      OnFilesLoading(operationId);

      Logger.Log("++++ Computing new state of file database from file system tree. ++++");
      var sw = Stopwatch.StartNew();

      var oldState = _currentState;
      var newState = new FileDatabase(_projectDiscovery, _fileSystemNameFactory,
                                      _fileContentsFactory, _progressTrackerFactory);
      newState.ComputeState(newTree, oldState);

      sw.Stop();
      Logger.Log("++++ Done computing new state of file database from file system tree in {0:n0} msec. ++++",
                 sw.ElapsedMilliseconds);
      Logger.LogMemoryStats();

      // Swap states
      lock (_lock) {
        _currentState = newState;
      }

      OnFilesLoaded(operationId);
    }

    private bool MatchFileName(IPathMatcher matcher, FileName fileName, IPathComparer comparer) {
      return matcher.MatchFileName(fileName.Name, comparer);
    }

    private bool MatchFileRelativePath(IPathMatcher matcher, FileName fileName, IPathComparer comparer) {
      return matcher.MatchFileName(fileName.RelativePathName.RelativeName, comparer);
    }

    private bool MatchDirectoryName(IPathMatcher matcher, DirectoryName directoryName, IPathComparer comparer) {
      // "Chromium" root directories make it through here, skip them.
      if (directoryName.IsAbsoluteName)
        return false;

      return matcher.MatchDirectoryName(directoryName.RelativePathName.Name, comparer);
    }

    private bool MatchDirectoryRelativePath(IPathMatcher matcher, DirectoryName directoryName, IPathComparer comparer) {
      // "Chromium" root directories make it through here, skip them.
      if (directoryName.IsAbsoluteName)
        return false;

      return matcher.MatchDirectoryName(directoryName.RelativePathName.RelativeName, comparer);
    }

    private static FileSearchResult MatchFileContents(FileData fileData, SearchContentsData searchContentsData, TaskResultCounter taskResultCounter) {
      var spans = fileData.Contents.Search(searchContentsData);
      if (spans.Count == 0)
        return null;

      taskResultCounter.Add(spans.Count);

      return new FileSearchResult {
        FileName = fileData.FileName,
        Spans = spans
      };
    }

    protected virtual void OnFilesLoading(long operationId) {
      var handler = FilesLoading;
      if (handler != null)
        handler(operationId);
    }

    protected virtual void OnFilesLoaded(long operationId) {
      var handler = FilesLoaded;
      if (handler != null)
        handler(operationId);
    }
  }
}
