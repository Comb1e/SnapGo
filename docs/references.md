# References

Projects used for in-process OCR:

- [RapidOCR](https://github.com/RapidAI/RapidOCR) — PP-OCRv5 ONNX models and dictionary. Det, rec, and cls files are the v3.10.0 ModelScope builds; SHA-256 is checked at download.
- [RapidOcrNet](https://github.com/BobLd/RapidOcrNet) — .NET PP-OCRv5 pipeline on ONNX Runtime and SkiaSharp. SnapGo uses 2.0.0 so it can pair with the DirectML runtime below.
- [PaddleOCR PP-OCRv5](https://github.com/PaddlePaddle/PaddleOCR) — source of the detection and Chinese/English recognition models. Apache-2.0.
- [ONNX Runtime DirectML](https://onnxruntime.ai/docs/execution-providers/DirectML-ExecutionProvider.html) — Windows GPU execution provider used when a DirectX 12 device is available. Package `Microsoft.ML.OnnxRuntime.DirectML` 1.24.3.
