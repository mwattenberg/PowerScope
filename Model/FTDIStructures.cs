using System;
using System.Runtime.InteropServices;

namespace PowerScope.Model
{
    /// <summary>
    /// Structure for device list information from FTDI header file
    /// Maps to FT_DEVICE_LIST_INFO_NODE from ftd2xx.h
    /// </summary>
    [StructLayout(LayoutKind.Sequential, CharSet = CharSet.Ansi)]
    public struct FT_DEVICE_LIST_INFO_NODE
    {
        public uint Flags;
        public uint Type;
        public uint ID;
        public uint LocId;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 16)]
        public string SerialNumber;
        [MarshalAs(UnmanagedType.ByValTStr, SizeConst = 64)]
        public string Description;
        public IntPtr ftHandle;
    }
}