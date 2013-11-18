﻿using System.Linq;
using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Threading;
using System.Timers;
using System.Windows.Forms;

namespace HgLib
{
    public class HgRepository : IDisposable
    {
        private const int UpdateInterval = 2000;
        private const int FullUpdateDirtyFilesLimit = 200;

        private int running;
        private HgCommandQueue commands;
        private DirectoryWatcherMap directoryWatchers;
        private HgFileInfoDictionary cache;
        private Dictionary<string, string> rootBranchDictionary;

        private bool updateRequired;
        
        private System.Timers.Timer updateTimer;


        public bool IsEmpty
        {
            get { return directoryWatchers.Count == 0; }
        }

        public HgFileInfo[] PendingFiles
        {
            get { return cache.PendingFiles; }
        }
        
        public bool FileSystemWatch
        {
            set { directoryWatchers.FileSystemWatch = value; }
        }


        public event EventHandler StatusChanged = (s, e) => { };


        public HgRepository()
        {
            Initialize();

            updateTimer.Start();
        }

        private void Initialize()
        {
            cache = new HgFileInfoDictionary();
            directoryWatchers = new DirectoryWatcherMap();
            commands = new HgCommandQueue();
            rootBranchDictionary = new Dictionary<string, string>();
            
            updateTimer = new System.Timers.Timer
            { 
                AutoReset = false,
                Interval = 100,
            };

            updateTimer.Elapsed += OnTimerElapsed;
        }

        
        public void Dispose()
        {
            updateTimer.Dispose();
            directoryWatchers.Dispose();
        }


        public void Enqueue(HgCommand command)
        {
            lock (commands)
            {
                commands.Enqueue(command);
            }
        }


        internal void AddFiles(string[] fileNames)
        {
            try
            {
                BeginUpdate();
                Cache(Hg.AddFiles(fileNames, HgFileStatus.NotTracked));
            }
            finally
            {
                EndUpdate();
            }
        }
        
        internal void RemoveFiles(string[] fileNames)
        {
            try
            {
                BeginUpdate();
                Cache(Hg.RemoveFiles(fileNames));
            }
            finally
            {
                EndUpdate();
            }
        }

        internal void RenameFiles(string[] fileNames, string[] newFileNames)
        {
            lock (cache.SyncRoot)
            {
                foreach (var fileName in fileNames.Concat(newFileNames))
                {
                    cache.Remove(fileName);
                }
            }

            try
            {
                BeginUpdate();
                Cache(Hg.RenameFiles(fileNames, newFileNames));
            }
            catch
            {
                EndUpdate();
            }
        }

        internal void UpdateFileStatus(string[] fileNames)
        {
            Cache(Hg.GetFileInfo(fileNames));
        }

        internal void UpdateRootStatus(string path)
        {
            var root = HgPath.FindRepositoryRoot(path);

            if (String.IsNullOrEmpty(root))
            {
                return;
            }

            directoryWatchers.WatchDirectory(root);
            rootBranchDictionary[root] = Hg.GetCurrentBranchName(root);

            Cache(Hg.GetRootStatus(root));
        }


        public string GetBranchNames()
        {
            lock (rootBranchDictionary)
            {
                return rootBranchDictionary.Count > 0 ? rootBranchDictionary.Values.Distinct().Aggregate((x, y) => String.Concat(x, ", ", y)) : "";
            }
        }

        public string GetDirectoryBranch(string directory)
        {
            var branch = "";

            rootBranchDictionary.TryGetValue(directory, out branch);
            
            return branch;
        }

        public HgFileStatus GetFileStatus(string fileName)
        {
            var fileInfo = cache[fileName];

            return fileInfo != null ? fileInfo.Status : HgFileStatus.NotTracked;
        }


        public void ClearCache()
        {
            lock (directoryWatchers.SyncRoot)
            {
                directoryWatchers.UnsubscribeEvents();
                directoryWatchers.Clear();
            }

            lock (rootBranchDictionary)
            {
                rootBranchDictionary.Clear();
            }

            cache.Clear();
        }

        
        private void BeginUpdate()
        {
            running++;
        }

        private void EndUpdate()
        {
            running = Math.Max(0, running - 1);
        }

        private void Cache(HgFileInfo[] files)
        {
            cache.Add(files);
        }

        
        private void OnTimerElapsed(object source, ElapsedEventArgs e)
        {
            try
            {
                var commandsToRun = commands.DumpCommands();

                if (commandsToRun.Count > 0)
                {
                    RunCommands(commandsToRun);
                }
                else
                {
                    Update();
                }
            }
            finally
            {
                updateTimer.Start();
            }
        }


        private void RunCommands(HgCommandQueue commands)
        {
            foreach (var command in commands)
            {
                command.Run(this);
            }
            
            OnStatusChanged();
        }


        protected virtual void Update()
        {
            if (updateRequired)
            {
                UpdateAllRoots();
            }
            else
            {
                UpdateDirtyFiles();
            }
        }

        private void UpdateAllRoots()
        {
            try
            {
                BeginUpdate();

                updateRequired = false;
                
                cache.Clear();
                directoryWatchers.DumpDirtyFiles();

                foreach (var root in GetRoots())
                {
                    Cache(Hg.GetRootStatus(root));
                    rootBranchDictionary[root] = Hg.GetCurrentBranchName(root);
                }
            }
            finally
            {
                EndUpdate();
            }

            OnStatusChanged();
        }

        private string[] GetRoots()
        {
            lock (rootBranchDictionary)
            {
                return rootBranchDictionary.Keys.Where(x => !String.IsNullOrEmpty(x)).ToArray();
            }
        }

        private void UpdateDirtyFiles()
        {
            if (CanIgnoreDirtyFiles())
            {
                return;
            }

            var dirtyFiles = directoryWatchers.DumpDirtyFiles();

            if (HgDirstateChanged(dirtyFiles))
            {
                RequireFullUpdate();
            }
            else
            {
                UpdateDirtyFiles(dirtyFiles);
            }
        }

        private bool CanIgnoreDirtyFiles()
        {
            int dirtyFilesCount;
            double elapsed;

            lock (directoryWatchers.SyncRoot)
            {
                dirtyFilesCount = directoryWatchers.DirtyFilesCount;
                elapsed = (DateTime.Now - directoryWatchers.LatestChange).TotalMilliseconds;
            }

            return elapsed < UpdateInterval || dirtyFilesCount == 0;
        }

        private bool HgDirstateChanged(string[] dirtyFiles)
        {
            return dirtyFiles.Any(x => x.IndexOf(".hg\\dirstate") != -1);
        }

        private void UpdateDirtyFiles(string[] dirtyFiles)
        {
            var filteredDirtyFiles = dirtyFiles.Where(x => IsNotSpecial(x)).ToArray();

            if (filteredDirtyFiles.Length > FullUpdateDirtyFilesLimit)
            {
                RequireFullUpdate();
            }
            else if (filteredDirtyFiles.Length > 0)
            {
                UpdateFileStatus(dirtyFiles);
                OnStatusChanged(dirtyFiles);
            }
        }

        private void RequireFullUpdate()
        {
            updateRequired = running == 0;
        }

        private bool IsNotSpecial(string fileName)
        {
            if (HgPath.IsDirectory(fileName))
            {
                return false;
            }
            
            if (fileName.IndexOf("\\.hg") != -1)
            {
                return false;
            }
            
            return HasChanged(fileName);
        }

        private bool HasChanged(string fileName)
        {
            var fileInfo = cache[fileName];

            return fileInfo == null || fileInfo.HasChanged;
        }


        protected virtual void OnStatusChanged(string[] dirtyFiles)
        {
            OnStatusChanged();
        }

        protected virtual void OnStatusChanged()
        {
            StatusChanged(this, EventArgs.Empty);
        }
    }
}