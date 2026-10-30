# Text recognition dependencies

Text recognition uses the components below. The full Apache License 2.0 is in `assets/ocr/LICENSE`. It and these notices are embedded in the application and extracted beside the OCR libraries on first use.

- Tesseract native engine, distributed by the TesseractOCR 5.5.2 NuGet package. Package source revision: `787ebdb488184f47df3dcad7fe687b0b95d0d98c`. The native engine in this package is Tesseract 5.5.1. Upstream: https://github.com/tesseract-ocr/tesseract. Copyright Google Inc. and contributors.
- Leptonica 1.85.0, from the same package. Upstream: http://www.leptonica.org. Copyright Dan Bloomberg and contributors. Leptonica uses the BSD 2-Clause license, reproduced below.
- English, Latvian, Lithuanian, Estonian, Swedish, Finnish, and Russian recognition models from https://github.com/tesseract-ocr/tessdata_fast, revision `87416418657359cb625c412a48b6e1d6d41c29bd`. Copyright Google Inc. and contributors.

The TesseractOCR managed wrapper is not included. The app calls the native engine's C API directly. The NuGet package is used only as a pinned source of Windows x64 native libraries.

## Bundled model checksums

- `eng.traineddata`, SHA256 `7D4322BD2A7749724879683FC3912CB542F19906C83BCC1A52132556427170B2`
- `lav.traineddata`, SHA256 `7D9EAF22254B381F18B806CD6CB647862A397A7221ADBCE3E22CC421793A8847`
- `lit.traineddata`, SHA256 `1E383DF5B055583BC01CB5764ECDF74C540753F2CB3F8205E7105361DA4BC989`
- `est.traineddata`, SHA256 `515D4A773682B286369511E83FE412BCFF16A92A886F99C761C1D760A7E30456`
- `swe.traineddata`, SHA256 `F7304988D41F833EFEBCC2D529DF54B1903ECEBBC3DA1FAABD19A0FDDD4FE586`
- `fin.traineddata`, SHA256 `61A04CD62B507C3D9AE0E1CDA399E6715EBF49DEA9DF47897C8ACDCD3BD3E13C`
- `rus.traineddata`, SHA256 `E16E5E036CCE1D9EC2B00063CF8B54472625B9E14D893A169E2B0DEDEB4DF225`

## Leptonica license

Copyright (C) 2001-2020 Leptonica. All rights reserved.

Redistribution and use in source and binary forms, with or without modification, are permitted provided that the following conditions are met:

1. Redistributions of source code must retain the above copyright notice, this list of conditions and the following disclaimer.
2. Redistributions in binary form must reproduce the above copyright notice, this list of conditions and the following disclaimer in the documentation and/or other materials provided with the distribution.

THIS SOFTWARE IS PROVIDED BY THE COPYRIGHT HOLDERS AND CONTRIBUTORS "AS IS" AND ANY EXPRESS OR IMPLIED WARRANTIES, INCLUDING, BUT NOT LIMITED TO, THE IMPLIED WARRANTIES OF MERCHANTABILITY AND FITNESS FOR A PARTICULAR PURPOSE ARE DISCLAIMED. IN NO EVENT SHALL ANY CONTRIBUTORS BE LIABLE FOR ANY DIRECT, INDIRECT, INCIDENTAL, SPECIAL, EXEMPLARY, OR CONSEQUENTIAL DAMAGES (INCLUDING, BUT NOT LIMITED TO, PROCUREMENT OF SUBSTITUTE GOODS OR SERVICES; LOSS OF USE, DATA, OR PROFITS; OR BUSINESS INTERRUPTION) HOWEVER CAUSED AND ON ANY THEORY OF LIABILITY, WHETHER IN CONTRACT, STRICT LIABILITY, OR TORT (INCLUDING NEGLIGENCE OR OTHERWISE) ARISING IN ANY WAY OUT OF THE USE OF THIS SOFTWARE, EVEN IF ADVISED OF THE POSSIBILITY OF SUCH DAMAGE.
