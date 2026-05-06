Place ONNX embedding model assets in this directory.

Required files for the default zero-cost setup:

- `model.onnx`
- `vocab.txt`
- `tokenizer.json` (optional; reserved for future tokenizer upgrades)

You can override paths via environment variables:

- `EMBEDDINGS__MODELPATH`
- `EMBEDDINGS__VOCABPATH`
