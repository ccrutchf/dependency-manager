# dependency-manager

A single-binary CLI that installs packages declared in a YAML config across Linux package managers (apt, snap, flatpak, deb, pip, pipx, cargo, vscode, script).

Ship-as-one-file successor to https://github.com/clcrutch/dependency-manager.

## Quick start

```sh
depend plan --config packages.yaml    # preview resolved plan
depend install --config packages.yaml  # run as your user; escalates via sudo per command
depend test --config packages.yaml    # exit 0 if everything in the plan is installed
depend list                           # show which managers are available on this machine
depend update                         # update all packages via every available provider
```

See `examples/packages.yaml` for config shape.

## Machine tags

`platform`/`architecture`/`version` can't tell apart two machines on the same OS and
CPU. Machine tags can: one `packages.yaml` can serve several machines with different
package sets.

```yaml
linux-shared:              # untagged: every Linux machine
  platform: linux
  flatpak:
    app.zen_browser.zen:
linux-desktop:
  platform: linux
  tags: [desktop]          # only when a listed tag is active
  flatpak:
    org.gimp.GIMP:
vscode-extensions:
  platform: all
  exclude_tags: [crostini] # skipped when a listed tag is active
  vscode:
    ms-python.python:
```

- Active tags come from `--tag <name>` (repeatable, or comma-separated) on `plan`,
  `install`, `test` and `prune`. With no `--tag`, they come from `DEPEND_TAGS`
  (comma-separated). Any `--tag` replaces `DEPEND_TAGS` outright; `--tag ''` ignores it.
- Tag comparison is case-insensitive. `tags` and `exclude_tags` combine with the other
  filters (all must pass), and `exclude_tags` wins when both hit.
- A block with `tags:` is skipped when no tags are active. Blocks without either key
  behave exactly as before.
- `plan` prints the active tags, their source, and every block skipped because of tags.
- **Prune safety:** an active tag that no block mentions is probably a typo that
  silently shrank the plan. `install --prune` and `prune --apply` refuse to run (exit 1)
  if that happens; `plan`, `test`, plain `install` and dry-run `prune` only warn.
