﻿using System;
using System.ComponentModel.Design;
using System.Linq;
using HgLib;
using Microsoft.VisualStudio;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Shell;
using Microsoft.VisualStudio.Shell.Interop;

namespace VisualHg
{
    partial class SccProvider
    {
        private const int OLECMDERR_E_NOTSUPPORTED = (int)Microsoft.VisualStudio.OLE.Interop.Constants.OLECMDERR_E_NOTSUPPORTED;

        private void InitializeMenuCommands()
        {
            var menuCommandService = GetService(typeof(IMenuCommandService)) as OleMenuCommandService;

            if (menuCommandService != null)
            {
                AddMenuCommands(menuCommandService);
            }
        }

        private void AddMenuCommands(OleMenuCommandService menuCommandService)
        {
            var commandId = new CommandID(Guids.CommandSetGuid, CommandId.PendingChanges);
            var command = new MenuCommand(ShowPendingChangesToolWindow, commandId);
            menuCommandService.AddCommand(command);

            commandId = new CommandID(Guids.CommandSetGuid, CommandId.Commit);
            command = new MenuCommand(ShowCommitWindow, commandId);
            menuCommandService.AddCommand(command);

            commandId = new CommandID(Guids.CommandSetGuid, CommandId.Workbench);
            command = new MenuCommand(ShowWorkbenchWindow, commandId);
            menuCommandService.AddCommand(command);

            commandId = new CommandID(Guids.CommandSetGuid, CommandId.Status);
            command = new MenuCommand(ShowStatusWindow, commandId);
            menuCommandService.AddCommand(command);

            commandId = new CommandID(Guids.CommandSetGuid, CommandId.Synchronize);
            command = new MenuCommand(ShowSynchronizeWindow, commandId);
            menuCommandService.AddCommand(command);

            commandId = new CommandID(Guids.CommandSetGuid, CommandId.Update);
            command = new MenuCommand(ShowUpdateWindow, commandId);
            menuCommandService.AddCommand(command);

            commandId = new CommandID(Guids.CommandSetGuid, CommandId.Add);
            command = new MenuCommand(ShowAddSelectedWindow, commandId);
            menuCommandService.AddCommand(command);

            commandId = new CommandID(Guids.CommandSetGuid, CommandId.CommitSelected);
            command = new MenuCommand(ShowCommitSelectedWindow, commandId);
            menuCommandService.AddCommand(command);

            commandId = new CommandID(Guids.CommandSetGuid, CommandId.Diff);
            command = new MenuCommand(ShowDiffWindow, commandId);
            menuCommandService.AddCommand(command);

            commandId = new CommandID(Guids.CommandSetGuid, CommandId.Revert);
            command = new MenuCommand(ShowRevertWindow, commandId);
            menuCommandService.AddCommand(command);

            commandId = new CommandID(Guids.CommandSetGuid, CommandId.History);
            command = new MenuCommand(ShowHistoryWindow, commandId);
            menuCommandService.AddCommand(command);
        }


        public int QueryStatus(ref Guid commandSetGuid, uint commandCount, OLECMD[] commands, IntPtr text)
        {
            if (commandCount != 1)
            {
                return VSConstants.E_INVALIDARG;
            }

            if (commandSetGuid != Guids.CommandSetGuid)
            {
                return OLECMDERR_E_NOTSUPPORTED;
            }

            var visible = sccService.Active ? IsCommandVisible(commands[0].cmdID) : false;

            commands[0].cmdf = (uint)VisibleToOleCmdf(visible);

            return VSConstants.S_OK;
        }
    
        private OLECMDF VisibleToOleCmdf(bool visible)
        {
            return OLECMDF.OLECMDF_SUPPORTED | (visible ? OLECMDF.OLECMDF_ENABLED : OLECMDF.OLECMDF_INVISIBLE);
        }

        private bool IsCommandVisible(uint commandId)
        {
            switch (commandId)
            {
                case CommandId.Add:
                    return IsAddMenuItemVisible();

                case CommandId.CommitSelected:
                    return IsCommitSelectedMenuItemVisible();

                case CommandId.Diff:
                    return IsDiffMenuItemVisible();

                case CommandId.Revert:
                    return IsRevertMenuItemVisible();

                case CommandId.History:
                    return IsHistoryMenuItemVisible();

                default:
                    return true;
            }
        }

        private bool IsAddMenuItemVisible()
        {
            return SearchAnySelectedFileStatusMatches(HgFileStatus.NotAdded, true);
        }

        private bool IsCommitSelectedMenuItemVisible()
        {
            return SearchAnySelectedFileStatusMatches(HgFileStatus.Pending, true);
        }

        private bool IsDiffMenuItemVisible()
        {
            return SelectedFileStatusMatches(HgFileStatus.Comparable);
        }

        private bool IsRevertMenuItemVisible()
        {
            return SearchAnySelectedFileStatusMatches(HgFileStatus.Pending);
        }

        private bool IsHistoryMenuItemVisible()
        {
            return SelectedFileStatusMatches(HgFileStatus.Tracked);
        }


        private void ShowPendingChangesToolWindow(object sender, EventArgs e)
        {
            var windowFrame = PendingChangesToolWindow.Frame as IVsWindowFrame;

            if (windowFrame != null)
            {
                ErrorHandler.ThrowOnFailure(windowFrame.Show());
            }
        }


        private void ShowCommitWindow(object sender, EventArgs e)
        {
            GetRootAnd(TortoiseHg.ShowCommitWindow);
        }

        private void ShowWorkbenchWindow(object sender, EventArgs e)
        {
            GetRootAnd(TortoiseHg.ShowWorkbenchWindow);
        }

        private void ShowStatusWindow(object sender, EventArgs e)
        {
            GetRootAnd(TortoiseHg.ShowStatusWindow);
        }

        private void ShowSynchronizeWindow(object sender, EventArgs e)
        {
            GetRootAnd(TortoiseHg.ShowSynchronizeWindow);
        }

        private void ShowUpdateWindow(object sender, EventArgs e)
        {
            GetRootAnd(TortoiseHg.ShowUpdateWindow);
        }

        private void GetRootAnd(Action<string> showWindow)
        {
            SaveAllFiles();

            var root = CurrentRootDirectory;

            if (!String.IsNullOrEmpty(root))
            {
                showWindow(root);
            }
            else
            {
                NotifySolutionIsNotUnderVersionControl();
            }
        }


        private void ShowAddSelectedWindow(object sender, EventArgs e)
        {
            SaveAllFiles();

            var filesToAdd = GetSelectedFiles(true).Where(FileIsNotAdded).ToArray();

            if (filesToAdd.Length > 0)
            {
                TortoiseHg.ShowAddWindow(filesToAdd);
            }
        }

        private void ShowCommitSelectedWindow(object sender, EventArgs e)
        {
            ShowCommitWindow(GetSelectedFiles(true));
        }

        private void ShowDiffWindow(object sender, EventArgs e)
        {
            ShowDiffWindow(SelectedFile);
        }

        private void ShowRevertWindow(object sender, EventArgs e)
        {
            ShowRevertWindow(GetSelectedFiles(false));
        }

        private void ShowHistoryWindow(object sender, EventArgs e)
        {
            ShowHistoryWindow(SelectedFile);
        }
    }
}