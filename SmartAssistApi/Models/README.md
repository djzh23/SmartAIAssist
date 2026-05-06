Place ONNX embedding model assets in this directory.

Required files for the default zero-cost setup:

- `model-2.onnx` (all-MiniLM-L6-v2)
- `vocab.txt`
- `tokenizer.json` (optional; reserved for future tokenizer upgrades)

You can override paths via environment variables:

- `EMBEDDINGS__MODELPATH`
- `EMBEDDINGS__VOCABPATH`

Optional smaller model (paraphrase-MiniLM-L3-v2):

- `paraphrase-MiniLM-L3-v2.onnx`
- `paraphrase-MiniLM-L3-v2.vocab.txt`
- `paraphrase-MiniLM-L3-v2.tokenizer.json`

To switch to the smaller model, set:

- `EMBEDDINGS__MODELPATH=Models/paraphrase-MiniLM-L3-v2.onnx`
- `EMBEDDINGS__VOCABPATH=Models/paraphrase-MiniLM-L3-v2.vocab.txt`
