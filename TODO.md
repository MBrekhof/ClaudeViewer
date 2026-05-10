# TODO

Open work for Claude Viewer, ordered roughly by usefulness.

## Done

- [x] **Compare mode.** Multi-select two rows in the grid → opens a tab with
  a vertical-splitter `SplitContainer` and an `ArtifactPanel` per side, both
  wired into the live-reload path. `ArtifactForm` was refactored to host a
  reusable `ArtifactPanel` so the compare tab can re-use the same renderer.
- [x] **Modified column shows full datetime.** `yyyy-MM-dd HH:mm` instead
  of just time-of-day; column widened to 130 px.
- [x] **Diff viewer for Markdown in compare mode.** Two MD files → DiffPlex
  side-by-side line diff, rendered into both panes with green/red/yellow
  line backgrounds and a line-number gutter. HTML / mixed pairs keep the
  current straight-render side-by-side. New files: `Services/DiffRenderer.cs`,
  `Services/FileReader.cs`. New API on `ArtifactPanel`: `LoadHtmlAsync`.

## Next up

- [ ] **Expose the built-in find panel.** `_gridView.OptionsFind.AlwaysVisible = true`
  puts a cross-column search box above the grid. Optionally also
  `OptionsView.ShowAutoFilterRow = true` for per-column filters. No custom
  textbox needed — DevExpress ships both.
- [ ] **Frontmatter parsing.** If a Markdown file starts with a YAML
  frontmatter block (`---` … `---`), surface `title`, `prompt`, and `tags`
  as columns. Lets Claude Code stamp metadata that's actually useful in the
  grid.

## Polish

- [ ] **Diff viewer: synchronized scrolling** between the two panes. Each
  side is its own WebView2 so a postMessage / scroll-event bridge is
  needed. Without it, long files drift out of alignment as you scroll.
- [ ] **Diff viewer: intra-line highlighting** for `Modified` rows.
  DiffPlex returns `SubPieces` per line — currently ignored.
- [ ] **Diff viewer: HTML source-level diff.** Re-use `DiffRenderer` for
  HTML/HTML pairs. Useful when both come from the same template; noisy
  otherwise. Consider a per-tab toggle.
- [ ] **Per-row "Open externally"** action — open in default browser,
  reveal in Explorer, copy path. Embedded ribbon button or context menu.
- [ ] **App icon (`.ico`) + product metadata** in `csproj`.
- [ ] **Single-file publish profile** (`dotnet publish -p:PublishSingleFile=true`)
  so the result is a single `.exe` to drop on any Windows box.
- [ ] **Dark theme for the Markdown + diff CSS.** Pair with the DevExpress
  dark skin and switch via `UserLookAndFeel.StyleChanged`.
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
