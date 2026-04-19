# dependency-manager

A single-binary CLI that installs packages declared in a YAML config across Linux package managers (apt, snap, flatpak).

Ship-as-one-file successor to https://github.com/clcrutch/dependency-manager.

## Quick start

```sh
depend plan --config packages.yaml    # preview resolved plan
sudo depend install --config packages.yaml
depend test --config packages.yaml    # exit 0 if everything in the plan is installed
```

See `examples/packages.yaml` for config shape.
