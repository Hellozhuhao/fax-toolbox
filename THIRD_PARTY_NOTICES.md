# Third-party notices

## PdfPig

Source: https://github.com/UglyToad/PdfPig
Package: PdfPig 0.1.8
License: Apache License 2.0, https://www.apache.org/licenses/LICENSE-2.0

PdfPig is used locally to extract selectable text from PDF attachments.

## scrcpy and its Windows runtime

Source: https://github.com/Genymobile/scrcpy (version 5.0)
License: Apache License 2.0. The bundled runtime contains LICENSE-scrcpy.txt;
upstream license files shipped with the official archive are retained.
Official Windows binaries (including ADB, SDL and FFmpeg dependencies) are
downloaded from the upstream release and verified against its published SHA-256.
Source and build instructions: https://github.com/Genymobile/scrcpy/tree/v5.0
No Android DEX binaries or proprietary source are incorporated.

## Microsoft Agent Framework

Source: https://github.com/microsoft/agent-framework
Package: Microsoft.Agents.AI 1.20.0

MIT License

Copyright (c) Microsoft Corporation.

Permission is hereby granted, free of charge, to any person obtaining a copy
of this software and associated documentation files (the "Software"), to deal
in the Software without restriction, including without limitation the rights
to use, copy, modify, merge, publish, distribute, sublicense, and/or sell
copies of the Software, and to permit persons to whom the Software is
furnished to do so, subject to the following conditions:

The above copyright notice and this permission notice shall be included in all
copies or substantial portions of the Software.

THE SOFTWARE IS PROVIDED "AS IS", WITHOUT WARRANTY OF ANY KIND, EXPRESS OR
IMPLIED, INCLUDING BUT NOT LIMITED TO THE WARRANTIES OF MERCHANTABILITY,
FITNESS FOR A PARTICULAR PURPOSE AND NONINFRINGEMENT. IN NO EVENT SHALL THE
AUTHORS OR COPYRIGHT HOLDERS BE LIABLE FOR ANY CLAIM, DAMAGES OR OTHER
LIABILITY, WHETHER IN AN ACTION OF CONTRACT, TORT OR OTHERWISE, ARISING FROM,
OUT OF OR IN CONNECTION WITH THE SOFTWARE OR THE USE OR OTHER DEALINGS IN THE
SOFTWARE.
# Mihomo

The Windows package includes the unmodified official Mihomo v1.19.32 Windows amd64 v1 release archive.
Copyright MetaCubeX and contributors. Licensed under GPL-3.0.
Source, license and corresponding release: https://github.com/MetaCubeX/mihomo/tree/v1.19.32
The source is available from the above versioned repository. The toolbox launches the executable as a separate process.

## Noto Sans SC / Fax Sans interface subset

The interface fonts in `apps/windows-shell/ui/fonts` and `apps/windows-shell/assets/fonts` are renamed, static subsets of Noto Sans SC by the Noto Project authors, distributed under the SIL Open Font License 1.1. The original copyright and license are included in `ui/fonts/OFL.txt`. Source: https://github.com/notofonts/noto-cjk/tree/main/Sans/Variable/TTF/Subset . The subsets include the entire basic CJK block available upstream, punctuation, kana, full-width forms and interface characters, so dynamic Chinese is not limited to source strings. Other scripts and supplementary characters fall back to system fonts. Rebuild with `python scripts/build-ui-fonts.py --source path/to/NotoSansSC-VF.ttf` after installing `fonttools[woff]` and `brotli`.
