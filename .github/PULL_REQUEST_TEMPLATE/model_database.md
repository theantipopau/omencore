<!--
Model Database Submission — see docs/model-database/CONTRIBUTING_MODEL_DATABASE.md for the full process.
This template is for adding or correcting one board's entry in ModelCapabilityDatabase.cs.
-->

## Model

- **Product ID:**
- **Model name:**
- **CPU / GPU:**

## Submission file

- [ ] Added `docs/model-database/submissions/<productid>.json`
- [ ] Ran `python docs/model-database/validate_model_submission.py docs/model-database/submissions/<productid>.json --existing-db src/OmenCoreApp/Hardware/ModelCapabilityDatabase.cs` locally with no errors

## Evidence

- [ ] `sourceDiagnosticsExport: true` is accurate — the values in this submission came from a real diagnostics
      export or Guided Fan Diagnostic run on this exact board, not a guess or a copy from a similar model
- **Diagnostics export / diagnostic run attached or linked:**
- **Anything you're unsure about (leave blank fields as blank rather than guessing):**

## Is this a new entry or an update to an existing one?

- [ ] New model, not currently in the database
- [ ] Correction/update to an existing entry — old value was:
