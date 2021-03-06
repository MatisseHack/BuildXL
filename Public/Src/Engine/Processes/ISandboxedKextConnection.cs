// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

using System;

namespace BuildXL.Processes
{
    /// <summary>
    /// Interface for <see cref="KextConnection"/> used to establish a sandbox kernel extension connection
    /// and manage communication between BuildXL and the macOS kernel extension.
    /// </summary>
    public interface IKextConnection : IDisposable
    {
        /// <summary>
        /// Whether to measure CPU times (user/system) of sandboxed processes.  Default: false.
        /// </summary>
        /// <remarks>
        /// In principle thre should be no reason not to measure CPU times.  This amounts to wrapping every
        /// process in '/usr/bin/time', which could (potentially) lead to some unexpected behavior.
        /// </remarks>
        bool MeasureCpuTimes { get; }

        /// <summary>
        /// Reports the earliest (minimum) enqueue time received from all the sandbox kernel report queues available
        /// </summary>
        ulong MinReportQueueEnqueueTime { get; }

        /// <summary>
        /// Timespan between now and when the last report was received (from any queue).
        /// </summary>
        TimeSpan CurrentDrought { get; }

        /// <summary>
        /// Notifies the kernel extension of:
        ///   (1) the current CPU usage (in basis points), and
        ///   (2) amount of available physical memory (in megabytes).
        /// CPU usage is normalized across all cores (i.e., this number should be between 0 and 10000,
        /// which corresponds to 0% and 100%).
        /// </summary>
        /// <remarks>
        /// This method wouldn't be necessary if the kernel extension could obtain 'host_statistics' on its own;
        /// with the current kernel (High Sierra), unfortunately, it appears that there is no way for it to do so.
        /// </remarks>
        bool NotifyUsage(uint cpuUsageBasisPoints, uint availableRamMB);

        /// <summary>
        /// Notifies the kernel extension that a new pip is about to start. Since the kernel extension expects to receive the
        /// process ID of the pip, this method requires that the supplied <paramref name="process"/> has already been started,
        /// and hence already has an ID assigned to it. To ensure that the process is not going to request file accesses before the
        /// kernel extension is notified about it being started, the process should be started in some kind of suspended mode, and
        /// resumed only after the kernel extension has been notified.
        /// </summary>
        bool NotifyKextPipStarted(FileAccessManifest fam, SandboxedProcessMacKext process);

        /// <summary>
        /// Notifies the sandbox kernel extension that <paramref name="process"/> is done processing access reports
        /// for Pip <paramref name="pipId"/> so that resources can be freed up.
        /// Returns whether the sandbox kernel extension was successfully notified and cleaned up all resources
        /// for the pip with <paramref name="pipId"/>d.
        /// </summary>
        bool NotifyKextProcessFinished(long pipId, SandboxedProcessMacKext process);

        /// <summary>
        /// Notification that a pip process was forcefully terminated.
        /// </summary>
        void NotifyKextPipProcessTerminated(long pipId, int processId);

        /// <summary>
        /// Releases all resources held by the sandbox kernel extension connection including all unmanaged references too. This is only for unit testing and should not
        /// be called directly at any time! Unit tests need this as they reference a static sandbox kernel extension connection instance that is torn down on process exit.
        /// This is done to not overburden the host system and kernel extension with connection spam.
        /// </summary>
        void ReleaseResources();

        /// <summary>
        /// Indicates if the SandboxKextConnection is running for unit-test mode.
        /// </summary>
        bool IsInTestMode { get; }
    }
}
