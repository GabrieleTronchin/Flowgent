# Module definitions

Mounted into the engine at `/definitions`. Put one folder per module here; the folder name is the module id:

```
definitions/
└── notes/
    ├── module.yaml
    ├── workflows/
    ├── demo/       # optional Demo-mode answers
    ├── prompts/
    ├── schemas/
    └── samples/
```

Then register the module on the engine (`Flowgent__Modules__<n>__Id=notes`), see [../README.md](../README.md#your-own-module).
This file is not a module: the engine only loads registered ids.
