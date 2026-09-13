# Third-Party Notices

Ironmon includes third-party components that remain subject to their own
license terms. The MIT License in the repository root does not replace those
terms.

## .NET runtime and Windows Desktop runtime

Setup and the recovery helper are self-contained Windows applications. They
include the Microsoft .NET and Windows Desktop runtimes under the MIT License:

Copyright (c) .NET Foundation and Contributors

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in
all copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN
THE SOFTWARE.

Runtime sources and additional notices:
[.NET runtime](https://github.com/dotnet/runtime),
[runtime third-party notices](https://github.com/dotnet/runtime/blob/main/THIRD-PARTY-NOTICES.TXT),
[WPF](https://github.com/dotnet/wpf).

This notice is embedded in both standalone executables and is also included
beside the helper in both tracker packages. The standalone executables also
embed the license and third-party notice files from their exact resolved
runtime packs under `Ironmon.RuntimeNotices` resource names.

## Private Git runtime

The updater downloads an unmodified, checksum-pinned MinGit archive directly
from Git for Windows when game operations need it. It does not redistribute
that archive inside the Ironmon executables. The downloaded distribution
retains its upstream license files. Git is licensed under GNU GPL version 2;
its bundled components have their own licenses.

See [Git for Windows releases and corresponding source](https://github.com/git-for-windows/git/releases)
and [Git's license](https://github.com/git/git/blob/master/COPYING).
The precise archive version, URL and SHA-256 used by this release are declared
in `MinGitPackage` in the reviewed updater source.

## Open Sans

The file
`tracker/src/Ironmon.Tracker.App/Resources/Fonts/OpenSans-Regular.ttf` is part
of the Open Sans font project.

Copyright 2020 The Open Sans Project Authors
(https://github.com/googlefonts/opensans)

Open Sans is licensed under the
[SIL Open Font License, Version 1.1](OPEN-SANS-LICENSE.txt).
