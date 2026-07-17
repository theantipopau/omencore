# Contributing a Model Capability Entry

OmenCore's per-model capability data (`src/OmenCoreApp/Hardware/ModelCapabilityDatabase.cs`) drives which
features are shown as available for your specific board — fan curves, RGB zones, undervolting, GPU power
boost, and more. It's hand-maintained today, sourced from individual Discord reports, which means every new
SKU currently has to go through the project owner personally. This process lets you submit a new or
corrected entry directly as a PR instead.

## Before you start

**Your board needs to actually be running OmenCore.** This process is for reporting *observed* behavior,
not guessing based on a similar-sounding model name. If your `ProductId` already shows up in the app's
"Model Capabilities" panel (Diagnostics tab) with "✓ User-verified profile", it's already covered — check
there first.

## Step 1 — Gather your data

1. Open OmenCore, go to the **Diagnostics** tab, and check the **Model Capabilities** panel. It shows your
   `ProductId` and whatever the app currently believes about your board (often an inferred/default profile
   if your board isn't in the database yet).
2. Click **Export Diagnostics** (Diagnostics tab or Settings tab) to generate a diagnostics bundle. This is
   the source of truth for your `ProductId`, WMI model name string, and current fan/RGB/undervolt behavior.
3. If you're not sure whether a feature works, test it rather than guessing:
   - Fan curves / independent curves: try the Fan Control tab's custom curve editor.
   - Real RPM readback: run the Guided Fan Diagnostic (Diagnostics tab) — if every result is tagged
     "(fan-level estimate)" rather than a real RPM, `supportsRpmReadback` is `false`.
   - Undervolt: try Advanced > Undervolt. AMD CPUs don't support this at all (`supportsUndervolt: false`).
   - RGB zones / light bar / per-key: check the Lighting tab.

## Step 2 — Fill out the submission JSON

Copy `docs/model-database/example-submission.json` to a new file and edit it. Field meanings, types, and
allowed values are documented in `docs/model-database/model-capabilities.schema.json` — every field mirrors
a property on the `ModelCapabilities` C# class 1:1, so nothing gets lost in translation during review.

Only `productId`, `modelName`, `modelYear`, `family`, `reportedBy`, and `sourceDiagnosticsExport` are
required. Everything else defaults to a sensible value (see the schema) — only include a field if your
board's actual behavior differs from that default, or if you want to explicitly confirm it matches.

**`sourceDiagnosticsExport` must be `true`.** Submissions not backed by a real export/diagnostic run on the
actual hardware will be rejected — this isn't a formality, it's the whole point of the evidence-gate
practice this project follows for anything hardware-behavior-related (see the project's `ROADMAP_v4.0.0.md`
for why: guessed capability data has caused real bugs before, like features silently appearing "supported"
on boards where they weren't).

## Step 3 — Validate locally

```
python docs/model-database/validate_model_submission.py path/to/your-submission.json \
    --existing-db src/OmenCoreApp/Hardware/ModelCapabilityDatabase.cs
```

No dependencies required beyond a Python 3 interpreter. Fix everything the validator reports as an `ERROR`
before opening a PR; `WARNING`s (e.g. "this ProductId already exists — is this an update?") won't block you
but should be addressed in your PR description.

## Step 4 — Open a PR

Use the **Model Database Submission** PR template (auto-selected when you name your branch or PR title
appropriately, or pick it manually from the PR template dropdown on GitHub). Attach your validated JSON file
under `docs/model-database/submissions/<your-productid>.json` and link the diagnostics export or Guided Fan
Diagnostic output you based it on.

## What happens next

A maintainer reviews the submission (schema validity is auto-checked by CI, not just local `git diff`
review) and converts an accepted entry into an `AddModel(new ModelCapabilities { ... })` call in
`ModelCapabilityDatabase.cs`, then marks it `UserVerified = true`. Your submitted JSON stays in
`docs/model-database/submissions/` as the auditable source record for that entry — if the C# entry is ever
questioned later, the original submission (and who reported it, and what it was based on) is still there.

**Why the database isn't just "load JSON at runtime" yet:** the JSON submission format exists to make
*contribution* easy, not to become a new runtime data-loading path. Merging still means writing a C# literal
by hand — a deliberate choice to keep this change pure tooling/process rather than adding new
capability-loading code to a hardware-control-adjacent subsystem, consistent with this project's evidence-gate
norm of treating anything fan/EC/thermal/OC/UV-adjacent carefully. If community submission volume grows
enough to justify a real runtime loader, that's a separate, larger piece of work with its own review.
