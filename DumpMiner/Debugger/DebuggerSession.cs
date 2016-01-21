﻿using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Linq;
using System.Threading.Tasks;
using DumpMiner.Common;
using Microsoft.Diagnostics.Runtime;
using Microsoft.Diagnostics.Runtime.Interop;

namespace DumpMiner.Debugger
{
    internal class DebuggerSession : IDebuggerSession
    {
        #region members and props
        private readonly Task _workerTask;
        private Process _attachedProcess;
        public static readonly IDebuggerSession Instance = new DebuggerSession();
        public bool IsAttached => DataTarget != null && Runtime != null;
        public DateTime AttachedTime { get; private set; }
        public Action OnDetach { get; set; }
        public ClrRuntime Runtime { get; private set; }
        public ClrHeap Heap { get; private set; }
        public DataTarget DataTarget { get; private set; }
        public CrashDumpReader DumpReader { get; private set; }

        //In case that we have more than one runtime
        //private readonly List<ClrRuntime> _runtimes = new List<ClrRuntime>(); 
        //private readonly List<ModuleInfo> _dacInfo = new List<ModuleInfo>();
        #endregion

        private DebuggerSession()
        {
            _workerTask = new Task(() => { });
            _workerTask.Start();
        }

        public void SetSymbolPath(string[] path, bool append = true)
        {
            if (append)
                foreach (var s in path)
                    DataTarget.SymbolLocator.SymbolPath += ";" + s;
            else
                DataTarget.SymbolLocator.SymbolPath = path.Single();
        }

        public async Task<IEnumerable<object>> ExecuteOperation(Func<IEnumerable<object>> operation)
        {
            IEnumerable<object> result = null;
            if (DumpReader == CrashDumpReader.ClrMD)
            {
                await Task.Run(() => { result = operation(); });
            }
            else
            {
                await _workerTask.ContinueWith(t => { result = operation(); });
            }
            return result;
        }

        #region Attach\Detach
        public async Task<bool> LoadDump(string fileName, CrashDumpReader readerType)
        {
            if (IsAttached)
                return true;

            DumpReader = readerType;
            if (readerType == CrashDumpReader.DbgEng)
            {
                return await _workerTask.ContinueWith(task => LoadDump(fileName));
            }

            return await Task.Run(() => LoadDump(fileName)).ConfigureAwait(false);
        }

        private bool LoadDump(string fileName)
        {
            DataTarget = DataTarget.LoadCrashDump(fileName, DumpReader);
            var result = CreateRuntime();
            if (!result)
                Dispose(true);
            return result;
        }

        public async Task<bool> Attach(Process process, uint milliseconds)
        {
            if (IsAttached)
                return true;
            bool result = false;
            _attachedProcess = process;
            await _workerTask.ContinueWith(task =>
            {
                DataTarget = DataTarget.AttachToProcess(_attachedProcess.Id, milliseconds, AttachFlag.NonInvasive);
                result = CreateRuntime();
                if (result)
                    _attachedProcess.Exited += Process_Exited;
                else
                    Dispose(true);
            });
            return result;
        }

        private bool CreateRuntime()
        {
            GC.ReRegisterForFinalize(this);

            var clrVersion = DataTarget.ClrVersions.FirstOrDefault();
            if (clrVersion == null)
                return false;
            Runtime = clrVersion.CreateRuntime();
            Heap = Runtime.GetHeap();
            if (Heap == null || !Heap.CanWalkHeap)
                return false;
            AttachedTime = GetAttachedTime();
            SetSymbolPath(new[] { Environment.CurrentDirectory, Properties.Resources.SymbolCache, Properties.Resources.DllsFolder });
            return true;
        }

        private DateTime GetAttachedTime()
        {
            try
            {
                uint secondsSinceUnix;
                var dbgCtrl2 = (IDebugControl2)Runtime.DataTarget.DebuggerInterface;
                dbgCtrl2.GetCurrentTimeDate(out secondsSinceUnix);
                var origin = new DateTime(1970, 1, 1, 0, 0, 0, 0);
                return origin.AddSeconds(secondsSinceUnix).ToLocalTime();
            }
            catch (Exception)
            {
                return DateTime.MinValue;
            }
        }

        public void Detach()
        {
            _workerTask.ContinueWith(t =>
            {
                try
                {
                    DataTarget.DebuggerInterface.DetachProcesses();
                }
                finally
                {
                    try
                    {
                        Dispose(true);
                        OnDetach?.Invoke();
                    }
                    catch (Exception)
                    {
                    }
                }
            });
        }

        void Process_Exited(object sender, EventArgs e)
        {
            Dispose(true);
        }

        #endregion

        #region Dispose

        public void Dispose()
        {
            Dispose(true);
        }

        private void Dispose(bool disposing)
        {
            if (disposing)
            {
                if (DataTarget != null)
                {
                    DataTarget.Dispose();
                    DataTarget = null;
                }
                if (_attachedProcess != null)
                {
                    _attachedProcess.Dispose();
                    _attachedProcess = null;
                }
                GC.SuppressFinalize(this);
            }
        }

        ~DebuggerSession()
        {
            Dispose(false);
        }

        #endregion
    }
}