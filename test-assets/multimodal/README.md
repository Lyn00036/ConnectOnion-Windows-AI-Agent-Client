# Multimodal Test Assets

This directory contains small, stable fixtures for manual and automated
multimodal testing.

## Images

- `images/workspace-scene.png`: generated desktop workspace scene for visual
  description and image upload tests.
- `images/shape-set.png`: generated five-object scene for visual counting,
  color, and object-relation tests.

## Files

- `files/brief.txt`: plain text attachment with short instructions.
- `files/notes.md`: Markdown attachment with headings and lists.
- `data/sample-observations.json`: structured JSON data.
- `data/measurements.csv`: tabular CSV data.
- `files/empty.txt`: zero-byte attachment for edge-case handling.

Use `manifest.json` as the stable index for file paths, MIME types, and
expected high-level content.
