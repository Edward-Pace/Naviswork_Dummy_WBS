# Naviswork Dummy WBS

A separate learning prototype for Navisworks Simulate / Manage 2027. It reads the hierarchy and properties of loaded models and exports a **proposed** work breakdown. It does not change geometry, properties, selection sets, viewpoints, visibility, or save the Navisworks document.

**This is a model-breakdown proposal, not an approved construction WBS or project-controls integration.** Quantities are provisional node/geometry counts, not material take-offs.

## Why vendor-neutral?

Imported models do not share one property schema. A fitting may store data on its parent while its Geometry child has almost none. Other vendors may use entirely different field names, or provide geometry only.

The prototype therefore:
1. Inventories every loaded hierarchy node and each property category/property exposed by the API, including internal names.
2. Profiles available fields by source model.
3. Applies explicit, editable mappings to semantic fields such as Area, Discipline, LineId, PipeRun and Tag.
4. Records whether each value is Direct, Ancestor, Missing or Conflict, with its origin node/property.
5. Builds a separate WBS export and retains unclassified geometry.

There is no AI inference, verified vendor detection, automatic discipline guessing, or dependency on SmartPlant metadata.

## Start here

Requirements: Windows, Visual Studio with .NET Framework 4.8 development tools, Navisworks Simulate or Manage 2027 API assemblies.

1. Clone this repository locally and open **Naviswork.DummyWbs.sln**.
2. In PowerShell at the repository root, run:
   ```powershell
   .\scripts\Build.ps1
   .\scripts\Test.ps1
   .\Demo\bin\Debug\DummyWbs.Demo.exe --demo .\artifacts\synthetic-demo
   ```
3. Open the generated folder's **summary.txt**, **property-profile.csv**, and **dummy-wbs.csv**.
4. Review the synthetic example before installing the host plugin.

Default API path: `C:\Program Files\Autodesk\Navisworks Simulate 2027`.
For another location, create an ignored `Directory.Build.local.props`:
```xml
<Project><PropertyGroup>
  <NavisworksInstallDirectory>C:\Program Files\Autodesk\Navisworks Manage 2027</NavisworksInstallDirectory>
</PropertyGroup></Project>
```

Build.ps1 builds and packages. Visual Studio's Build Solution compiles the projects; run Build.ps1 to assemble the installable bundle.

## Install and run a controlled model test

1. Close Navisworks.
2. Run ` .\scripts\Install.ps1 ` from the repository root.
3. Reopen Navisworks 2027 and an approved sample model.
4. Under the **Tool add-ins** tab, look for **Dummy WBS - Export Property Inventory**.
5. Select one small branch in the Selection Tree, run the command, keep Sample selected, and choose a local output folder.
6. Review the new output folder. Start with **property-profile.csv**, then tune mappings and replay the snapshot.

This bundle is separate from EPCS Navisworks Tools. No original EPCS code or real model files are included here.
Installer replacement requires `-ReplaceExisting` and retains a backup. Close Navisworks before replacement.

## Outputs

| File | Contents |
|---|---|
| nodes.csv | Every visited hierarchy node, parent link, source and geometry flags |
| properties.csv | Every enumerated direct property, display value, internal names and read errors |
| property-profile.csv | Field availability per inferred source; node coverage and error counts |
| model-snapshot.json | Literal inventory for offline regrouping without rescanning Navisworks |
| components.csv | Provisional component candidates, resolved fields, provenance and assigned WBS codes |
| dummy-wbs.csv | Hierarchical WBS rows with separate component and terminal-geometry counts |
| issues.csv | Conflicting properties, read failures, unresolved component candidates and nonterminal geometry |
| wbs-settings.json | Mapping settings used for this exact export |
| summary.txt | Scan totals, reconciliation counts and limitations |

Each run creates a new unique folder. A failed run may leave partial output: only regard a folder with a successfully written summary.txt and completed command as complete.

**Real model exports belong in ignored local-data or another approved local location. Do not commit them to GitHub.** The synthetic sample is fictional.

## How grouping works

The initial layout is:
```text
SourceModel
  Discipline (or Unclassified)
    LineId (or Unclassified)
      PipeRun (or Unclassified)
        Provisional component
```

This is configurable. Read [mapping instructions](docs/MAPPINGS.md) before treating proposed groupings as engineering classifications.

- Source model is inferred from the deepest filename-like tree node (.nwd/.nwc/.vue/.ifc etc.); it is **not verified vendor identity**.
- For every terminal geometry leaf, the nearest meaningfully named ancestor with a configured Tag, ItemId, ComponentType or LineId is a provisional owner.
- Multiple geometry leaves resolving to the same owner produce one component row.
- If there is no owner, retain the leaf as a provisional component and flag it.
- Ancestor field lookup stops at the inferred source boundary.
- Conflicting values at the nearest matching level remain unclassified and are logged; no arbitrary winner is selected.
- Identical group names in distinct source nodes stay separate.
- Counts reflect this heuristic. A line-level node with a mapped identifier can still aggregate several physical parts. Compare with manual samples; these are not certified physical-component counts.
- No data is inferred from an object's shape. Missing Area, Discipline or LineId is not silently guessed.

## Verification performed during development

- Built Core, Demo and the Navisworks 2027 host plugin against the installed 2027 API.
- Fourteen standalone tests cover mixed schemas, geometry deduplication, provenance, unclassified retention, conflicts, source boundaries, source overrides, read errors, aggregate reconciliation, deterministic ordering, scan limits and CSV escaping.
- Synthetic demo generates three source groups and preserves four terminal geometry leaves as three provisional components.
- **Real-model host execution, installation and performance are not yet verified.** Do not equate a successful build with a completed Navisworks integration test.

## Limits

- Synchronous in-process scan with native progress and Cancel. Version 0.1.1 defaults to a 1,000-node sample of one selected branch (with ancestor context), or model-root traversal if nothing is selected. A cooperative two-minute limit is checked between API calls; a stuck native call cannot be interrupted. Whole-model mode is explicit and retains the 250,000-node cap. Full snapshots consume memory and disk.
- Properties are exported as displayed strings, not original CAD typed values or normalized units.
- All properties exposed by the enumerated API categories are attempted; unavailable data is logged, not reconstructed.
- The proposed WBS uses terminal geometry. Geometry-bearing parent nodes are inventoried and flagged for coverage review.
- Snapshot node keys and generated WBS codes are not stable cross-revision identifiers.
- This code deliberately allows explicit ancestor mappings for the dummy WBS. It does **not** change the original EPCS chainage baseline's direct-leaf rule.
- No P6, EPCS database, IFC authoring, model rebuilding, chainage calculation, Excel workbook generation or saved-set creation is implemented in this prototype. CSVs can be opened in Excel.

## Code map

- **Core/**: snapshot records, mapping rules, grouping engine and CSV/JSON exports.
- **Plugin/**: one read-only Navisworks command and whole-model traversal.
- **Demo/**: fictional mixed-vendor data, standalone tests and offline replay.
- **scripts/**: build, test and separate per-user installation.
- **packaging/**: Navisworks 2027 application manifest.

API reference: [Autodesk property-category and data-property overview](https://blog.autodesk.io/navisworks-net-api-properties/).

## 0.1.1 recovery update

A real-model trial of 0.1.0 ran for approximately 30 minutes without useful progress feedback. It was stopped with user authorization; its stage and outputs were not verified. Version 0.1.1 adds sample scope, cooperative cancellation/time checks, progress logs and explicit RUN-STATUS.txt / scan-scope.txt files. A completed sample is not a complete model inventory; truncated descendants and counts require review. Native UI cancellation and actual selected-branch execution remain to be verified in Navisworks.
