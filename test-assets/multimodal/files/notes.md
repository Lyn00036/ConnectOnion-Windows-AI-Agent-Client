# Field Notes

## Context

These notes are intended to travel with an image attachment in the same user
message. They provide a predictable file payload for mixed-input testing.

## Assertions

- The sender may include text, images, and files in one request.
- The receiver should preserve file names in `files_received` events.
- Image data should remain separate from generic file attachments.

## Prompt Seed

Describe the image first, then mention whether the attached notes add any
useful context.
