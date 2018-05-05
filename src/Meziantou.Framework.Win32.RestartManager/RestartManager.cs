using System;
using System.Collections.Generic;
using System.ComponentModel;
using System.Diagnostics;
using Meziantou.Framework.Win32.Natives;

namespace Meziantou.Framework.Win32
{
    public class RestartManager : IDisposable
    {
        private int SessionHandle { get; }
        public string SessionKey { get; }

        private RestartManager(int sessionHandle, string sessionKey)
        {
            SessionHandle = sessionHandle;
            SessionKey = sessionKey;
        }

        public static RestartManager CreateSession()
        {
            var sessionKey = Guid.NewGuid().ToString();
            var result = NativeMethods.RmStartSession(out var handle, 0, strSessionKey: sessionKey);
            if (result != RmResult.ERROR_SUCCESS)
                throw new Win32Exception((int)result, "RmStartSession failed");

            return new RestartManager(handle, sessionKey);
        }

        public static RestartManager JoinSession(string sessionKey)
        {
            var result = NativeMethods.RmJoinSession(out var handle, sessionKey);
            if (result != RmResult.ERROR_SUCCESS)
                throw new Win32Exception((int)result, "RmStartSession failed");

            return new RestartManager(handle, sessionKey);
        }

        public void RegisterFile(string path)
        {
            if (path == null) throw new ArgumentNullException(nameof(path));

            string[] resources = { path };
            var result = NativeMethods.RmRegisterResources(SessionHandle, 1, resources, 0, null, 0, null);
            if (result != RmResult.ERROR_SUCCESS)
                throw new Win32Exception((int)result, "RmRegisterResources failed");
        }

        public void RegisterFiles(string[] paths)
        {
            if (paths == null) throw new ArgumentNullException(nameof(paths));

            var result = NativeMethods.RmRegisterResources(SessionHandle, (uint)paths.LongLength, paths, 0, null, 0, null);
            if (result != RmResult.ERROR_SUCCESS)
                throw new Win32Exception((int)result, "RmRegisterResources failed");
        }

        public bool IsResourcesLocked()
        {
            uint arraySize = 1;
            while (true)
            {
                var array = new RM_PROCESS_INFO[arraySize];
                var result = NativeMethods.RmGetList(SessionHandle, out var arrayCount, ref arraySize, array, out _);
                if (result == RmResult.ERROR_SUCCESS || result == RmResult.ERROR_MORE_DATA)
                {
                    return arrayCount > 0;
                }
                else
                {
                    throw new Win32Exception((int)result, "RmGetList failed");
                }
            }
        }

        public IReadOnlyList<Process> GetProcessesLockingResources()
        {
            uint arraySize = 10;
            while (true)
            {
                var array = new RM_PROCESS_INFO[arraySize];
                var result = NativeMethods.RmGetList(SessionHandle, out var arrayCount, ref arraySize, array, out _);
                if (result == RmResult.ERROR_SUCCESS)
                {
                    var processes = new List<Process>((int)arrayCount);
                    for (var i = 0; i < arrayCount; i++)
                    {
                        try
                        {
                            var process = Process.GetProcessById(array[i].Process.dwProcessId);
                            if (process != null)
                                processes.Add(process);
                        }
                        catch
                        {
                        }
                    }

                    return processes;
                }
                else if (result == RmResult.ERROR_MORE_DATA)
                {
                    arraySize = arrayCount;
                }
                else
                {
                    throw new Win32Exception((int)result, "RmGetList failed");
                }
            }
        }

        public void Shutdown(RmShutdownType action)
        {
            Shutdown(action, null);
        }

        public void Shutdown(RmShutdownType action, RmWriteStatusCallback statusCallback)
        {
            var result = NativeMethods.RmShutdown(SessionHandle, action, statusCallback);
            if (result != RmResult.ERROR_SUCCESS)
                throw new Win32Exception((int)result, "RmShutdown failed");
        }

        public void Restart()
        {
            Restart(null);
        }

        public void Restart(RmWriteStatusCallback statusCallback)
        {
            var result = NativeMethods.RmRestart(SessionHandle, 0, statusCallback);
            if (result != RmResult.ERROR_SUCCESS)
                throw new Win32Exception((int)result, "RmShutdown failed");
        }

        public void Dispose()
        {
            if (SessionHandle != 0)
            {
                var result = NativeMethods.RmEndSession(SessionHandle);
                if (result != RmResult.ERROR_SUCCESS)
                    throw new Win32Exception((int)result, "RmEndSession failed");
            }
        }

        public static bool IsFileLocked(string path)
        {
            using (var restartManager = CreateSession())
            {
                restartManager.RegisterFile(path);
                return restartManager.IsResourcesLocked();
            }
        }

        public static IReadOnlyList<Process> GetProcessesLockingFile(string path)
        {
            using (var restartManager = CreateSession())
            {
                restartManager.RegisterFile(path);
                return restartManager.GetProcessesLockingResources();
            }
        }
    }
}
