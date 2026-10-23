# Dependency notices

- Microsoft.Data.Sqlite / Microsoft.Data.Sqlite.Core 10.0.13: MIT; copyright Microsoft Corporation in NuGet metadata and .NET Foundation and Contributors in the upstream license. License copied verbatim from dotnet/efcore tag v10.0.13, LICENSE.txt.
- SQLitePCLRaw.bundle_e_sqlite3, SQLitePCLRaw.core, SQLitePCLRaw.lib.e_sqlite3, SQLitePCLRaw.provider.e_sqlite3 2.1.13: Apache-2.0, copyright 2014–2024 SourceGear, LLC per the packages' nuspec metadata. These NuGet packages omit standalone license/notice files and the upstream repository has no v2.1.13 tag. The complete upstream Apache license and notice from the preceding v2.1.12 tag are retained verbatim here. The broad upstream notice also describes optional variants (such as SQLCipher) that this app does not use.
- Native SQLite is supplied by SQLitePCLRaw.lib.e_sqlite3; its public-domain dedication is included in the upstream notice.

`scripts/publish.ps1` adds these texts, the exact restored NuGet package metadata, and the .NET runtime license/notice files to every portable bundle. No third-party source files were modified. This directory does not license the app's own code.

Sources:

- https://github.com/dotnet/efcore/blob/v10.0.13/LICENSE.txt
- https://github.com/ericsink/SQLitePCL.raw/blob/v2.1.12/LICENSE.TXT
- https://github.com/ericsink/SQLitePCL.raw/blob/v2.1.12/NOTICE.TXT

- Windows SDK .NET projection, Microsoft.Windows.SDK.NET.Ref 10.0.19041.57: Windows-SDK-LICENSE.rtf is the unmodified license at the package's https://aka.ms/WinSDKLicenseURL, resolved on 2026-10-23 to https://download.microsoft.com/download/0/F/F/0FF2B061-47DD-4F55-89B6-FD1D8C44F14D/sdk_license.rtf. The actual implicit runtime-pack NuGet metadata is copied by the publisher.
- WinRT.Runtime 2.2.0.48161: CsWinRT-LICENSE.txt is the unmodified MIT license from https://github.com/microsoft/CsWinRT/blob/8649ee3eeb2445ca2a36d80d878ef60b96a6c65d/LICENSE (commit embedded in the binary's product version).
