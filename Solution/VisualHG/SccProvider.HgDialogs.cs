﻿using System;
using System.Threading;
using HgLib;

namespace VisualHg
{
    partial class SccProvider
    {
        public void ShowCommitWindow(string directory)
        {
            QueueTortoiseHgStart("commit", directory);
        }

        public void ShowWorkbenchWindow(string directory)
        {
            QueueTortoiseHgStart("log", directory);
        }

        public void ShowStatusWindow(string directory)
        {
            QueueTortoiseHgStart("status", directory);
        }

        public void ShowSynchronizeWindow(string directory)
        {
            QueueTortoiseHgStart("synch", directory);
        }

        public void ShowUpdateWindow(string directory)
        {
            QueueTortoiseHgStart("update", directory);
        }


        public void ShowAddSelectedWindow(string[] files)
        {
            QueueTortoiseHgStart(" --nofork add ", files);
        }

        public void ShowCommitWindowPrivate(string[] files)
        {
            QueueTortoiseHgStart(" --nofork commit ", files);
        }

        public void ShowDiffWindow(string parent, string current, string customDiffTool)
        {
            QueueDiffToolStart(parent, current, customDiffTool);
        }

        public void ShowRevertWindowPrivate(string[] files)
        {
            QueueTortoiseHgStart(" --nofork revert ", files);
        }

        public void ShowHistoryWindowPrivate(string fileName)
        {
            var root = HgProvider.FindRepositoryRoot(fileName);

            if (!String.IsNullOrEmpty(root))
            {
                fileName = fileName.Substring(root.Length + 1);

                QueueTortoiseHgStart(String.Format("log \"{0}\"", fileName), root);
            }
        }


        private void QueueTortoiseHgStart(string command, string directory)
        {
            ThreadPool.QueueUserWorkItem(o => {
                try
                {
                    var process = TortoiseHg.Start(command, directory);

                    if (process != null)
                    {
                        process.WaitForExit();
                    }

                    sccService.Repository.CacheUpdateRequired = false;
                    sccService.Repository.Enqueue(new UpdateRootStatusHgCommand(directory));
                }
                catch { }
            });
        }

        private void QueueTortoiseHgStart(string command, string[] files)
        {
            ThreadPool.QueueUserWorkItem(o => {
                try
                {
                    TortoiseHg.StartForEachRoot(command, files);

                    sccService.Repository.CacheUpdateRequired = false;
                    sccService.Repository.Enqueue(new UpdateFileStatusHgCommand(files));
                }
                catch { }
            });
        }

        private void QueueDiffToolStart(string parent, string current, string customDiffTool)
        {
            ThreadPool.QueueUserWorkItem(o => {
                try
                {
                    var process = TortoiseHg.StartDiff(parent, current, customDiffTool);

                    if (process != null)
                    {
                        process.WaitForExit();
                    }

                    sccService.Repository.CacheUpdateRequired = false;
                    sccService.Repository.Enqueue(new UpdateFileStatusHgCommand(new[] { current }));
                }
                catch { }
            });
        }
    }
}
