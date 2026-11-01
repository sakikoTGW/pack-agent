# PAD Headless CLI Implementation Plan

**Goal:** Run a single DSH headless task using a PAD instance and return machine-readable results without exposing credentials.

**Architecture:** `Cli` routes `task run` to a separate headless runner. The runner resolves the instance's pinned release, workspace and credential source, then starts Node directly with stdin/stdout pipes and a timeout. It never writes a new credentials copy.

**Tech stack:** C#/.NET 9, DSH headless profile, Bun integration test, isolated temporary PAD roots.

1. Add an integration test using a fake Node-based DSH CLI. Run it against the existing PAD and confirm the task command fails.
2. Add the task runner and CLI route. Pass the UTF-8 prompt via stdin and `DEEPSEEK_API_KEY` via child environment, not command arguments. Generate JSON containing state, exit code, result and redacted diagnostic. Reject missing credentials; kill child process tree on timeout.
3. Exercise successful Chinese output, per-instance credential selection, failed child, missing credential, timeout, and secret redaction using the fake CLI. Build to a separate output directory to avoid replacing the running PAD.
4. Run the actual installed DSH CLI with the user's PAD credential using a short harmless prompt. Record the real outcome and limitations. Update CLI documentation and preserve existing unrelated work.
