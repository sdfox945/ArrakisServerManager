# Microsoft runtime terms for the Windows package

The original Arrakis Server Manager code is separate from the Microsoft binaries bundled to make the Windows ZIP self-contained. The project's MIT license does not relicense those binaries.

Before using the packaged application, read and accept the applicable Microsoft terms in the `licenses` folder. If you do not accept them, do not use the bundled Microsoft components. Redistributors must retain these terms and notices and comply with Microsoft's requirements; the application license alone is not permission to redistribute every adjacent file under MIT.

| Components | Applicable text |
| --- | --- |
| Open-source .NET/Windows Desktop portions | [DOTNET-MIT.txt](licenses/DOTNET-MIT.txt), [WINDOWSDESKTOP-MIT.txt](licenses/WINDOWSDESKTOP-MIT.txt) and [full third-party notices](licenses/DOTNET-THIRD-PARTY-NOTICES.txt) |
| `coreclr.dll`, `Microsoft.DiaSymReader.Native.amd64.dll`, `PresentationNative_cor3.dll`, `vcruntime140_cor3.dll`, `wpfgfx_cor3.dll` | [Microsoft .NET Library terms](licenses/Microsoft.NET-Library.html) · [official source](https://dotnet.microsoft.com/en-us/dotnet_library_license.htm) |
| `D3DCompiler_47_cor3.dll` | [Microsoft Windows SDK terms](licenses/Microsoft.Windows-SDK.md) · [official source](https://learn.microsoft.com/en-us/legal/windows-sdk/license) |

The Microsoft terms address permitted distribution with an application, protections required for distributors/end users, notices, restrictions, warranties, remedies and other conditions. They are not replaced by this explanatory document. The maintainer must review those obligations before approving binary publication.

Microsoft identifies these exceptions in [its Windows licensing information](https://github.com/dotnet/core/blob/main/license-information-windows.md). In particular, the Windows runtime can include Windows Error Reporting crash-report data; consult the Microsoft terms and Windows privacy settings. The manager itself does not automatically upload diagnostics.
