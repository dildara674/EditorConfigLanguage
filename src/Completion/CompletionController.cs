﻿using Microsoft.VisualStudio;
using Microsoft.VisualStudio.Language.Intellisense;
using Microsoft.VisualStudio.OLE.Interop;
using Microsoft.VisualStudio.Text;
using Microsoft.VisualStudio.Text.Editor;
using System;
using System.Runtime.InteropServices;

namespace EditorConfig
{
    internal sealed class CompletionController : BaseCommand
    {
        private ICompletionSession _currentSession;
        private IQuickInfoBroker _quickInfoBroker;

        public CompletionController(IWpfTextView textView, ICompletionBroker broker, IQuickInfoBroker quickInfoBroker)
        {
            _currentSession = null;
            _quickInfoBroker = quickInfoBroker;

            TextView = textView;
            Broker = broker;
        }

        public IWpfTextView TextView { get; }
        public ICompletionBroker Broker { get; }

        public override int Exec(ref Guid pguidCmdGroup, uint nCmdID, uint nCmdexecopt, IntPtr pvaIn, IntPtr pvaOut)
        {
            bool handled = false;
            int hresult = VSConstants.S_OK;

            // 1. Pre-process
            if (pguidCmdGroup == VSConstants.VSStd2K)
            {
                switch ((VSConstants.VSStd2KCmdID)nCmdID)
                {
                    case VSConstants.VSStd2KCmdID.AUTOCOMPLETE:
                    case VSConstants.VSStd2KCmdID.COMPLETEWORD:
                    case VSConstants.VSStd2KCmdID.SHOWMEMBERLIST:
                        handled = StartSession();
                        break;
                    case VSConstants.VSStd2KCmdID.RETURN:
                        handled = Complete(false);
                        break;
                    case VSConstants.VSStd2KCmdID.TAB:
                        handled = Complete(true);
                        break;
                    case VSConstants.VSStd2KCmdID.CANCEL:
                        handled = Cancel();
                        break;
                }
            }

            if (!handled)
                hresult = Next.Exec(pguidCmdGroup, nCmdID, nCmdexecopt, pvaIn, pvaOut);

            if (ErrorHandler.Succeeded(hresult))
            {
                if (pguidCmdGroup == VSConstants.VSStd2K)
                {
                    switch ((VSConstants.VSStd2KCmdID)nCmdID)
                    {
                        case VSConstants.VSStd2KCmdID.TYPECHAR:
                            HandleTypeChar(pvaIn);
                            break;
                        case VSConstants.VSStd2KCmdID.BACKSPACE:
                        case VSConstants.VSStd2KCmdID.DELETE:
                            Filter();
                            break;
                    }
                }
            }

            return hresult;
        }

        private void HandleTypeChar(IntPtr pvaIn)
        {
            bool handled = false;

            if (EditorConfigPackage.Language.Preferences.AutoListMembers)
            {
                char ch = (char)(ushort)Marshal.GetObjectForNativeVariant(pvaIn);

                if (char.IsLetterOrDigit(ch) && EditorConfigPackage.Language.Preferences.AutoListMembers)
                {
                    StartSession();
                    handled = true;
                }
                else if ((ch == ':' || ch == '=' || ch == ' ' || ch == ',') && EditorConfigPackage.Language.Preferences.AutoListMembers)
                {
                    Cancel();
                    StartSession();
                    handled = true;
                }
            }

            if (!handled && _currentSession != null)
            {
                Filter();
            }
        }

        private void Filter()
        {
            if (_currentSession == null)
                return;

            _currentSession.SelectedCompletionSet.SelectBestMatch();
            _currentSession.SelectedCompletionSet.Recalculate();
        }

        bool Cancel()
        {
            if (_currentSession == null)
                return false;

            _currentSession.Dismiss();

            return true;
        }

        bool Complete(bool force)
        {
            if (_currentSession == null)
                return false;

            if (!_currentSession.SelectedCompletionSet.SelectionStatus.IsSelected && !force)
            {
                _currentSession.Dismiss();
                return false;
            }
            else
            {
                string moniker = _currentSession.SelectedCompletionSet.Moniker;
                _currentSession.Commit();

                if (!EditorConfigPackage.CompletionOptions.AutoInsertDelimiters)
                    return true;

                SnapshotPoint position = TextView.Caret.Position.BufferPosition;

                if (moniker == "keyword")
                {
                    TextView.TextBuffer.Insert(position, " = ");

                    if (EditorConfigPackage.Language.Preferences.AutoListMembers)
                        StartSession();
                }
                else if (moniker == "value")
                {
                    var document = EditorConfigDocument.FromTextBuffer(TextView.TextBuffer);
                    Property prop = document.PropertyAtPosition(position - 1);

                    if (SchemaCatalog.TryGetKeyword(prop.Keyword.Text, out Keyword keyword) && prop.Value != null)
                    {
                        if (keyword.RequiresSeverity && prop.Value.Text.Is("true"))
                        {
                            TextView.TextBuffer.Insert(position, ":");

                            if (EditorConfigPackage.Language.Preferences.AutoListMembers)
                                StartSession();
                        }
                    }
                }

                return true;
            }
        }

        bool StartSession()
        {
            if (_currentSession != null)
                return false;

            SnapshotPoint caret = TextView.Caret.Position.BufferPosition;
            ITextSnapshot snapshot = caret.Snapshot;

            if (!Broker.IsCompletionActive(TextView))
            {
                _currentSession = Broker.CreateCompletionSession(TextView, snapshot.CreateTrackingPoint(caret, PointTrackingMode.Positive), true);
            }
            else
            {
                _currentSession = Broker.GetSessions(TextView)[0];
            }

            _currentSession.Dismissed += (sender, args) => _currentSession = null;
            _currentSession.Start();

            if (_quickInfoBroker.IsQuickInfoActive(TextView))
            {
                foreach (IQuickInfoSession session in _quickInfoBroker.GetSessions(TextView))
                {
                    session.Dismiss();
                }
            }

            return true;
        }

        public override int QueryStatus(ref Guid pguidCmdGroup, uint cCmds, OLECMD[] prgCmds, IntPtr pCmdText)
        {
            if (pguidCmdGroup == VSConstants.VSStd2K)
            {
                switch ((VSConstants.VSStd2KCmdID)prgCmds[0].cmdID)
                {
                    case VSConstants.VSStd2KCmdID.AUTOCOMPLETE:
                    case VSConstants.VSStd2KCmdID.COMPLETEWORD:
                    case VSConstants.VSStd2KCmdID.SHOWMEMBERLIST:
                        prgCmds[0].cmdf = (uint)OLECMDF.OLECMDF_ENABLED | (uint)OLECMDF.OLECMDF_SUPPORTED;
                        return VSConstants.S_OK;
                }
            }
            return Next.QueryStatus(pguidCmdGroup, cCmds, prgCmds, pCmdText);
        }
    }
}