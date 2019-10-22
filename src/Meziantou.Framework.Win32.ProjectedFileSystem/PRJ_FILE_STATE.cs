﻿using System;

namespace Meziantou.Framework.Win32.ProjectedFileSystem
{
    [Flags]
    public enum PRJ_FILE_STATE
    {
        PRJ_FILE_STATE_PLACEHOLDER = 0x00000001,
        PRJ_FILE_STATE_HYDRATED_PLACEHOLDER = 0x00000002,
        PRJ_FILE_STATE_DIRTY_PLACEHOLDER = 0x00000004,
        PRJ_FILE_STATE_FULL = 0x00000008,
        PRJ_FILE_STATE_TOMBSTONE = 0x00000010,
    }
}
