# Dependency notices

- Microsoft.Data.Sqlite / Microsoft.Data.Sqlite.Core 10.0.13: MIT; copyright Microsoft Corporation in NuGet metadata and .NET Foundation and Contributors in the upstream license. License copied verbatim from dotnet/efcore tag v10.0.13, LICENSE.txt.
- SQLitePCLRaw.bundle_e_sqlite3, SQLitePCLRaw.core, SQLitePCLRaw.lib.e_sqlite3, SQLitePCLRaw.provider.e_sqlite3 2.1.13: Apache-2.0, copyright 2014–2024 SourceGear, LLC per the packages' nuspec metadata. These NuGet packages omit standalone license/notice files and the upstream repository has no v2.1.13 tag. The complete upstream Apache license and notice from the preceding v2.1.12 tag are retained verbatim here. The broad upstream notice also describes optional variants (such as SQLCipher) that this app does not use.
- Native SQLite is supplied by SQLitePCLRaw.lib.e_sqlite3; its public-domain dedication is included in the upstream notice.

`scripts/publish.ps1` adds these texts, the exact restored NuGet package metadata, and the .NET runtime license/notice files to every portable bundle. No third-party source files were modified. This directory does not license the app's own code.

Sources:

- https://github.com/dotnet/efcore/blob/v10.0.13/LICENSE.txt
- https://github.com/ericsink/SQLitePCL.raw/blob/v2.1.12/LICENSE.TXT
- https://github.com/ericsink/SQLitePCL.raw/blob/v2.1.12/NOTICE.TXT
