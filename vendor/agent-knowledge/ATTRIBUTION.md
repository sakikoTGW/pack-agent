# agent-knowledge (vendored)

Deterministic transcript parsing and session summary logic adapted from:

- **Repository:** https://github.com/keshrath/agent-knowledge
- **License:** MIT
- **Files:** `parser.ts`, `summary.ts` (adapted for agent-pack: in-memory JSONL, Cursor nested tool_use)

Upstream `agent-knowledge` provides cross-session memory, hybrid search, and optional LLM distillation.
agent-pack uses only the **deterministic** pre-extraction layer (topics, tools, files, git, errors, URLs, packages).
