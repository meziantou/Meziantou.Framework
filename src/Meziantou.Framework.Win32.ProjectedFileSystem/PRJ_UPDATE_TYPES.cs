﻿#nullable disable
using System;

namespace Meziantou.Framework.Win32.ProjectedFileSystem
{
    [Flags]
    public enum PRJ_UPDATE_TYPES
    {
        PRJ_UPDATE_NONE = 0x00000000,
        PRJ_UPDATE_ALLOW_DIRTY_METADATA = 0x00000001,
        PRJ_UPDATE_ALLOW_DIRTY_DATA = 0x00000002,
        PRJ_UPDATE_ALLOW_TOMBSTONE = 0x00000004,
        //PRJ_UPDATE_RESERVED1 = 0x00000008,
        //PRJ_UPDATE_RESERVED2 = 0x00000010,
        PRJ_UPDATE_ALLOW_READ_ONLY = 0x00000020,
        PRJ_UPDATE_MAX_VAL = (PRJ_UPDATE_ALLOW_READ_ONLY << 1),
    }
}
