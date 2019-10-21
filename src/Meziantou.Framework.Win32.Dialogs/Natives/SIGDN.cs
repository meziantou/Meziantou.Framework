﻿#nullable disable
namespace Meziantou.Framework.Win32.Natives
{
    internal enum SIGDN : uint
    {
        SIGDN_NORMALDISPLAY = 0x00000000,               // SHGDN_NORMAL
        SIGDN_PARENTRELATIVEPARSING = 0x80018001,       // SHGDN_INFOLDER | SHGDN_FORPARSING
        SIGDN_DESKTOPABSOLUTEPARSING = 0x80028000,      // SHGDN_FORPARSING
        SIGDN_PARENTRELATIVEEDITING = 0x80031001,       // SHGDN_INFOLDER | SHGDN_FOREDITING
        SIGDN_DESKTOPABSOLUTEEDITING = 0x8004c000,      // SHGDN_FORPARSING | SHGDN_FORADDRESSBAR
        SIGDN_FILESYSPATH = 0x80058000,                 // SHGDN_FORPARSING
        SIGDN_URL = 0x80068000,                         // SHGDN_FORPARSING
        SIGDN_PARENTRELATIVEFORADDRESSBAR = 0x8007c001, // SHGDN_INFOLDER | SHGDN_FORPARSING | SHGDN_FORADDRESSBAR
        SIGDN_PARENTRELATIVE = 0x80080001,               // SHGDN_INFOLDER
    }
}
