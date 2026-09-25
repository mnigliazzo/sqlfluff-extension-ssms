using System;

namespace SqlFluff.Ssms
{
    internal static class PackageGuids
    {
        public const string PackageString = "8f1a3d52-6b0c-4d8e-9a47-2c5e7b1f4a90";
        public const string CmdSetString = "3c9e6a17-5d2b-4f83-b1c4-7a0e9d8f2b65";
        public const string OptionsPageString = "b2d6f0a4-91c3-4e57-8a1d-0c7f3e5b9d28";
        public const string ErrorListProviderString = "e4a7c1b8-0d63-4a95-9f2e-5b8d1c3a6f70";

        public static readonly Guid CmdSet = new Guid(CmdSetString);
        public static readonly Guid ErrorListProvider = new Guid(ErrorListProviderString);
    }

    internal static class PackageIds
    {
        public const int CmdLint = 0x0100;
        public const int CmdFix = 0x0101;
        public const int CmdClear = 0x0102;
        public const int CmdOptions = 0x0103;
        public const int CmdFormat = 0x0104;
        public const int CmdLintFolder = 0x0105;
        public const int CmdFixFolder = 0x0106;
        public const int CmdFormatFolder = 0x0107;
        public const int CmdCheckForUpdates = 0x0108;
        public const int CmdInstallSqlFluffTool = 0x0109;
    }
}
