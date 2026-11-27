# Git workflow

## Review before commit

- Never run `git commit` (or `git push`) until the user has seen the code changes and said to go ahead.
- Before proposing a commit, show the change: list the changed files (`git status --short`) and the diff (`git diff`, or `git diff --staged`), or summarize each file's change with the relevant snippets.
- Wait for explicit approval of what was shown. A request like "fai commit" does not skip this step if the user has not yet seen the diff: show it first, then ask.
- If the user asks for further changes after reviewing, show the updated diff again before committing.
