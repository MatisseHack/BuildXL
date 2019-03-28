// Copyright (c) Microsoft. All rights reserved.
// Licensed under the MIT license. See LICENSE file in the project root for full license information.

#ifndef Sandbox_h
#define Sandbox_h

#import "BuildXLSandboxShared.hpp"

#define KEXT_SERVICE_NOT_FOUND                     0x1
#define KEXT_SERVICE_COULD_NOT_OPEN                0x2
#define KEXT_MACH_PORT_CREATION_ERROR              0x4
#define KEXT_NOTIFICATION_PORT_ERROR               0x8
#define KEXT_SHARED_MEMORY_CREATION_ERROR          0x10
#define KEXT_BUILDXL_LAUNCH_SIGNAL_FAIL            0x20
#define KEXT_BUILDXL_CONNECTION_INFO_CALLBACK_FAIL 0x40
#define KEXT_THREAD_ID_ERROR                       0x80
#define KEXT_WRONG_BUFFER_SIZE                     0x100

#define REPORT_QUEUE_SUCCESS                      0x1000
#define REPORT_QUEUE_CONNECTION_ERROR             0x1001
#define REPORT_QUEUE_DEQUEUE_ERROR                0x1002

extern "C"
{
    void SetLogger(os_log_t newLogger);

    /*!
     * Normalized path is stored in 'buffer'.  That buffer must be 'bufferSize' bytes long.
     *
     * @param path Path to normalize and hash.
     * @param buffer Buffer where the normalized path is stored.
     * @param bufferSize The size of 'buffer' in bytes.
     * @result Hash of the normalized path.
     */
    int NormalizePathAndReturnHash(const BYTE *path, BYTE *buffer, int bufferSize);

    typedef struct {
        int error;
        uint connection;
        IONotificationPortRef port;
    } KextConnectionInfo;

    typedef struct {
        int error;
        mach_vm_address_t address;
        mach_port_t port;
    } KextSharedMemoryInfo;

    void InitializeKextConnection(KextConnectionInfo *info, long infoSize);
    void InitializeKextSharedMemory(KextSharedMemoryInfo *memoryInfo, long memoryInfoSize, KextConnectionInfo info);

    void DeinitializeKextConnection(KextConnectionInfo info);
    void DeinitializeKextSharedMemory(KextSharedMemoryInfo memoryInfo, KextConnectionInfo info);

    bool Configure(KextConfig config, KextConnectionInfo info);

    bool SendPipStarted(const pid_t processId, pipid_t pipId, const char *const famBytes, int famBytesLength, KextConnectionInfo info);
    bool SendPipProcessTerminated(pipid_t pipId, pid_t processId, KextConnectionInfo info);
    bool CheckForDebugMode(bool *isDebugModeEnabled, KextConnectionInfo info);

    /*!
     * The kernel extension currently relies on being externally notified of the current CPU usage
     * in order for fork throttling to work correctly.  If fork throttling is enabled, invoke this
     * function at steady intervals to provide the latest CPU/RAM usage (to obtain CPU usage, you can
     * use 'host_statistics()' or 'host_processor_info()').
     */
    bool UpdateCurrentResourceUsage(uint cpuUsageBasisPoints, uint ramUsageBasisPoints, KextConnectionInfo info);

    typedef void (__cdecl *FailureNotificationCallback)(void *, IOReturn);
    bool SetFailureNotificationHandler(FailureNotificationCallback callback, KextConnectionInfo info);

    typedef void (__cdecl *AccessReportCallback)(AccessReport, int);
    __cdecl void ListenForFileAccessReports(AccessReportCallback callback, long accessReportSize, mach_vm_address_t address, mach_port_t port);

    uint64_t GetMachAbsoluteTime(void);
    __cdecl void KextVersionString(char *version, int size);

    bool IntrospectKernelExtension(KextConnectionInfo info, IntrospectResponse *result);
}

#endif /* sandbox_h */
