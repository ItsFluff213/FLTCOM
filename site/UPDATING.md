# Updating the public website

The public project site is `site/index.html` with local CSS, fonts and screenshots. It is separate from the live KSP browser display in `display/`. It has no build step, JavaScript or external trackers. Keep fonts local: use the bundled fonts and retain their SIL OFL notices, or use system fonts.

## Refresh screenshots

The current screenshots show the browser display, including demo telemetry. A screenshot illustrates the interface; it does not establish that an operation was verified in KSP. Keep captions accurate about the data shown.

The current screenshots are taken from the mock telemetry server in a headless browser, at 1600 × 1000, with the fixture named below. Start a fresh server for each shot (`node tools/mock-telemetry/server.mjs --fixture <name> --port <free port>`), because operations persist on the server, and capture with `tools/verify/display-shot.mjs`. The manifest is:

| Fixture and scene | Published file | Dimensions | Use |
| --- | --- | --- | --- |
| `kerbin-small`, station selected, zoomed in | `hero-kerbin.webp` | 1600 × 1000 | Hero and README |
| `kerbin-small`, Kerbal X launch to 100 km, briefed | `briefing-launch.webp` | 1600 × 1000 | Briefing gallery and README |
| `kerbin-small`, the same launch approved, at ignition | `live-burn.webp` | 1600 × 1000 | Live burn gallery and README |
| `crowded`, Mun Lander 1 landing at custom coordinates, mid-descent | `mun-descent.webp` | 1600 × 1000 | Landing gallery and README |
| `polar-ring`, a relay selected, centred on Kerbin | `polar-ring.webp` | 1600 × 1000 | Constellation gallery and README |
| `crowded`, station selected, centred on the Sun | `system-sun.webp` | 1600 × 1000 | System gallery and README |

These are lossless WebP files with no metadata. For a replacement:

1. Capture only the FLTCOM browser display. Prefer a synthetic fixture or a save with neutral vessel and operation names.
2. Inspect the full image, including tiny labels, browser chrome, save names and any overlays. Exclude personal data: email addresses, user names, local machine paths and account identifiers. The repository owner's GitHub name belongs only in repository URLs.
3. Exclude KSP game textures, logos and game art. Use only FLTCOM's own display graphics. Describe the style only as “a cinematic mission-briefing display”; do not add inspiration references.
4. Pick images that remain useful at phone width. Keep the source untouched, then encode the chosen copy losslessly as WebP. For example, from the repository root with Python and Pillow available:

   ```powershell
   python -c "from PIL import Image; Image.open('shot.png').save('site/img/hero-kerbin.webp', format='WEBP', lossless=True, method=6)"
   ```

5. Compare the output to the source at full size. Check fine text and lines, dimensions, file size, and that metadata contains no personal information. Do not copy source metadata into the published file.
6. Update the image's `src`, `width`, `height`, meaningful `alt` text and caption in `index.html`. Keep full-size image links working. Update the README image and this manifest if the hero changes. Preserve lazy loading for gallery images and eager loading for the hero.
7. Review the staged files before committing; stage specific published paths only.

When the display changes, refresh the hero, README images and gallery together. Do not leave old-interface screenshots presented as the current display; remove obsolete published images once no page links to them.

## Refresh feature status and roadmap

Last source review: **2026-11-17**. This checkout had no `docs/` directory. Read it when it becomes available, but confirm public claims against the implementation and relevant validation. Review both `src/` and `display/src/`; an interface label or mock screenshot alone is not evidence that the feature works in game.

The website sections are `#features`, `#gallery`, `#guide`, `#faq`, `#roadmap` and `#credits`. Update related sections and the README together when a feature changes.

| Public claim | Sources to inspect |
| --- | --- |
| Available orbit changes and units | `display/src/briefing/Composer.svelte`, `src/FLTCOM.Core/Planning/OrbitChange.cs`, `StandardOrbit.cs` |
| Briefing, stages, budgets and contingencies | `display/src/briefing/Briefing.svelte`, `src/FLTCOM.Core/Planning/Briefing.cs`, `src/FLTCOM.Core/Propulsion/` |
| Approval, warp, control and resume behavior | `src/FLTCOM.Core/Execution/MissionRunner.cs`, `src/FLTCOM/Execution/Runner.cs`, `ControlRelease.cs`, `display/src/briefing/Operations.svelte` |
| Named groups and even spacing | `display/src/briefing/Groups.svelte`, `src/FLTCOM.Core/Groups/FleetGroups.cs`, `src/FLTCOM.Core/Planning/GroupPlanner.cs` |
| Background burns and limitations | `src/FLTCOM/Execution/BackgroundExecutor.cs`, `src/FLTCOM.Core/Execution/BackgroundBurn.cs` |
| Fleet map and vessel details | `src/FLTCOM/Snapshot/`, `display/src/map/`, `display/src/hud/` |
| Localhost, port and toolbar | `src/FLTCOM.Server/HttpServer.cs`, `src/FLTCOM/PluginFiles.cs`, `src/FLTCOM/Ui/ToolbarButton.cs` |
| Career, signal and other mods | `src/FLTCOM/Execution/MissionDesk.cs`, `VesselTasks.cs`, `ControlRelease.cs`, `src/FLTCOM/Compat/ModDetector.cs` |
| Save persistence and load behavior | `src/FLTCOM/FleetScenario.cs`, `src/FLTCOM.Core/Execution/MissionRunner.cs` |
| Supported game version and packaging | `README.md`, `tools/build.ps1`, `tools/check-package.mjs`, release contents |
| Licence and font credits | `LICENSE`, README licence paragraph, `assets/fonts/SOURCES.md`, each font's `OFL.txt` |

Current implementation includes fleet tracking on a 3D map, approved orbit changes, launch to orbit, landings, moon and planet transfers, rendezvous and docking, satellite carriers with equatorial or tilted rings, automatic systems, named groups, even-spacing planning and background group burns. CKAN is **coming**, not an installation option yet; manual installation points to GitHub Releases.

Use “available” only after the implementation and relevant checks support it. Use “planned” for anything not built. Do not invent phase numbers, completion percentages, release dates or delivery promises. Change the review date only after repeating the source review; it is not a release date. When a phase lands, record the implemented behavior and remaining limits before changing its status.

Keep these current limits explicit where relevant:

- Background burns apply to group operations and require a vessel outside active physics range. A loaded nearby member cannot burn on rails.
- A common circular-orbit altitude does not itself align orbital planes. Recommend same-body, same-plane groups for even spacing.
- Career operations require maneuver planning and suitable SAS. With CommNet enabled, uncrewed ships need a home link to upload; crewed ships are exempt in the current implementation.
- A saved running operation holds on load until resumed. Do not promise that saves are risk-free; the mod is under development.
- Compatibility checks deny particular incompatible assemblies and detect competing MechJeb/kOS control. This is not proof of compatibility with every other mod.

### Attribution uncertainty

The root `LICENSE` and README identify FLTCOM as GPL-3.0. Keep the README licence paragraph intact. The two files in `src/FLTCOM.Core/TwoBody/` still have headers referring to MIT and explicitly saying their maths was not ported from MechJebLib. No current MechJeb maths port was identified in this review. Do not invent a file-level provenance or claim a confirmed existing port. Acknowledge MechJeb and the licence's allowance for reuse; verify actual ported files and preserve their original notices when such code lands. Resolving source-header provenance is separate from updating this website.

## Preview and review

From the repository root:

```powershell
python -m http.server 8000 --bind 127.0.0.1 --directory site
```

Open `http://127.0.0.1:8000/`. This previews the public site, not the live mod display at port 47800. Stop the server when finished.

- Check desktop and phone widths, including 320–390 px: no horizontal page overflow, readable copy, and uncropped screenshots.
- Follow every navigation, download, repository, image and licence link. Check image loading, captions and alt text; check the page with images disabled if practical.
- Navigate with the keyboard. Confirm visible focus, a working skip link, usable FAQ disclosures, and sensible reading order. Check enlarged text and reduced-motion preferences.
- Inspect network requests: content and fonts should come from the site itself; no trackers or remote font services.
- Run `git diff --check`; review the staged diff for personal data, unsupported feature claims and unintended source or staging files.

## GitHub Pages

The operator enables Pages in the repository's **Settings → Pages**, choosing **GitHub Actions** as the source. `.github/workflows/pages.yml` uploads `site/` directly using the official configure, upload-artifact and deploy actions. It runs on pushes to `main` and manual dispatch. There is no site build command.

The workflow's `github-pages` environment and deployment output provide the actual public URL. Until that URL is confirmed, the README links to the repository's Pages deployment page. Update public-site links after deployment or a custom-domain change, while retaining the rule that the owner's GitHub name appears only in repository URLs. Use relative local asset links so a project subpath works correctly.

Review and commit small, focused changes on the authorized branch. Enabling Pages, publishing, pushing, or switching branches are separate operator actions; these maintenance instructions do not authorize them.
