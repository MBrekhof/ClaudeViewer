# TODO

Open work for Claude Viewer, ordered roughly by usefulness.

## Next up

- [ ] **Compare mode.** Multi-select two rows in the grid → open a doc with a
  vertical `SplitContainer` and one `WebView2` per side. The "version A vs
  version B" view was the original argument for building a tool instead of
  using `live-server`.
- [ ] **Filter / search box** above the grid. `IncrementalSearch` on the
  `GridView` plus a text box that narrows by file name and title.
- [ ] **Frontmatter parsing.** If a Markdown file starts with a YAML
  frontmatter block (`---` … `---`), surface `title`, `prompt`, and `tags`
  as columns. Lets Claude Code stamp metadata that's actually useful in the
  grid.

## Polish

- [ ] **Per-row "Open externally"** action — open in default browser,
  reveal in Explorer, copy path. Embedded ribbon button or context menu.
- [ ] **App icon (`.ico`) + product metadata** in `csproj`.
- [ ] **Single-file publish profile** (`dotnet publish -p:PublishSingleFile=true`)
  so the result is a single `.exe` to drop on any Windows box.
- [ ] **Dark theme for the Markdown CSS.** Pair with the DevExpress dark
  skin and switch via `UserLookAndFeel.StyleChanged`.
- [ ] **Pin / favourite** flag on a row that survives across sessions
  (extend `Settings`).

## Quality of life

- [ ] Show a small toast / status when a watched file is added or
  overwritten while its tab isn't open.
- [ ] Persist window size, dock layout, and last-active tab between runs.
- [ ] Optional: register `BonusSkins` (separate `DevExpress.BonusSkins`
  assembly) for richer skin choices.

## Maybe / later

- [ ] Browser-style history list ("artifacts viewed this session")
  separate from the on-disk listing.
- [ ] Optional MCP tool that exposes "save artifact" / "list artifacts" so
  Claude Code can write to the watched folder via a server, not just files.
