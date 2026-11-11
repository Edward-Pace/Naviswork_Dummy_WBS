# Field mappings and review

## First inspect, then map

1. Export inventory from an approved test model.
2. Open property-profile.csv. Compare sources, category names, property names and availability.
3. Check a few actual values and node locations in properties.csv / nodes.csv.
4. Add a mapping only when you understand the engineering meaning.
5. Replay the snapshot and compare the proposed WBS with a manual sample.
6. Document exceptions and obtain review before adopting a structure.

A similar-looking name does not prove two fields have the same meaning.

## Settings

The installed plugin reads wbs-settings.json beside its DLL. Build.ps1 generates this file using the default mappings if it does not exist; it preserves existing bundle settings on later builds.

The exported copy records exactly which settings were used. Modify an exported copy and replay it before changing the installed settings.

Example source-specific mapping:
```json
{
  "Field": "LineId",
  "SourceContains": ".ifc",
  "Category": "VendorPset",
  "Property": "PipelineRef"
}
```

This means: for source names containing .ifc, resolve LineId using VendorPset > PipelineRef. The example vendor field is fictional; replace it with a field observed in your model.

Matching:
- Category and property names match case-insensitively after trimming. Internal names are also accepted.
- Category "*" explicitly matches any category. Narrow it after profiling to reduce ambiguity.
- Non-empty SourceContains is a case-insensitive source-name filter.
- Source-specific rules replace generic rules for that field on matching sources.
- Values are trimmed but their case is preserved.
- If distinct matching values appear at the nearest matching node, the field becomes Conflict. The code does not fall back to a higher ancestor to hide the conflict.

Supported semantic fields: Area, Discipline, System, LineId, PipeRun, Tag, ItemId, ComponentType.

WbsLevels can contain SourceModel and any supported semantic field, once each. For example:
```json
"WbsLevels": ["SourceModel", "Area", "Discipline", "System"]
```

Missing levels create explicit Unclassified groups. No property mapping is fabricated for disciplines inferred solely from hierarchy names.

## Replay without Navisworks

```powershell
.\Demo\bin\Debug\DummyWbs.Demo.exe --replay "<export>\model-snapshot.json" "<edited>\wbs-settings.json" ".\local-data\replays"
```

This generates a new output folder. It does not rewrite the original snapshot or outputs.

## Acceptance checks for a mixed-vendor trial

- [ ] Compare at least two vendor/source models.
- [ ] Confirm all expected sources were inventoried.
- [ ] Reconcile terminal geometry assigned with terminal geometry scanned.
- [ ] Check parent-only properties and provenance.
- [ ] Confirm geometry-only items remain visible as Unclassified.
- [ ] Review conflicts and read errors.
- [ ] Count a small assembly manually and compare provisional components versus geometry leaves.
- [ ] Verify no values leaked across source boundaries.
- [ ] Confirm repeated names in distinct sources stay separate.
- [ ] Record units and missing engineering metadata.
- [ ] Confirm all test output remains local and no original model was saved or edited.
