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

## Flatpak bundles

A flatpak entry normally installs from a remote (`source:`, default `flathub`). Give it a
`url:` instead to install a single-file `.flatpak` bundle, for apps whose vendor only ships
one (e.g. as a GitHub release asset):

```yaml
linux-desktop-flatpaks:
  platform: linux
  flatpak:
    io.github.crealityofficial.CrealityPrint:
      url: https://github.com/CrealityOfficial/CrealityPrint/releases/download/v7.3.0/CrealityPrint-Linux-flatpak_V7.3.0-Release_x86_64.flatpak
      sha256: 50a281ad21e73c7132ed20f1301ac8cc7c8cb4dfbea6b7d6afbeade6a8856d91   # optional
```

- The key must be the app id inside the bundle. depend reads it from the downloaded file
  and refuses to install a bundle whose app id doesn't match.
- Flathub is added first so the bundle's runtime can be installed from it. `scope:` works
  as for remote installs (`user` by default, `system` uses sudo).
- depend records the url and sha256 it installed (`~/.cache/depend/flatpaks.json`).
  Changing either in the config makes the entry count as missing, so the next `install`
  upgrades (or downgrades) the app in place. App data in `~/.var/app/<id>` is kept.
- Bundles usually carry no repository url, so `flatpak update` (and `depend update`) skip
  them silently. To update one, point `url:` at the new release.
- `install --prune` treats a bundle app like any other flatpak app: declared ones are kept.
  Removing an undeclared one also removes the `<name>-origin` remote flatpak created for it.

## VS Code extensions from a .vsix

A vscode entry normally installs from the Marketplace by id. Give it a `url:` instead to
install a `.vsix` file, for extensions that aren't on the Marketplace (e.g. a private one
built with `vsce package`):

```yaml
kairos-vscode:
  platform: linux
  tags: [desktop]
  vscode:
    phonedash.phone-dash-focus:
      url: ~/.local/share/phone-dash/phone-dash-focus.vsix
    someone.released-ext:
      url: https://example.com/released-ext-1.2.0.vsix
      sha256: 9f86d081884c7d659a2feaa0c55ad015a3bf4f1b2b0b822cd15d6c15b0f00a08   # optional
```

- `url:` can be an `http(s)://` url (downloaded), an absolute path, a `~/` path, or a
  `file://` url. Relative paths are rejected.
- The key must be the extension's `publisher.name`. depend reads it from
  `extension/package.json` inside the `.vsix` and refuses a file whose id doesn't match
  (case doesn't matter).
- depend runs `code --install-extension <file> --force` (or the `source:` binary) and
  records the url/path and the file's sha256 in `~/.cache/depend/vsix.json`.
- The entry counts as installed when the extension is listed by `code --list-extensions`
  and the record has the same url/path and sha256. Without a `sha256:` pin, a local file
  is hashed on every check, so rebuilding it reinstalls it on the next `install`. An
  `https` url is not re-downloaded just to check it: change the url or pin a new
  `sha256:` to upgrade. If a local file is missing but the extension is installed, it
  counts as installed; if it is missing and not installed, that entry fails and names the path.
- `depend update` skips extensions recorded as installed from a `.vsix`, so it never
  asks the Marketplace for them. Removing `url:` from an entry makes the next `install`
  reinstall it from the Marketplace, after which `update` covers it again.
- `install --prune` treats a `.vsix` extension like any other: declared ones are kept.

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
- In YAML, `tags`/`exclude_tags` take a list or a single value; either way values are
  comma-split, so `tags: desktop, laptop` is two tags.
- Tag comparison is case-insensitive. `tags` and `exclude_tags` combine with the other
  filters (all must pass), and `exclude_tags` wins when both hit.
- A block with `tags:` is skipped when no tags are active. Blocks without either key
  behave exactly as before.
- `plan` prints the active tags, their source, and every block skipped because of tags.
- **Prune safety:** a wrong or missing tag silently shrinks the plan, and a prune would
  then remove real packages. `install --prune` and `prune --apply` refuse to run (exit 1)
  when:
  - an active tag is not mentioned by any block (probably a typo); `plan`, `test`, plain
    `install` and dry-run `prune` only warn about this; or
  - no tags were given at all (no `--tag`, `DEPEND_TAGS` unset, e.g. under cron) but
    `tags:` blocks apply to this platform. Pass `--tag ''` to prune as an untagged
    machine on purpose. A machine whose tagged blocks are all for other platforms (an
    untagged Mac in a Linux-tagged config) isn't affected.
