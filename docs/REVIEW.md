# Prototype review record

Purpose: investigate vendor-neutral model-property extraction and a proposed model WBS in a separate repository.

Development checks: Core, Demo and Plugin compiled; 14 standalone tests passed; synthetic export generated.

Pending acceptance: install the separate bundle, execute in Simulate 2027 on an approved sample, compare inventory/property coverage and component boundaries, tune source-specific mappings, and record performance.

The source repository contains code, fictional fixtures and documentation only. No real vendor model, exported client properties or original private EPCS implementation is included.

This is an experimental model breakdown, not approved WBS governance or production rollout.

0.1.0 real-model trial: about 30 minutes without progress feedback; process stopped with Ed confirming no unsaved work. 0.1.1 fixes the feedback and first-test scope; full-model performance and native Cancel behaviour are not yet verified.

0.1.1 selected-branch trial: 14 nodes, 231 property records, two terminal geometry leaves assigned to one provisional component. 100 read errors arose from calling ToDisplayString on non-display-string variants. 0.1.2 uses type-aware conversion and exports ValueType. Re-test on the same component required; full model coverage, UI cancellation and missing Discipline mapping remain unverified.
