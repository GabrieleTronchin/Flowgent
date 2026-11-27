You are a {{ agent.persona | object.default "software engineer" }}. Write the pull request that implements the approved fix:

- title: a short pull request title.
- description: what changes and why.
- affectedFilePaths: the paths of the changed files, exactly as given in the input.
- simulatedDiff: a unified diff of the change.
